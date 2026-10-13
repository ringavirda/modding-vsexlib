using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using NSubstitute.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>
/// A client-side twin of a server block entity, stood in a <see cref="TestWorld"/> so the block's own
/// client branch builds its animator and draws through the engine's animation code.
/// </summary>
/// <remarks>The twin is made of the server entity's class, reads the server's tree once, and is
/// initialised against <see cref="TestWorld.ClientApi"/>. Its client object cache, asset manager,
/// tesselator, renderer registry and tick listeners are fakes; the fake records what it is handed
/// and runs no game thread, so a renderer's main-thread start is queued and never runs. The first
/// shape a twin reads needs a game install; see <see cref="TestWorld.ClientApi"/>.</remarks>
public sealed class ClientTwin {
  private readonly TestWorld _world;

  internal ClientTwin(TestWorld world, BlockEntity server, BlockEntity twin) {
    _world = world;
    Server = server;
    Twin = twin;
  }

  /// <summary>The server entity this twin mirrors, as the server holds it.</summary>
  public BlockEntity Server { get; }

  /// <summary>The client-side instance, initialised against <see cref="TestWorld.ClientApi"/>.</summary>
  public BlockEntity Twin { get; }

  /// <summary>The animator the twin's <c>BEBehaviorAnimatable</c> holds, or null when the twin has no
  /// animatable behaviour or has not built its animator yet.</summary>
  public AnimatorBase? Animator =>
    Twin.GetBehavior<BEBehaviorAnimatable>()?.animUtil?.animator;

  /// <summary>Copies the server's tree onto the twin when the server marked its entity dirty since the
  /// last call; the mark is consumed either way.</summary>
  /// <returns>True when a tree was applied to the twin; false when the server marked nothing dirty.</returns>
  public bool Sync() {
    if (!_world.TakeServerDirty(Server.Pos))
      return false;
    var tree = new TreeAttribute();
    Server.ToTreeAttributes(tree);
    Twin.FromTreeAttributes(tree, _world.ClientApi.World);
    return true;
  }

  /// <summary>Advances the client frame clock <paramref name="n"/> frames of <paramref name="dt"/>
  /// seconds each: in every frame the captured client tick listeners whose interval has come due
  /// fire, each with the seconds since it last fired, then the captured renderers fire in stage order.</summary>
  /// <remarks>The clock belongs to the whole world, so the renderers and ticks of every twin stood in
  /// it advance together.</remarks>
  /// <param name="dt">Seconds per frame, above zero.</param>
  /// <param name="n">Number of frames, zero or more.</param>
  public void Frames(float dt, int n) => _world.StepClientFrames(dt, n);

  /// <summary>Makes a client twin of each server entity and initialises it against
  /// <see cref="TestWorld.ClientApi"/>.</summary>
  /// <remarks>Every twin is created and reads its server's tree before any is initialised, so an
  /// initialiser that reads a neighbour finds the neighbour's twin. Each entity's position must be
  /// placed in <paramref name="world"/> already, and each server entity initialised against
  /// <see cref="TestWorld.Api"/>.</remarks>
  /// <param name="world">The world the twins stand in.</param>
  /// <param name="servers">Server entities, one twin each, in the order returned.</param>
  /// <returns>One twin per server entity, in the order given.</returns>
  /// <exception cref="InvalidOperationException">The first shape read finds no game install.</exception>
  /// <exception cref="Exception">A twin's own <c>FromTreeAttributes</c> or <c>Initialize</c> throws; the
  /// twins already made are released and the exception passes through unchanged.</exception>
  public static ClientTwin[] Stand(
    TestWorld world,
    params BlockEntity[] servers
  ) {
    BlockEntity[] twins = servers.Select(world.NewClientTwin).ToArray();
    try {
      world.InitializeClientTwins(twins);
    } catch {
      foreach (BlockEntity twin in twins)
        world.ReleaseClientTwin(twin);
      throw;
    }
    return servers
      .Select((server, i) => new ClientTwin(world, server, twins[i]))
      .ToArray();
  }
}

public sealed partial class TestWorld {
  private sealed class ClientTick(
    Action<float> callback,
    int intervalMs,
    Action<Exception>? errorHandler,
    BlockEntity? owner,
    double startMs
  ) {
    public Action<float> Callback { get; } = callback;
    public int IntervalMs { get; } = intervalMs;
    public Action<Exception>? ErrorHandler { get; } = errorHandler;
    public BlockEntity? Owner { get; } = owner;
    public double LastMs { get; set; } = startMs;
  }

  private readonly Dictionary<string, object> _clientCache = new();
  private readonly Dictionary<BlockPos, BlockEntity> _clientTwins = new();
  private readonly HashSet<BlockPos> _serverDirty = new();
  private readonly HashSet<BlockPos> _clientDirty = new();
  private readonly List<(
    EnumRenderStage Stage,
    IRenderer Renderer,
    BlockEntity? Owner
  )> _clientRenderers = new();
  private readonly Dictionary<long, ClientTick> _clientTicks = new();
  private double _clientClockMs;
  private BlockEntity? _initialising;
  private readonly List<Action> _mainThreadTasks = new();
  private readonly Dictionary<string, string> _clientAssetRoots = new();
  private readonly HashSet<string> _clientShapeDomains = new();
  private AssetManager? _clientShapes;
  private long _nextClientListenerId;

  /// <summary>Points the client twins' shapes of <paramref name="domain"/> at
  /// <paramref name="directory"/>, a folder laid out as a mod's <c>assets/&lt;domain&gt;</c> with
  /// a <c>shapes</c> folder beneath it.</summary>
  /// <remarks>A domain not named here reads <c>RepoPaths.Assets</c>, and <c>game</c> reads the install's
  /// survival tree. Named before the domain's shapes are first read: the directory is read once, on
  /// that first read.</remarks>
  /// <param name="domain">The asset domain the shapes belong to, such as a mod id.</param>
  /// <param name="directory">The folder holding the domain's <c>shapes</c>, absolute.</param>
  /// <returns>This world.</returns>
  /// <exception cref="InvalidOperationException">The domain's shapes were already read.</exception>
  public TestWorld ClientAssets(string domain, string directory) {
    if (_clientShapeDomains.Contains(domain))
      throw new InvalidOperationException(
        $"The shapes of {domain} were already read; name the directory before the first read."
      );
    _clientAssetRoots[domain] = directory;
    return this;
  }

  internal BlockEntity NewClientTwin(BlockEntity server) {
    BlockEntity twin = NewBlockEntityLike(server, server.Block);
    twin.Pos = server.Pos.Copy();
    twin.Block = server.Block;
    var tree = new TreeAttribute();
    server.ToTreeAttributes(tree);
    twin.FromTreeAttributes(tree, ClientApi.World);
    return twin;
  }

  internal void InitializeClientTwins(IReadOnlyList<BlockEntity> twins) {
    foreach (BlockEntity twin in twins)
      _clientTwins[twin.Pos] = twin;
    foreach (BlockEntity twin in twins) {
      twin.Api = ClientApi;
      _initialising = twin;
      try {
        twin.Initialize(ClientApi);
      } finally {
        _initialising = null;
      }
    }
  }

  internal void ReleaseClientTwin(BlockEntity twin) {
    try {
      if (twin.Api != null)
        twin.OnBlockRemoved();
    } finally {
      if (
        _clientTwins.TryGetValue(twin.Pos, out BlockEntity? held)
        && ReferenceEquals(held, twin)
      )
        _clientTwins.Remove(twin.Pos);
      _clientRenderers.RemoveAll(r => ReferenceEquals(r.Owner, twin));
      foreach (
        long id in _clientTicks
          .Where(t => ReferenceEquals(t.Value.Owner, twin))
          .Select(t => t.Key)
          .ToList()
      )
        _clientTicks.Remove(id);
    }
  }

  internal bool TakeServerDirty(BlockPos pos) => _serverDirty.Remove(pos);

  internal bool ClientMarkedDirty(BlockPos pos) => _clientDirty.Contains(pos);

  internal int QueuedMainThreadTasks => _mainThreadTasks.Count;

  internal int ClientRendererCount => _clientRenderers.Count;

  internal int ClientTickCount => _clientTicks.Count;

  internal void StepClientFrames(float dt, int n) {
    for (int i = 0; i < n; i++) {
      _clientClockMs += dt * 1000.0;
      foreach (ClientTick tick in _clientTicks.Values.ToList()) {
        if (_clientClockMs - tick.LastMs < tick.IntervalMs)
          continue;
        float elapsed = (float)((_clientClockMs - tick.LastMs) / 1000.0);
        tick.LastMs = _clientClockMs;
        try {
          tick.Callback(elapsed);
        } catch (Exception e) when (tick.ErrorHandler != null) {
          tick.ErrorHandler(e);
        }
      }
      foreach (
        (EnumRenderStage stage, IRenderer renderer, _) in _clientRenderers
          .Where(r => FiresOnClient(r.Renderer))
          .OrderBy(r => r.Stage)
          .ToList()
      )
        renderer.OnRenderFrame(dt, stage);
    }
  }

  private static bool FiresOnClient(IRenderer renderer) =>
    renderer is AnimationUtil
    || (renderer.GetType().Namespace ?? "").StartsWith(
      "ExpandedLib",
      StringComparison.Ordinal
    );

  private IBlockAccessor BuildClientAccessor() {
    var a = Substitute.For<IBlockAccessor>();
    StubBlockReads(a);
    a.GetBlockEntity(Arg.Any<BlockPos>())
      .Returns(ci => ClientTwinAt(ci.Arg<BlockPos>()));
    SubstitutionContext
      .Current.GetCallRouterFor(a)
      .RegisterCustomCallHandlerFactory(_ => new TypedBlockEntityCallHandler(
        ClientTwinAt
      ));
    a.When(x => x.MarkBlockEntityDirty(Arg.Any<BlockPos>()))
      .Do(ci => _clientDirty.Add(ci.Arg<BlockPos>()));
    return a;
  }

  private BlockEntity? ClientTwinAt(BlockPos pos) =>
    _clientTwins.TryGetValue(pos, out BlockEntity? twin) ? twin : null;

  private void WireClientFake(ICoreClientAPI api) {
    var coreApi = (ICoreAPI)api;

    api.ObjectCache.Returns(_clientCache);
    coreApi.ObjectCache.Returns(_clientCache);

    var assets = Substitute.For<IAssetManager>();
    assets
      .TryGet(Arg.Any<AssetLocation>(), Arg.Any<bool>())
      .Returns(ci => ClientShape(ci.Arg<AssetLocation>(), ci.Arg<bool>()));
    api.Assets.Returns(assets);
    coreApi.Assets.Returns(assets);

    // A minimal mesh: the twin's renderer takes it, and nothing draws it.
    var tesselator = Substitute.For<ITesselatorAPI>();
    tesselator
      .When(x =>
        x.TesselateShape(
          Arg.Any<TesselationMetaData>(),
          Arg.Any<Shape>(),
          out Arg.Any<MeshData>()
        )
      )
      .Do(ci => ci[2] = new MeshData());
    api.Tesselator.Returns(tesselator);

    var events = Substitute.For<IClientEventAPI>();
    api.Event.Returns(events);
    coreApi.Event.Returns(events);
    WireRecipeMatching(events);
    events
      .When(x =>
        x.RegisterRenderer(
          Arg.Any<IRenderer>(),
          Arg.Any<EnumRenderStage>(),
          Arg.Any<string>()
        )
      )
      .Do(ci =>
        _clientRenderers.Add(
          (ci.ArgAt<EnumRenderStage>(1), ci.ArgAt<IRenderer>(0), _initialising)
        )
      );
    events
      .When(x =>
        x.UnregisterRenderer(Arg.Any<IRenderer>(), Arg.Any<EnumRenderStage>())
      )
      .Do(ci =>
        _clientRenderers.RemoveAll(r => r.Renderer == ci.ArgAt<IRenderer>(0))
      );
    events
      .When(x => x.EnqueueMainThreadTask(Arg.Any<Action>(), Arg.Any<string>()))
      .Do(ci => _mainThreadTasks.Add(ci.Arg<Action>()));
#if GAME_GE_1_22
    events
      .RegisterGameTickListener(
        Arg.Any<Action<float>>(),
        Arg.Any<BlockPos>(),
        Arg.Any<Action<Exception>>(),
        Arg.Any<int>(),
        Arg.Any<int>()
      )
      .Returns(ci =>
        AddClientTick(
          ci.Arg<Action<float>>(),
          ci.ArgAt<int>(3),
          ci.Arg<Action<Exception>>()
        )
      );
    events
      .RegisterGameTickListener(
        Arg.Any<Action<IWorldAccessor, BlockPos, float>>(),
        Arg.Any<BlockPos>(),
        Arg.Any<Action<Exception>>(),
        Arg.Any<int>(),
        Arg.Any<int>()
      )
      .Returns(ci =>
      {
        var callback = ci.Arg<Action<IWorldAccessor, BlockPos, float>>();
        BlockPos pos = ci.Arg<BlockPos>();
        return AddClientTick(
          dt => callback(ClientApi.World, pos, dt),
          ci.ArgAt<int>(3),
          ci.Arg<Action<Exception>>()
        );
      });
#else
    events
      .RegisterGameTickListener(
        Arg.Any<Action<float>>(),
        Arg.Any<Action<Exception>>(),
        Arg.Any<int>(),
        Arg.Any<int>()
      )
      .Returns(ci =>
        AddClientTick(
          ci.Arg<Action<float>>(),
          ci.ArgAt<int>(2),
          ci.Arg<Action<Exception>>()
        )
      );
#endif
    events
      .When(x => x.UnregisterGameTickListener(Arg.Any<long>()))
      .Do(ci => _clientTicks.Remove(ci.Arg<long>()));
  }

  private long AddClientTick(
    Action<float> callback,
    int intervalMs,
    Action<Exception>? errorHandler
  ) {
    long id = ++_nextClientListenerId;
    _clientTicks[id] = new ClientTick(
      callback,
      intervalMs,
      errorHandler,
      _initialising,
      _clientClockMs
    );
    return id;
  }

  private IAsset? ClientShape(AssetLocation location, bool load) {
    _clientShapes ??= NewClientShapeManager();
    if (_clientShapeDomains.Add(location.Domain))
      LoadClientShapes(_clientShapes, location.Domain);
    return _clientShapes.TryGet(location, load);
  }

  private AssetManager NewClientShapeManager() {
    var mgr = new AssetManager(
      Path.Combine(GameInstall(), "assets"),
      EnumAppSide.Client
    );
    mgr.InitAndLoadBaseAssets(Log);
    // The manager refuses lookups until every asset is marked loaded, as the game marks them.
    ReflectionHelpers.SetField(mgr, "allAssetsLoaded", true);
    return mgr;
  }

  private void LoadClientShapes(AssetManager mgr, string domain) {
    foreach (
      IAsset asset in new PathOrigin(domain, ClientAssetRoot(domain)).GetAssets(
        AssetCategory.shapes,
        shouldLoad: true
      )
    )
      mgr.Add(asset.Location, asset);
  }

  private string ClientAssetRoot(string domain) {
    if (_clientAssetRoots.TryGetValue(domain, out string? root))
      return root;
    if (domain != GlobalConstants.DefaultDomain)
      return RepoPaths.Assets(domain);
    return Path.Combine(GameInstall(), "assets", "survival");
  }

  private static string GameInstall() =>
    VsAssemblyResolver.InstallPath
    ?? throw new InvalidOperationException(
      "No game install found - set the game's env var or provision .game/<slug>."
    );
}
