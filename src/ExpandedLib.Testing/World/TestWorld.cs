using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NSubstitute.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Testing;

/// <summary>A headless, in-process stand-in for a Vintage Story server world, backing the
/// block-network test suite.</summary>
public sealed partial class TestWorld : IDisposable {
  private readonly Dictionary<BlockPos, Block> _blocks = new();
  private readonly Dictionary<BlockPos, BlockEntity> _blockEntities = new();
  private readonly Dictionary<int, Block> _blocksById = new();
  private readonly Dictionary<string, Block> _blocksByCode = new();
  private readonly Dictionary<string, Item> _itemsByCode = new();
  private readonly Dictionary<int, Item> _itemsById = new();
  private readonly Dictionary<string, Func<BlockEntity>> _beFactories = new();
  private int _nextItemId = 1;

  private double _totalDays;

  /// <summary>The block returned for any cell that has not been placed (id 0, code "game:air").</summary>
  public Block Air { get; }

  /// <summary>The network graph manager under test. Factories are registered via <see cref="RegisterNetwork"/>.</summary>
  public BlockNetworkModSystem Networks { get; } = new();

  /// <summary>The fake block accessor handed to every production network call.</summary>
  public IBlockAccessor Accessor { get; }

  /// <summary>The fake server world (calendar, item-drop spawning) exposed as <see cref="BlockNetworkModSystem.ServerWorld"/>.</summary>
  public IServerWorldAccessor World { get; }

  /// <summary>The calendar; <see cref="AdvanceDays"/> moves <c>TotalDays</c> for evaporation tests.</summary>
  public IGameCalendar Calendar { get; }

  /// <summary>A server-side core API wired to this world; assign it to a block entity's <c>Api</c>,
  /// or use <see cref="Attach"/>.</summary>
  public ICoreServerAPI Api { get; }

  /// <summary>A client-side core API wired to this world, for exercising a <c>ModSystem</c>'s
  /// <c>StartClientSide</c>.</summary>
  public ICoreClientAPI ClientApi { get; }

  /// <summary>Channel pairs handed out by <see cref="Channels"/>, keyed by channel name.</summary>
  private readonly Dictionary<string, TestChannels> _channels = new();

  /// <summary>The <see cref="RecordingLogger"/> wired as both <see cref="Api"/>'s and
  /// <see cref="World"/>'s <c>Logger</c>; a real object, not a substitute.</summary>
  public RecordingLogger Log { get; } = new();

  /// <summary>The bag behind <see cref="World"/>'s <c>Config</c> tree.</summary>
  public WorldConfigBag Config { get; } = new();

  /// <summary>The mod loader behind <see cref="Api"/>'s <c>ModLoader</c>, with "exlib" enabled by
  /// default.</summary>
  public TestModLoader Mods { get; } = new();

  /// <summary>Backs <see cref="Api"/>'s <c>LoadModConfig</c>/<c>StoreModConfig</c> with real files
  /// under a temp directory.</summary>
  public ModConfigFiles ConfigFiles { get; } = new();

  private readonly Dictionary<long, TickListener> _tickListeners = new();

  // Sim-time (ms) accrued toward each listener's next fire; ignored by FireBlockEntityTicks.
  private readonly Dictionary<long, int> _tickAccumMs = new();
  private long _nextListenerId;

  /// <summary>A captured block-entity tick listener: its callback and requested interval (ms).</summary>
  private readonly record struct TickListener(
    System.Action<float> Callback,
    int IntervalMs
  );

  /// <summary>Item stacks spawned by the simulation.</summary>
  public List<ItemStack> Drops { get; } = new();

  public TestWorld() {
    Air = TestBlocks.Configure(new Block(), "game:air", 0);
    _blocksById[0] = Air;

    Calendar = Substitute.For<IGameCalendar>();
    PushCalendar();

    Mods.Add("exlib", "1.0.0").Register(Networks);

    Accessor = BuildAccessor();
    World = BuildWorld();
    Api = BuildApi();
    World.Api.Returns(Api);
    ClientApi = BuildClientApi();

    // StartServerSide is not called; the server world is primed directly.
    ReflectionHelpers.SetProperty(
      Networks,
      nameof(Networks.ServerWorld),
      World
    );
  }

  /// <summary>Links <paramref name="be"/> to this world's API so it can resolve networks and ticks.</summary>
  public TestWorld Attach(BlockEntity be) {
    be.Api = Api;
    return this;
  }

  /// <summary>Builds a <see cref="TestPlayer"/> standing in this world.</summary>
  public TestPlayer Player(string uid = "test", string name = "Tester") =>
    TestPlayer.Create(this, uid, name);

  /// <summary>Deletes <see cref="ConfigFiles"/>'s temp directory.</summary>
  public void Dispose() => ConfigFiles.Dispose();

  /// <summary>The client/server channel pair for <paramref name="channelName"/>, built the first
  /// time it is asked for and memoised after.</summary>
  public TestChannels Channels(string channelName) {
    if (!_channels.TryGetValue(channelName, out TestChannels? channels)) {
      channels = TestChannels.Create(this, channelName);
      _channels[channelName] = channels;
    }
    return channels;
  }

  /// <summary>Runs <paramref name="be"/> through its real <see cref="BlockEntity.Initialize"/>
  /// against this world's API; must already be <see cref="Place"/>d.</summary>
  public TestWorld Initialize(BlockEntity be) {
    be.Api = Api;
    be.Initialize(Api);
    return this;
  }

  #region Setup

  /// <summary>Registers a typed-network factory, exactly as a mod would in <c>ModSystem.Start</c>.</summary>
  public TestWorld RegisterNetwork(
    string networkType,
    System.Func<BlockNetworkModSystem, BlockNetwork> factory
  ) {
    Networks.RegisterNetworkType(networkType, () => factory(Networks));
    return this;
  }

  /// <summary>Places <paramref name="block"/> (and optional <paramref name="be"/>) at
  /// <paramref name="pos"/>, registering the block in the id/code lookup.</summary>
  public TestWorld Place(BlockPos pos, Block block, BlockEntity? be = null) {
    Register(block);
    _blocks[pos] = block;
    if (be != null) {
      be.Pos = pos.Copy();
      be.Block = block;
      _blockEntities[pos] = be;
    }
    return this;
  }

  /// <summary>Places a network node at <paramref name="pos"/>, running the real
  /// <see cref="Initialize"/> that registers the cell. <see cref="RegisterNetwork"/> must have run
  /// for <paramref name="networkType"/> first.</summary>
  public TestWorld PlaceNode(
    BlockPos pos,
    string networkType,
    string orientation,
    int id = 1
  ) {
    TestMemberBlockEntity be = TestMemberBlockEntity.Carrying(networkType);
    Place(pos, TestNetworkBlock.Create(networkType, orientation, id), be);
    return Initialize(be);
  }

  /// <summary>Places a cell whose membership alone puts it on the graph, under a block entity
  /// declaring its own <paramref name="connectors"/>.</summary>
  public TestWorld PlaceMemberBlock(
    BlockPos pos,
    string networkType,
    string connectors,
    int id = 899
  ) {
    TestMemberBlockEntity be = TestMemberBlockEntity.Declaring(
      networkType,
      connectors
    );
    Place(pos, TestBlocks.Configure(new Block(), $"test:member-{id}", id), be);
    return Initialize(be);
  }

  /// <summary>The one shared <see cref="BlockStructureFiller"/> this world places footprint cells
  /// from, created on first use.</summary>
  public BlockStructureFiller Filler =>
    _filler ??= TestBlocks.Configure(
      new BlockStructureFiller(),
      "exlib:structurefiller",
      897
    );

  private BlockStructureFiller? _filler;

  /// <summary>The registered class code of the network-membership behaviour, as a
  /// <c>fillerOffsets</c> cell names it.</summary>
  public const string NetworkMemberClass = "exlib.BEBehaviorNetworkMember";

  /// <summary>Places a mega-block footprint cell at <paramref name="pos"/> over a filler block
  /// entity, running the real <see cref="Initialize"/> that creates its hosted
  /// behaviours.</summary>
  public TestWorld PlaceFiller(
    BlockPos pos,
    FillerBehavior[]? hosted = null,
    BlockPos? principal = null
  ) {
    var be = new BlockEntityStructureFiller {
      Principal = (principal ?? pos.AddCopy(0, -1, 0)).Copy(),
      HostedBehaviors = hosted,
    };
    Place(pos, Filler, be);
    return Initialize(be);
  }

  /// <summary>Places a footprint cell that is a graph node in its own right, on
  /// <paramref name="networkType"/>. <paramref name="orientation"/> is one side letter, or two
  /// naming an opposite pair for a pass-through cell.</summary>
  public TestWorld PlaceFillerNode(
    BlockPos pos,
    string networkType,
    string orientation,
    BlockPos? principal = null
  ) {
    RegisterBlockEntityBehaviorFactory(
      NetworkMemberClass,
      be => new BEBehaviorNetworkMember(be)
    );
    (BlockFacing face, bool passThrough) = ReadOrientation(orientation);
    var props = new JObject { ["networkType"] = networkType };
    if (passThrough)
      props["passThrough"] = true;
    return PlaceFiller(
      pos,
      [new FillerBehavior(NetworkMemberClass, face, new JsonObject(props))],
      principal
    );
  }

  /// <summary>Reads a one- or two-letter orientation as the coupling face plus whether the run
  /// passes through to its opposite.</summary>
  private static (BlockFacing Face, bool PassThrough) ReadOrientation(
    string orientation
  ) {
    BlockFacing[] faces =
    [
      .. orientation
        .Select(c => BlockNetworkModSystem.SideToFace(c.ToString()))
        .OfType<BlockFacing>()
        .Distinct(),
    ];
    return faces switch {
      [BlockFacing one] => (one, false),
      [BlockFacing a, BlockFacing b] when a.Opposite == b => (a, true),
      _ => throw new ArgumentException(
        $"A filler cell couples on one face, or on two opposite ones; '{orientation}' names neither.",
        nameof(orientation)
      ),
    };
  }

  /// <summary>Registers a factory the fake class registry builds <paramref name="classname"/> from,
  /// the headless stand-in for the behaviour registry.</summary>
  public TestWorld RegisterBlockEntityBehaviorFactory(
    string classname,
    System.Func<BlockEntity, BlockEntityBehavior> factory
  ) {
    Api.ClassRegistry.CreateBlockEntityBehavior(
        Arg.Any<BlockEntity>(),
        classname
      )
      .Returns(ci => factory(ci.Arg<BlockEntity>()));
    return this;
  }

  /// <summary>Registers a factory that <c>BlockAccessor.SpawnBlockEntity(classname, pos)</c> uses
  /// for <paramref name="classname"/>.</summary>
  public TestWorld RegisterBlockEntityFactory(
    string classname,
    Func<BlockEntity> factory
  ) {
    _beFactories[classname] = factory;
    return this;
  }

  /// <summary>Registers a block in the id/code lookup without placing it (for orientation-variant swaps).</summary>
  public TestWorld Register(Block block) {
    _blocksById[block.BlockId] = block;
    if (block.Code != null)
      _blocksByCode[block.Code.ToString()] = block;
    return this;
  }

  /// <summary>Registers a real, already-resolved <see cref="Item"/> in the id/code lookup.</summary>
  public TestWorld Register(Item item) {
    _itemsById[item.ItemId] = item;
    if (item.Code != null)
      _itemsByCode[item.Code.ToString()] = item;
    return this;
  }

  /// <summary>Registers a resolvable <see cref="Item"/> under <paramref name="code"/>.
  /// <paramref name="meltingPoint"/> is in degrees C; a known vanilla fuel code gets its own
  /// shipped burn figures when <paramref name="burnTemperature"/>/<paramref name="burnDuration"/>
  /// are left at 0.</summary>
  /// <returns>The created item.</returns>
  public Item RegisterItem(
    string code,
    float meltingPoint = 0f,
    float burnTemperature = 0f,
    float burnDuration = 0f
  ) {
    // Unique non-zero id: ItemStack.ResolveBlockOrItem re-resolves a stack by id.
    var item = new Item {
      Code = new AssetLocation(code),
      ItemId = _nextItemId++,
    };
    (float defaultTemp, float defaultDuration) = VanillaFuelBurn(code);
    float temp = burnTemperature > 0f ? burnTemperature : defaultTemp;
    float duration = burnDuration > 0f ? burnDuration : defaultDuration;
    if (meltingPoint > 0f || temp > 0f)
      item.CombustibleProps = new CombustibleProperties {
        MeltingPoint = (int)meltingPoint,
        BurnTemperature = (int)temp,
        BurnDuration = (int)duration,
      };
    // CollectibleObject.Equals reads api.World directly; an item without one throws on comparison.
    ReflectionHelpers.SetField(item, "api", Api);
    _itemsByCode[code] = item;
    _itemsById[item.ItemId] = item;
    return item;
  }

  /// <summary>Vanilla's own burn temperature and duration for a handful of real fuel codes; empty
  /// for anything else.</summary>
  private static (float temperature, float duration) VanillaFuelBurn(
    string code
  ) =>
    code switch {
      "game:coke" => (1340f, 40f),
      "game:charcoal" => (1300f, 40f),
      "game:ore-bituminouscoal" => (1200f, 84f),
      "game:ore-anthracite" => (1200f, 196f),
      "game:ore-lignite" => (1100f, 77f),
      _ => (0f, 0f),
    };

  public Item? GetItem(AssetLocation? code) =>
    code != null && _itemsByCode.TryGetValue(code.ToString(), out var i)
      ? i
      : null;

  public Item? GetItem(int id) =>
    _itemsById.TryGetValue(id, out var i) ? i : null;

  #endregion

  #region Store access

  /// <summary>What the store holds at <paramref name="pos"/>, whether or not its chunk is
  /// loaded.</summary>
  public Block GetBlock(BlockPos pos) =>
    _blocks.TryGetValue(pos, out var b) ? b : Air;

  /// <summary>What the store holds at <paramref name="pos"/>, whether or not its chunk is
  /// loaded.</summary>
  public BlockEntity? GetBlockEntity(BlockPos pos) =>
    _blockEntities.TryGetValue(pos, out var be) ? be : null;

  #endregion

  #region Chunk loading

  private readonly HashSet<Vec3i> _unloadedChunks = new();
  private readonly IWorldChunk _loadedChunk = Substitute.For<IWorldChunk>();

  /// <summary>The chunk every loaded position resolves to; one instance for the whole
  /// world.</summary>
  public IWorldChunk LoadedChunk => _loadedChunk;

  /// <summary>The chunk coordinate <paramref name="pos"/> falls in, dimension-aware through
  /// <c>InternalY</c>.</summary>
  private static Vec3i ChunkOf(BlockPos pos) =>
    new(
      pos.X / GlobalConstants.ChunkSize,
      pos.InternalY / GlobalConstants.ChunkSize,
      pos.Z / GlobalConstants.ChunkSize
    );

  /// <summary>Whether the chunk holding <paramref name="pos"/> is loaded.</summary>
  public bool IsChunkLoaded(BlockPos pos) =>
    !_unloadedChunks.Contains(ChunkOf(pos));

  /// <summary>Hides every cell in the chunk holding <paramref name="pos"/> from
  /// <see cref="Accessor"/>: its blocks read as <see cref="Air"/>, its block entities and
  /// <c>GetChunkAtBlockPos</c> as <c>null</c>.</summary>
  public TestWorld UnloadChunkAt(BlockPos pos) {
    _unloadedChunks.Add(ChunkOf(pos));
    return this;
  }

  /// <summary>Brings back the chunk holding <paramref name="pos"/>.</summary>
  public TestWorld LoadChunkAt(BlockPos pos) {
    _unloadedChunks.Remove(ChunkOf(pos));
    return this;
  }

  /// <summary>What <see cref="Accessor"/> sees at <paramref name="pos"/>: the placed block, or
  /// <see cref="Air"/> when its chunk is away.</summary>
  private Block ReadBlock(BlockPos pos) =>
    IsChunkLoaded(pos) ? GetBlock(pos) : Air;

  /// <summary>What <see cref="Accessor"/> sees at <paramref name="pos"/>: the live block entity, or
  /// <c>null</c> when its chunk is away.</summary>
  private BlockEntity? ReadBlockEntity(BlockPos pos) =>
    IsChunkLoaded(pos) ? GetBlockEntity(pos) : null;

  #endregion

  #region Graph passthrough

  public void AddNode(BlockPos pos, string networkType) =>
    Networks.AddNode(Accessor, pos, networkType);

  public void RemoveNode(BlockPos pos) => Networks.RemoveNode(Accessor, pos);

  public BlockNetwork? NetworkAt(BlockPos pos) => Networks.GetNetworkAt(pos);

  #endregion

  #region Neighbours

  /// <summary>Fires <see cref="Block.OnNeighbourBlockChange"/> on the six blocks adjacent to
  /// <paramref name="changedPos"/>. Opt-in only.</summary>
  public TestWorld NotifyNeighbours(BlockPos changedPos) {
    foreach (BlockFacing face in BlockFacing.ALLFACES) {
      BlockPos nPos = changedPos.AddCopy(face);
      GetBlock(nPos).OnNeighbourBlockChange(World, nPos, changedPos);
    }
    return this;
  }

  #endregion

  #region Time

  /// <summary>Advances the simulation by <paramref name="seconds"/> server ticks through
  /// <see cref="BlockNetworkModSystem.ServerTick"/>.</summary>
  public void Tick(int seconds = 1) {
    for (int i = 0; i < seconds; i++)
      Networks.ServerTick(Accessor, 1f);
  }

  /// <summary>Fires every block-entity server tick listener registered through <see cref="Api"/>
  /// (i.e. via <c>BlockEntity.RegisterGameTickListener</c>), <paramref name="times"/> times.</summary>
  public void FireBlockEntityTicks(float dt = 1f, int times = 1) {
    for (int i = 0; i < times; i++)
      foreach (var listener in _tickListeners.Values.ToList())
        listener.Callback(dt);
  }

  /// <summary>Advances block-entity sim time by <paramref name="totalMs"/> ms, firing each listener
  /// once per whole interval it registered.</summary>
  public void AdvanceBlockEntityTime(int totalMs) {
    // Snapshot: a listener may unregister (or a block entity may register a new one) while firing.
    foreach (long id in _tickListeners.Keys.ToList()) {
      if (!_tickListeners.TryGetValue(id, out TickListener listener))
        continue; // already removed by an earlier callback this pass
      int interval = System.Math.Max(1, listener.IntervalMs);
      int accum = _tickAccumMs.TryGetValue(id, out int a) ? a : 0;
      accum += totalMs;
      float dt = interval / 1000f;
      while (accum >= interval && _tickListeners.ContainsKey(id)) {
        accum -= interval;
        listener.Callback(dt);
      }
      // The listener may have been torn down by its own callback; only keep live remainders.
      if (_tickListeners.ContainsKey(id))
        _tickAccumMs[id] = accum;
    }
  }

  /// <summary>Moves the calendar forward without ticking, for calendar-driven effects (evaporation).</summary>
  public void AdvanceDays(double days) {
    _totalDays += days;
    PushCalendar();
  }

  /// <summary>Moves the calendar forward by game hours without ticking (for away-catch-up tests).</summary>
  public void AdvanceHours(double hours) => AdvanceDays(hours / 24.0);

  private void PushCalendar() {
    Calendar.TotalDays.Returns(_totalDays);
    Calendar.TotalHours.Returns(_totalDays * 24.0);
  }

  #endregion

  #region Lifecycle

  /// <summary>Models a save, chunk unload and reload of the block entity at
  /// <paramref name="pos"/>.</summary>
  /// <returns>The new instance, or null if none was placed.</returns>
  public BlockEntity? Reload(BlockPos pos) {
    BlockEntity? old = GetBlockEntity(pos);
    if (old == null)
      return null;

    Block block = GetBlock(pos);

    var tree = new TreeAttribute();
    old.ToTreeAttributes(tree);

    // A network node keeps its graph node: base OnBlockUnloaded does not RemoveNode.
    old.OnBlockUnloaded();
    _blockEntities.Remove(pos);

    BlockEntity fresh = NewBlockEntityLike(old, block);
    fresh.Pos = pos.Copy();
    fresh.Block = block;
    fresh.Api = Api;
    _blockEntities[pos] = fresh;
    fresh.FromTreeAttributes(tree, World);
    fresh.Initialize(Api);
    return fresh;
  }

  /// <summary>Models the block-entity half of a chunk unload at <paramref name="pos"/>: runs its
  /// real <c>OnBlockUnloaded</c> and drops the instance, leaving the block placed.</summary>
  public void Unload(BlockPos pos) {
    GetBlockEntity(pos)?.OnBlockUnloaded();
    _blockEntities.Remove(pos);
  }

  /// <summary>Creates a fresh block entity of the same class the engine would instantiate on
  /// load.</summary>
  private BlockEntity NewBlockEntityLike(BlockEntity old, Block block) {
    string? classname = block?.EntityClass ?? old.Block?.EntityClass;
    if (
      classname != null
      && _beFactories.TryGetValue(classname, out var factory)
    )
      return factory();
    return (BlockEntity)Activator.CreateInstance(old.GetType())!;
  }

  #endregion

  #region Fake wiring

  private IBlockAccessor BuildAccessor() {
    var a = Substitute.For<IBlockAccessor>();

    a.GetBlock(Arg.Any<BlockPos>())
      .Returns(ci => ReadBlock(ci.Arg<BlockPos>()));
    // The fluid/solid-layer overload (BlockLayersAccess) reads the same store.
    a.GetBlock(Arg.Any<BlockPos>(), Arg.Any<int>())
      .Returns(ci => ReadBlock(ci.Arg<BlockPos>()));
    // The one accessor call that can tell an absent cell from an unreadable one.
    a.GetChunkAtBlockPos(Arg.Any<BlockPos>())
      .Returns(ci => IsChunkLoaded(ci.Arg<BlockPos>()) ? _loadedChunk : null);
    // Coordinate overloads vanilla's multiblock code reads through; the int overload is obsolete
    // but still called.
#pragma warning disable CS0618
    a.GetBlock(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
      .Returns(ci =>
        ReadBlock(
          new BlockPos(ci.ArgAt<int>(0), ci.ArgAt<int>(1), ci.ArgAt<int>(2))
        )
      );
#pragma warning restore CS0618
    a.GetBlockRaw(
        Arg.Any<int>(),
        Arg.Any<int>(),
        Arg.Any<int>(),
        Arg.Any<int>()
      )
      .Returns(ci =>
        ReadBlock(
          new BlockPos(ci.ArgAt<int>(0), ci.ArgAt<int>(1), ci.ArgAt<int>(2))
        )
      );
    a.GetBlockEntity(Arg.Any<BlockPos>())
      .Returns(ci => ReadBlockEntity(ci.Arg<BlockPos>()));
    // Resolve-by-code; orientation behaviours read a null here as an undeclared variant.
    a.GetBlock(Arg.Any<AssetLocation>())
      .Returns(ci => GetByCode(ci.Arg<AssetLocation>()));

    a.When(x => x.SetBlock(Arg.Any<int>(), Arg.Any<BlockPos>()))
      .Do(ci => DoSetBlock(ci.ArgAt<int>(0), ci.ArgAt<BlockPos>(1)));
    // The placement overload; left unwired, a real TryPlaceBlock would place nothing.
    a.When(x =>
        x.SetBlock(Arg.Any<int>(), Arg.Any<BlockPos>(), Arg.Any<ItemStack>())
      )
      .Do(ci => DoSetBlock(ci.ArgAt<int>(0), ci.ArgAt<BlockPos>(1)));
    a.When(x => x.ExchangeBlock(Arg.Any<int>(), Arg.Any<BlockPos>()))
      .Do(ci => DoExchangeBlock(ci.ArgAt<int>(0), ci.ArgAt<BlockPos>(1)));
    a.When(x => x.MarkBlockDirty(Arg.Any<BlockPos>())).Do(_ => { });
    a.When(x =>
        x.SpawnBlockEntity(
          Arg.Any<string>(),
          Arg.Any<BlockPos>(),
          Arg.Any<ItemStack>()
        )
      )
      .Do(ci => DoSpawnBlockEntity(ci.ArgAt<string>(0), ci.ArgAt<BlockPos>(1)));
    a.When(x =>
        x.BreakBlock(Arg.Any<BlockPos>(), Arg.Any<IPlayer>(), Arg.Any<float>())
      )
      .Do(ci => DoBreak(ci.ArgAt<BlockPos>(0)));

    // WalkBlocks over an inclusive box, reading the store cell by cell.
    a.When(x =>
        x.WalkBlocks(
          Arg.Any<BlockPos>(),
          Arg.Any<BlockPos>(),
          Arg.Any<System.Action<Block, int, int, int>>(),
          Arg.Any<bool>()
        )
      )
      .Do(ci =>
        DoWalkBlocks(
          ci.ArgAt<BlockPos>(0),
          ci.ArgAt<BlockPos>(1),
          ci.ArgAt<System.Action<Block, int, int, int>>(2)
        )
      );

    return a;
  }

  private void DoWalkBlocks(
    BlockPos min,
    BlockPos max,
    System.Action<Block, int, int, int> onBlock
  ) {
    int x0 = System.Math.Min(min.X, max.X),
      x1 = System.Math.Max(min.X, max.X);
    int y0 = System.Math.Min(min.Y, max.Y),
      y1 = System.Math.Max(min.Y, max.Y);
    int z0 = System.Math.Min(min.Z, max.Z),
      z1 = System.Math.Max(min.Z, max.Z);
    for (int x = x0; x <= x1; x++)
      for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
          onBlock(GetBlock(new BlockPos(x, y, z, min.dimension)), x, y, z);
  }

  private IServerWorldAccessor BuildWorld() {
    var w = Substitute.For<IServerWorldAccessor>();
    w.BlockAccessor.Returns(Accessor);
    w.Calendar.Returns(Calendar);
    w.Logger.Returns(Log);
    w.Config.Returns(Config.Tree);
    // Particle/sound helpers read world.Rand.
    w.Rand.Returns(new Random(1));
    w.GetBlock(Arg.Any<AssetLocation>())
      .Returns(ci => GetByCode(ci.Arg<AssetLocation>()));
    w.GetBlock(Arg.Any<int>())
      .Returns(ci =>
        _blocksById.TryGetValue(ci.Arg<int>(), out var b) ? b : Air
      );
    w.GetItem(Arg.Any<AssetLocation>())
      .Returns(ci => GetItem(ci.Arg<AssetLocation>()));
    w.GetItem(Arg.Any<int>()).Returns(ci => GetItem(ci.Arg<int>()));
    w.When(x =>
        x.SpawnItemEntity(
          Arg.Any<ItemStack>(),
          Arg.Any<Vec3d>(),
          Arg.Any<Vec3d>()
        )
      )
      .Do(ci => Drops.Add(ci.Arg<ItemStack>()));
    // The full registries a content check enumerates; computed on each read.
    w.Blocks.Returns(ci => (IList<Block>)_blocksById.Values.ToList());
    w.Items.Returns(ci => (IList<Item>)_itemsByCode.Values.ToList());
    return w;
  }

  private ICoreServerAPI BuildApi() {
    var api = Substitute.For<ICoreServerAPI>();
    // BlockEntity.Api is typed ICoreAPI; ICoreServerAPI re-declares these members with `new`.
    var coreApi = (ICoreAPI)api;

    api.Side.Returns(EnumAppSide.Server);
    api.World.Returns(World);
    coreApi.World.Returns(World);
    api.Logger.Returns(Log);
    coreApi.Logger.Returns(Log);

    // Mods.IsModEnabled reports every id enabled; see TestModLoader's own doc.
    api.ModLoader.Returns(Mods);
    coreApi.ModLoader.Returns(Mods);

    // LoadModConfig<T>/StoreModConfig<T> are open generics NSubstitute cannot bind by type; a
    // custom call handler dispatches on the raw method info instead.
    SubstitutionContext
      .Current.GetCallRouterFor(api)
      .RegisterCustomCallHandlerFactory(_ => new ModConfigCallHandler(
        ConfigFiles
      ));

    // Empty rather than unconfigured, so a content check reads "nothing shipped" rather than null.
    var assets = Substitute.For<IAssetManager>();
    assets
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns([]);
    api.Assets.Returns(assets);
    coreApi.Assets.Returns(assets);

    var events = Substitute.For<IServerEventAPI>();
    api.Event.Returns(events);
    coreApi.Event.Returns(events);

    // RegisterChannel hands out this world's memoised pair; see Channels.
    var network = Substitute.For<IServerNetworkAPI>();
    network
      .RegisterChannel(Arg.Any<string>())
      .Returns(ci => Channels(ci.Arg<string>()).Server);
    api.Network.Returns(network);
    coreApi.Network.Returns(network);

    // Captures server tick listeners for FireBlockEntityTicks; only the RegisterGameTickListener
    // overload this game version calls is mocked.
#if GAME_GE_1_22
    events
      .RegisterGameTickListener(
        Arg.Any<System.Action<float>>(),
        Arg.Any<BlockPos>(),
        Arg.Any<System.Action<System.Exception>>(),
        Arg.Any<int>(),
        Arg.Any<int>()
      )
      .Returns(ci =>
        AddTickListener(ci.Arg<System.Action<float>>(), ci.ArgAt<int>(3))
      );
#else
    events
      .RegisterGameTickListener(
        Arg.Any<System.Action<float>>(),
        Arg.Any<System.Action<System.Exception>>(),
        Arg.Any<int>(),
        Arg.Any<int>()
      )
      .Returns(ci =>
        AddTickListener(ci.Arg<System.Action<float>>(), ci.ArgAt<int>(2))
      );
#endif

    // Honours UnregisterGameTickListener so a torn-down block entity stops ticking.
    events
      .When(x => x.UnregisterGameTickListener(Arg.Any<long>()))
      .Do(ci => {
        long id = ci.Arg<long>();
        _tickListeners.Remove(id);
        _tickAccumMs.Remove(id);
      });

    return api;
  }

  private ICoreClientAPI BuildClientApi() {
    var api = Substitute.For<ICoreClientAPI>();
    var coreApi = (ICoreAPI)api;

    api.Side.Returns(EnumAppSide.Client);
    api.Logger.Returns(Log);
    coreApi.Logger.Returns(Log);

    // RegisterChannel hands out this world's memoised pair, same as Api's server side.
    var network = Substitute.For<IClientNetworkAPI>();
    network
      .RegisterChannel(Arg.Any<string>())
      .Returns(ci => Channels(ci.Arg<string>()).Client);
    api.Network.Returns(network);
    coreApi.Network.Returns(network);

    return api;
  }

  private Block? GetByCode(AssetLocation? code) =>
    code != null && _blocksByCode.TryGetValue(code.ToString(), out var b)
      ? b
      : null;

  private long AddTickListener(System.Action<float> callback, int intervalMs) {
    long id = ++_nextListenerId;
    _tickListeners[id] = new TickListener(callback, intervalMs);
    _tickAccumMs[id] = 0;
    return id;
  }

  private void DoSetBlock(int id, BlockPos pos) {
    if (id == 0) {
      _blocks.Remove(pos);
      _blockEntities.Remove(pos);
      return;
    }
    if (!_blocksById.TryGetValue(id, out var b))
      return;
    _blocks[pos] = b;

    // Engine parity: placing a block with an entity class (re)creates its block entity when a
    // factory is registered for it.
    if (
      b.EntityClass is { } entityClass
      && (
        !_blockEntities.TryGetValue(pos, out var existing)
        || existing.Block != b
      )
    ) {
      // Tears the stale block entity down first so it cannot keep ticking.
      existing?.OnBlockUnloaded();
      DoSpawnBlockEntity(entityClass, pos);
    }
  }

  private void DoSpawnBlockEntity(string classname, BlockPos pos) {
    if (!_beFactories.TryGetValue(classname, out var factory))
      return;
    var be = factory();
    be.Pos = pos.Copy();
    be.Block = GetBlock(pos);
    _blockEntities[pos] = be;
    be.Initialize(Api);
  }

  private void DoExchangeBlock(int id, BlockPos pos) {
    if (!_blocksById.TryGetValue(id, out var b))
      return;
    _blocks[pos] = b;
    if (_blockEntities.TryGetValue(pos, out var be))
      be.Block = b;
  }

  private void DoBreak(BlockPos pos) {
    // Routes through the real break lifecycle: drops contents, runs OnBlockRemoved.
    if (_blockEntities.TryGetValue(pos, out var be)) {
      be.OnBlockBroken();
      be.OnBlockRemoved();
    }
    _blocks.Remove(pos);
    _blockEntities.Remove(pos);
  }

  #endregion
}
