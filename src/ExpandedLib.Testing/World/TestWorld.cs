using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NSubstitute.ClearExtensions;
using NSubstitute.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>A headless, in-process stand-in for a Vintage Story server world, backing the
/// block-network test suite.</summary>
public sealed partial class TestWorld : IDisposable {
  private readonly Dictionary<BlockPos, Block> _blocks = new();
  private readonly Dictionary<BlockPos, Block> _fluids = new();
  private readonly Dictionary<BlockPos, BlockEntity> _blockEntities = new();
  private readonly Dictionary<int, Block> _blocksById = new();
  private readonly Dictionary<string, Block> _blocksByCode = new();
  private readonly Dictionary<string, Item> _itemsByCode = new();
  private readonly Dictionary<int, Item> _itemsById = new();
  private readonly Dictionary<string, Func<BlockEntity>> _beFactories = new();
  private int _nextItemId = 1;

  private double _totalDays;

  private readonly List<object> _substitutes = [];
  private readonly List<ReleasableCallHandler> _handlers = [];

  /// <summary>The block returned for any cell that has not been placed (id 0, code "game:air",
  /// <c>Replaceable</c> 9999 as the game's air, so any block may be placed over it).</summary>
  public Block Air { get; }

  /// <summary>The network graph manager under test. Factories are registered via <see cref="RegisterNetwork"/>.</summary>
  public BlockNetworkModSystem Networks { get; } = new();

  /// <summary>The fake block accessor handed to every production network call.</summary>
  /// <remarks><c>GetBlockEntity&lt;T&gt;</c> answers as <c>GetBlockEntity</c> does, cast to
  /// <c>T</c>: null when the cell holds no entity, its chunk is away, or the entity is not a
  /// <c>T</c>. <c>ExchangeBlock</c> puts the new block in the cell, keeps the cell's block entity and
  /// then runs its <see cref="BlockEntity.OnExchanged"/> with the new block, as the engine's
  /// does. Each cell holds a solid layer and a fluid layer: a read with a
  /// <see cref="BlockLayersAccess"/> value answers as <see cref="GetBlock(BlockPos, int)"/> does,
  /// <c>SetBlock</c> without a layer puts a block whose <see cref="Block.ForFluidsLayer"/> is true
  /// into the fluid layer and empties the solid one, <c>SetBlock</c> with a stack also runs a solid
  /// block's <see cref="Block.OnBlockPlaced"/> with it, and <c>SetBlock</c> with
  /// <see cref="BlockLayersAccess.Solid"/> or <see cref="BlockLayersAccess.Fluid"/> writes that layer
  /// alone.</remarks>
  public IBlockAccessor Accessor { get; }

  /// <summary>The fake server world (calendar, item-drop spawning) exposed as
  /// <see cref="BlockNetworkModSystem.ServerWorld"/>; its <c>Side</c> is
  /// <see cref="EnumAppSide.Server"/>, and its <c>Claims.TryAccess</c> answers true for every
  /// player, cell and flag until a test restubs it.</summary>
  public IServerWorldAccessor World { get; }

  /// <summary>The calendar; <see cref="AdvanceDays"/> moves <c>TotalDays</c> for evaporation tests.</summary>
  public IGameCalendar Calendar { get; }

  /// <summary>A server-side core API wired to this world; assign it to a block entity's <c>Api</c>,
  /// or use <see cref="Attach"/>.</summary>
  /// <remarks>Its <c>ClassRegistry.CreateInvNetworkUtil</c> builds the engine's own
  /// <c>InventoryNetworkUtil</c>, so an inventory's <c>InvNetworkUtil.HandleClientPacket</c> moves
  /// stacks as the server does; <see cref="ClientApi"/>'s does the same.</remarks>
  public ICoreServerAPI Api { get; }

  /// <summary>A client-side core API wired to this world, for exercising a <c>ModSystem</c>'s
  /// <c>StartClientSide</c> and code that branches on the client.</summary>
  /// <remarks>Its <c>World</c> is a client world whose <c>Side</c> is
  /// <see cref="EnumAppSide.Client"/>, reading the same block store as <see cref="Accessor"/>, with
  /// block entities answered by the client twins stood through <see cref="ClientTwin.Stand"/>, and the
  /// same calendar, config, logger, block and item lookups and <c>Collectibles</c> as <see cref="World"/>.
  /// Code handed <see cref="Api"/> runs its server branch; code handed this runs its client branch.</remarks>
  /// <exception cref="InvalidOperationException">The first shape read through the client's asset manager
  /// finds no game install: the game's env var is unset and no <c>.game/&lt;slug&gt;</c> is provisioned.</exception>
  public ICoreClientAPI ClientApi { get; }

  /// <summary>Channel pairs handed out by <see cref="Channels"/>, keyed by channel name.</summary>
  private readonly Dictionary<string, TestChannels> _channels = new();

  /// <summary>The latest <see cref="TestPlayer"/> made per uid; <c>PlayerByUid</c> answers from
  /// it.</summary>
  private readonly Dictionary<string, IPlayer> _playersByUid = new();

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

  private readonly Dictionary<long, DelayedCallback> _callbacks = new();

  // Sim time (ms) since the world was built; a delayed callback is due once this reaches its DueMs.
  private long _clockMs;

  /// <summary>A captured block-entity tick listener: its callback and requested interval (ms).</summary>
  private readonly record struct TickListener(
    System.Action<float> Callback,
    int IntervalMs
  );

  /// <summary>A captured one-shot delayed callback and the sim time (ms) it is due at.</summary>
  private readonly record struct DelayedCallback(
    System.Action<float> Callback,
    long DueMs
  );

  /// <summary>Item stacks spawned by the simulation.</summary>
  public List<ItemStack> Drops { get; } = new();

  /// <summary>Every item entity the simulation spawned, in order, with the position it spawned at.
  /// Nothing clears it; <see cref="Drops"/> holds the same stacks without positions.</summary>
  public List<(ItemStack Stack, Vec3d At)> Spawns { get; } = new();

  /// <summary>Whether <see cref="Accessor"/>'s <c>BreakBlock</c> runs the broken block's own
  /// <see cref="Block.OnBlockBroken"/> with the breaking player, as the engine does; true by
  /// default.</summary>
  /// <remarks>When false, <c>BreakBlock</c> runs only the block entity's
  /// <see cref="BlockEntity.OnBlockBroken"/> and <see cref="BlockEntity.OnBlockRemoved"/> and
  /// clears the cell.</remarks>
  public bool BreakRunsBlockHooks { get; set; } = true;

  /// <summary>Whether <see cref="Accessor"/> runs the engine's removal hooks; true by
  /// default.</summary>
  /// <remarks>When true, <c>SetBlock</c> over a placed block of another id first runs that block's
  /// <see cref="Block.OnBlockRemoved"/>, and <c>RemoveBlockEntity</c> runs the entity's
  /// <see cref="BlockEntity.OnBlockRemoved"/> and drops it. When false, a replaced cell's hooks never
  /// run and <c>RemoveBlockEntity</c> does nothing.</remarks>
  public bool RunsRemovalHooks { get; set; } = true;

  /// <summary>Whether a block entity spawned through <see cref="Accessor"/> is initialised before
  /// the cell answers it, as the engine's spawn does; false by default.</summary>
  /// <remarks>When true, <c>GetBlockEntity</c> at the cell answers null while the entity's
  /// <see cref="BlockEntity.Initialize"/> runs and the entity afterwards. When false, the cell
  /// answers the entity throughout.</remarks>
  public bool SpawnInitializesBeforeRegistering { get; set; }

  public TestWorld() {
    Air = TestBlocks.Configure(new Block(), "game:air", 0);
    Air.Replaceable = 9999;
    Air.SideSolid = new SmallBoolArray(0);
    _blocksById[0] = Air;

    _loadedChunk = Made<IWorldChunk>();
    Calendar = Made<IGameCalendar>();
    PushCalendar();
    Calendar
      .When(c => c.Add(Arg.Any<float>()))
      .Do(ci => AdvanceHours(ci.Arg<float>()));

    Mods.Add("exlib", "1.0.0").Register(Networks);
    Networks.FastForward.BudgetMs = 0;

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

  /// <summary>Makes <c>PlayerByUid(uid)</c> answer <paramref name="player"/>, replacing an earlier
  /// player of that uid.</summary>
  internal void AddPlayer(string uid, IPlayer player) =>
    _playersByUid[uid] = player;

  /// <summary>Creates an NSubstitute substitute of <typeparamref name="T"/> that
  /// <see cref="Dispose"/> clears.</summary>
  internal T Made<T>()
    where T : class {
    T made = Substitute.For<T>();
    _substitutes.Add(made);
    return made;
  }

  /// <summary>Registers <paramref name="handler"/> as a custom call handler of
  /// <paramref name="substitute"/>, released by <see cref="Dispose"/>.</summary>
  internal void Handled(object substitute, ICallHandler handler) {
    var releasable = new ReleasableCallHandler(handler);
    SubstitutionContext
      .Current.GetCallRouterFor(substitute)
      .RegisterCustomCallHandlerFactory(_ => releasable);
    _handlers.Add(releasable);
  }

  /// <summary>Clears the recorded calls, configured returns and callbacks of every substitute this
  /// world created, releases its custom call handlers, so a proxy type's cached call router no
  /// longer reaches the world, and deletes <see cref="ConfigFiles"/>'s temp directory.</summary>
  /// <remarks>A world, and every object taken from it, is not supported after disposal.</remarks>
  public void Dispose() {
    foreach (ReleasableCallHandler handler in _handlers)
      handler.Release();
    _handlers.Clear();
    foreach (object made in _substitutes)
      made.ClearSubstitute(ClearOptions.All);
    _substitutes.Clear();
    ConfigFiles.Dispose();
  }

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

  /// <summary>Registers a typed-network factory, exactly as a mod would in <c>ModSystem.Start</c>;
  /// the calling assembly is the type's owner.</summary>
  [System.Runtime.CompilerServices.MethodImpl(
    System.Runtime.CompilerServices.MethodImplOptions.NoInlining
  )]
  public TestWorld RegisterNetwork(
    string networkType,
    System.Func<BlockNetworkModSystem, BlockNetwork> factory
  ) =>
    RegisterNetwork(
      networkType,
      factory,
      System.Reflection.Assembly.GetCallingAssembly().GetName().Name
        ?? "unknown assembly"
    );

  internal TestWorld RegisterNetwork(
    string networkType,
    System.Func<BlockNetworkModSystem, BlockNetwork> factory,
    string owner
  ) {
    Networks.RegisterNetworkType(networkType, () => factory(Networks), owner);
    return this;
  }

  /// <summary>Places <paramref name="block"/> (and optional <paramref name="be"/>) at
  /// <paramref name="pos"/>, registering the block in the id/code lookup.</summary>
  /// <remarks>A block whose <see cref="Block.ForFluidsLayer"/> is true (water, lava, lake ice) goes
  /// into the cell's fluid layer and empties its solid layer and block entity, as the engine's
  /// <c>SetBlock</c> does; any other goes into the solid layer and leaves the fluid. In a world
  /// holding a class registry (<see cref="RegisterClasses"/>), logs a Warning when
  /// <paramref name="block"/> names an <c>EntityClass</c> other than the one
  /// <paramref name="be"/>'s type is registered under.</remarks>
  public TestWorld Place(BlockPos pos, Block block, BlockEntity? be = null) {
    Register(block);
    if (block.ForFluidsLayer) {
      _fluids[pos] = block;
      _blocks.Remove(pos);
      _blockEntities.Remove(pos);
    } else
      _blocks[pos] = block;
    if (be != null) {
      be.Pos = pos.Copy();
      be.Block = block;
      _blockEntities[pos] = be;
      WarnOnEntityClassMismatch(block, be);
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

  private readonly Dictionary<
    string,
    System.Func<BlockEntity, BlockEntityBehavior>
  > _behaviorFactories = new();

  /// <summary>Registers a factory that <see cref="Api"/>'s and <see cref="World"/>'s
  /// <c>ClassRegistry.CreateBlockEntityBehavior</c> build the block-entity behaviour
  /// <paramref name="classname"/> from, for the entity it is handed.</summary>
  /// <remarks>Beside a real class registry (<see cref="RegisterClasses"/>,
  /// <see cref="RegisterClass"/>), whether made before or after this call, the factory wins over a
  /// class registered under the same name, and <c>GetBlockEntityBehaviorClass</c> answers
  /// <paramref name="classname"/> with <see cref="BlockEntityBehavior"/> when the registry holds no
  /// class of that name, so an entity the registry builds for a block declaring
  /// <paramref name="classname"/> gets the factory's behaviour. A later factory for the same name
  /// replaces the earlier.</remarks>
  /// <param name="classname">The behaviour's registered name, as a block's
  /// <c>entityBehaviors</c> or a filler cell names it.</param>
  /// <param name="factory">Builds the behaviour for the block entity passed.</param>
  /// <returns>This world.</returns>
  public TestWorld RegisterBlockEntityBehaviorFactory(
    string classname,
    System.Func<BlockEntity, BlockEntityBehavior> factory
  ) {
    _behaviorFactories[classname] = factory;
    if (_classes == null)
      Api.ClassRegistry.CreateBlockEntityBehavior(
          Arg.Any<BlockEntity>(),
          classname
        )
        .Returns(ci => factory(ci.Arg<BlockEntity>()));
    return this;
  }

  /// <summary>Builds a <typeparamref name="T"/>, runs its <see cref="ModSystem.Start"/> against
  /// <see cref="Api"/> and registers it in <see cref="Mods"/>, so the mod loader's
  /// <c>GetModSystem</c> answers it: the way to stand up a vanilla system a block or behaviour asks
  /// the loader for (<c>ModSystemBlockReinforcement</c> for <c>BlockBehaviorLockable</c>).</summary>
  /// <remarks>Its <c>StartServerSide</c>, <c>StartClientSide</c> and asset hooks are not run, and
  /// the classes its <c>Start</c> registers through <see cref="Api"/> are not recorded in a class
  /// registry. Each call builds and registers another instance; <c>GetModSystem</c> answers the
  /// first registered.</remarks>
  /// <typeparam name="T">The mod system type.</typeparam>
  /// <returns>The started system.</returns>
  /// <exception cref="Exception">Whatever the system's <c>Start</c> throws.</exception>
  public T StartModSystem<T>()
    where T : ModSystem, new() {
    var system = new T();
    system.Start(Api);
    Mods.Register(system);
    return system;
  }

  /// <summary>Registers a factory that <c>BlockAccessor.SpawnBlockEntity(classname, pos)</c> uses
  /// for <paramref name="classname"/>. As the engine's, that spawn initialises the entity and then
  /// runs its <see cref="BlockEntity.OnBlockPlaced"/> with the spawn's stack.</summary>
  public TestWorld RegisterBlockEntityFactory(
    string classname,
    Func<BlockEntity> factory
  ) {
    _beFactories[classname] = factory;
    return this;
  }

  /// <summary>Registers a block in the id/code lookup without placing it (for orientation-variant
  /// swaps). A block without <see cref="Block.Sounds"/> is given empty ones, as the engine's
  /// registration gives them.</summary>
  public TestWorld Register(Block block) {
    block.Sounds ??= new BlockSounds();
    ReflectionHelpers.SetField(block, "api", Api);
    _blocksById[block.BlockId] = block;
    if (block.Code != null)
      _blocksByCode[block.Code.ToString()] = block;
    return this;
  }

  /// <summary>Registers a real, already-resolved <see cref="Item"/> in the id/code lookup.</summary>
  public TestWorld Register(Item item) {
    ReflectionHelpers.SetField(item, "api", Api);
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

  /// <summary>What the store holds at <paramref name="pos"/>, whether or not its chunk is loaded:
  /// the solid layer's block, else the fluid layer's, else <see cref="Air"/>.</summary>
  public Block GetBlock(BlockPos pos) =>
    GetBlock(pos, BlockLayersAccess.Default);

  /// <summary>What the store holds at <paramref name="pos"/> as the engine reads
  /// <paramref name="layer"/>, whether or not its chunk is loaded.</summary>
  /// <param name="pos">The cell.</param>
  /// <param name="layer">A <see cref="BlockLayersAccess"/> value: <c>Default</c> reads the solid
  /// layer, else the fluid; <c>SolidBlocks</c> the solid layer; <c>Fluid</c> the fluid layer;
  /// <c>FluidOrSolid</c> the fluid layer, else the solid; <c>MostSolid</c> a fluid-layer block that
  /// is not a liquid (ice), else the solid layer, so liquid water reads as air there.</param>
  /// <returns>The block read, or <see cref="Air"/> when the layers read hold none.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> is none of the five
  /// values.</exception>
  public Block GetBlock(BlockPos pos, int layer) {
    Block? solid = _blocks.GetValueOrDefault(pos);
    Block? fluid = _fluids.GetValueOrDefault(pos);
    return layer switch {
      BlockLayersAccess.Default => solid ?? fluid ?? Air,
      BlockLayersAccess.SolidBlocks => solid ?? Air,
      BlockLayersAccess.Fluid => fluid ?? Air,
      BlockLayersAccess.FluidOrSolid => fluid ?? solid ?? Air,
      BlockLayersAccess.MostSolid => fluid is { } ice && !ice.IsLiquid()
        ? ice
        : solid ?? Air,
      _ => throw new ArgumentOutOfRangeException(
        nameof(layer),
        layer,
        "not a BlockLayersAccess value"
      ),
    };
  }

  /// <summary>What the store holds at <paramref name="pos"/>, whether or not its chunk is
  /// loaded.</summary>
  public BlockEntity? GetBlockEntity(BlockPos pos) =>
    _blockEntities.TryGetValue(pos, out var be) ? be : null;

  #endregion

  #region Chunk loading

  private readonly HashSet<Vec3i> _unloadedChunks = new();
  private readonly Dictionary<
    BlockPos,
    (BlockEntity Entity, TreeAttribute Tree)
  > _unloadedEntities = new();
  private readonly IWorldChunk _loadedChunk;

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
  /// <param name="pos">Any position inside the chunk.</param>
  /// <param name="unloadEntities">When true the chunk's block entities are also saved to their trees, sent
  /// <c>OnBlockUnloaded</c> and dropped, as the game does on a real unload; <see cref="LoadChunkAt"/> brings
  /// each back as a new instance read from its tree. False leaves the instances in place.</param>
  public TestWorld UnloadChunkAt(BlockPos pos, bool unloadEntities = false) {
    Vec3i chunk = ChunkOf(pos);
    if (unloadEntities)
      foreach (
        (BlockPos at, BlockEntity entity) in _blockEntities
          .Where(e => ChunkOf(e.Key) == chunk)
          .Select(e => (e.Key, e.Value))
          .ToList()
      ) {
        var tree = new TreeAttribute();
        entity.ToTreeAttributes(tree);
        entity.OnBlockUnloaded();
        _blockEntities.Remove(at);
        _unloadedEntities[at] = (entity, tree);
      }
    _unloadedChunks.Add(chunk);
    return this;
  }

  /// <summary>Brings back the chunk holding <paramref name="pos"/>, with a new block entity read from the
  /// saved tree of each one <see cref="UnloadChunkAt"/> unloaded with its entities.</summary>
  public TestWorld LoadChunkAt(BlockPos pos) {
    Vec3i chunk = ChunkOf(pos);
    _unloadedChunks.Remove(chunk);
    foreach (
      BlockPos at in _unloadedEntities
        .Keys.Where(k => ChunkOf(k) == chunk)
        .ToList()
    ) {
      (BlockEntity old, TreeAttribute tree) = _unloadedEntities[at];
      _unloadedEntities.Remove(at);
      Revive(old, at, tree);
    }
    return this;
  }

  /// <summary>The positions of the members in the fast-forward group holding <paramref name="pos"/>, as of the last
  /// replay pass.</summary>
  /// <param name="pos">A member's position.</param>
  /// <returns>Every member position of the group, <paramref name="pos"/> included; empty when it is in none.</returns>
  public IReadOnlyList<BlockPos> FastForwardGroupOf(BlockPos pos) =>
    [
      .. Networks.FastForward.GroupOf(pos)?.Members.Select(m => m.Pos.Copy())
        ?? [],
    ];

  /// <summary>How many base replay steps the fast-forward group holding <paramref name="pos"/> has run in the
  /// replay now under way; a replay that closes its gap resets it.</summary>
  /// <param name="pos">A member's position.</param>
  /// <returns>The step count, 0 when the position is in no group.</returns>
  public int ReplayedSteps(BlockPos pos) =>
    Networks.FastForward.GroupOf(pos)?.ReplayedSteps ?? 0;

  /// <summary>What <see cref="Accessor"/> sees at <paramref name="pos"/> in
  /// <paramref name="layer"/>: the placed block, or <see cref="Air"/> when its chunk is
  /// away.</summary>
  private Block ReadBlock(
    BlockPos pos,
    int layer = BlockLayersAccess.Default
  ) => IsChunkLoaded(pos) ? GetBlock(pos, layer) : Air;

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

  /// <summary>Advances the simulation by <paramref name="seconds"/> seconds through
  /// <see cref="BlockNetworkModSystem.ListenerTick"/>, in the server listener's 200 ms fires, five per second.
  /// Each fire replays the game time the calendar moved past the machines' ticks, with no time budget, so a
  /// single fire finishes a gap of up to 12 game hours.</summary>
  public void Tick(int seconds = 1) {
    const float fire = BlockNetworkModSystem.ListenerIntervalMs / 1000f;
    for (int i = 0; i < seconds * 5; i++)
      Networks.ListenerTick(Accessor, fire);
  }

  /// <summary>Fires every server tick listener registered through <see cref="Api"/> or
  /// <see cref="World"/>, <paramref name="times"/> times, each with <paramref name="dt"/>; then
  /// advances sim time by <paramref name="dt"/> seconds per time and fires the delayed callbacks
  /// that come due. Only <c>RegisterCallback(Action&lt;float&gt;, int)</c> is captured.</summary>
  public void FireBlockEntityTicks(float dt = 1f, int times = 1) {
    for (int i = 0; i < times; i++)
      FireBlockEntityTicks(dt, new HashSet<long>());
  }

  /// <summary>Fires every listener not in <paramref name="except"/> once with
  /// <paramref name="dt"/>, then advances sim time by <paramref name="dt"/> seconds and fires the
  /// delayed callbacks that come due.</summary>
  internal void FireBlockEntityTicks(float dt, IReadOnlySet<long> except) {
    foreach (var (id, listener) in _tickListeners.ToList())
      if (!except.Contains(id))
        listener.Callback(dt);
    AdvanceCallbacks((long)System.Math.Round(dt * 1000f));
  }

  /// <summary>Fires every listener not in <paramref name="except"/> once with
  /// <paramref name="dt"/> in firing order, runs <paramref name="network"/> just before the
  /// listener <paramref name="networkBefore"/> even when that listener is in except, advances
  /// sim time and fires the delayed callbacks that come due, and runs <paramref name="network"/>
  /// last when it has not run (that listener is null or fires nowhere).</summary>
  internal void FireBlockEntityTicks(
    float dt,
    IReadOnlySet<long> except,
    System.Action network,
    long? networkBefore
  ) {
    bool ran = false;
    foreach (var (id, listener) in _tickListeners.ToList()) {
      if (!ran && id == networkBefore) {
        network();
        ran = true;
      }
      if (except.Contains(id))
        continue;
      listener.Callback(dt);
    }
    AdvanceCallbacks((long)System.Math.Round(dt * 1000f));
    if (!ran)
      network();
  }

  /// <summary>The tick listeners in firing order, each with its id and callback.</summary>
  internal List<(long Id, System.Action<float> Callback)> TickCallbacks() =>
    [.. _tickListeners.Select(l => (l.Key, l.Value.Callback))];

  /// <summary>Moves the listeners of each set in <paramref name="groups"/> to the end of the firing
  /// order, the first group's first, in the order the sets are given; the rest keep their order
  /// ahead of them.</summary>
  internal void MoveListenersLast(IReadOnlyList<IReadOnlySet<long>> groups) {
    var entries = _tickListeners.ToList();
    List<long> moved =
    [
      .. groups
        .SelectMany(g => entries.Where(e => g.Contains(e.Key)))
        .Select(e => e.Key)
        .Distinct(),
    ];
    _tickListeners.Clear();
    foreach (var entry in entries.Where(e => !moved.Contains(e.Key)))
      _tickListeners.Add(entry.Key, entry.Value);
    foreach (long id in moved)
      _tickListeners.Add(id, entries.First(e => e.Key == id).Value);
  }

  /// <summary>Advances block-entity sim time by <paramref name="totalMs"/> ms, firing each listener
  /// <see cref="FireBlockEntityTicks"/> fires once per whole interval it registered, one listener's
  /// fires before the next listener's, then the delayed callbacks that come due.</summary>
  public void AdvanceBlockEntityTime(int totalMs) =>
    AdvanceBlockEntityTime(totalMs, null);

  /// <summary>The last tick listener or delayed callback id issued, 0 before any. Every id issued
  /// after this read is greater than it.</summary>
  internal long LastListenerId => _nextListenerId;

  /// <summary>The ids of the tick listeners registered and not yet removed, as a copy.</summary>
  internal HashSet<long> TickListenerIds() => [.. _tickListeners.Keys];

  /// <summary>Advances block-entity sim time by <paramref name="totalMs"/> ms for the listeners in
  /// <paramref name="only"/>, or for every listener when it is null. Delayed callbacks that come due
  /// fire scene-wide, whichever listeners <paramref name="only"/> names.</summary>
  internal void AdvanceBlockEntityTime(int totalMs, IReadOnlySet<long>? only) {
    StepListeners(totalMs, only);
    AdvanceCallbacks(totalMs);
  }

  /// <summary>Fires the listeners in <paramref name="only"/>, or every listener when it is null,
  /// once per whole interval of <paramref name="totalMs"/> ms; no delayed callback comes due.</summary>
  internal void StepListeners(int totalMs, IReadOnlySet<long>? only) {
    // Snapshot: a listener may unregister (or a block entity may register a new one) while firing.
    foreach (long id in _tickListeners.Keys.ToList()) {
      if (only != null && !only.Contains(id))
        continue;
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

  /// <summary>Game hours one real second makes on the live server at the default calendar.</summary>
  public const double GameHoursPerSecond = 1.0 / GameTime.MachineSecondsPerHour;

  /// <summary>Moves the calendar forward by game hours without ticking; the next <see cref="Tick"/> replays the gap on the production machines and networks of its groups.</summary>
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

    var tree = new TreeAttribute();
    old.ToTreeAttributes(tree);

    // A network node keeps its graph node: base OnBlockUnloaded does not RemoveNode.
    old.OnBlockUnloaded();
    _blockEntities.Remove(pos);

    return Revive(old, pos, tree);
  }

  private BlockEntity Revive(BlockEntity old, BlockPos pos, TreeAttribute tree) {
    Block block = GetBlock(pos);
    BlockEntity fresh = NewBlockEntityLike(old, block);
    fresh.Pos = pos.Copy();
    fresh.Block = block;
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

  /// <summary>Creates a fresh block entity of the class the engine would instantiate on load: the
  /// placed block's entity class through a registered factory, else through the class registry with
  /// its behaviours, else one of <paramref name="old"/>'s type.</summary>
  internal BlockEntity NewBlockEntityLike(BlockEntity old, Block block) {
    string? classname = block?.EntityClass ?? old.Block?.EntityClass;
    if (classname == null)
      return (BlockEntity)Activator.CreateInstance(old.GetType())!;
    if (_beFactories.TryGetValue(classname, out var factory))
      return factory();
    return CreateRegisteredBlockEntity(classname, block ?? old.Block!)
      ?? (BlockEntity)Activator.CreateInstance(old.GetType())!;
  }

  #endregion

  #region Fake wiring

  // The block reads the server and the client accessor answer from the same store.
  private void StubBlockReads(IBlockAccessor a) {
    a.GetBlock(Arg.Any<BlockPos>())
      .Returns(ci => ReadBlock(ci.Arg<BlockPos>()));
    a.GetBlock(Arg.Any<BlockPos>(), Arg.Any<int>())
      .Returns(ci => ReadBlock(ci.Arg<BlockPos>(), ci.ArgAt<int>(1)));
    a.GetBlock(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
      .Returns(ci =>
        ReadBlock(
          new BlockPos(ci.ArgAt<int>(0), ci.ArgAt<int>(1), ci.ArgAt<int>(2)),
          ci.ArgAt<int>(3)
        )
      );
    a.GetMostSolidBlock(Arg.Any<BlockPos>())
      .Returns(ci =>
        ReadBlock(ci.Arg<BlockPos>(), BlockLayersAccess.MostSolid)
      );
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
          new BlockPos(ci.ArgAt<int>(0), ci.ArgAt<int>(1), ci.ArgAt<int>(2)),
          ci.ArgAt<int>(3)
        )
      );
  }

  private IBlockAccessor BuildAccessor() {
    var a = Made<IBlockAccessor>();

    StubBlockReads(a);
    a.GetBlockEntity(Arg.Any<BlockPos>())
      .Returns(ci => ReadBlockEntity(ci.Arg<BlockPos>()));
    // GetBlockEntity<T> is an open generic NSubstitute cannot bind by type.
    Handled(a, new TypedBlockEntityCallHandler(ReadBlockEntity));
    // Resolve-by-code; orientation behaviours read a null here as an undeclared variant.
    a.GetBlock(Arg.Any<AssetLocation>())
      .Returns(ci => GetByCode(ci.Arg<AssetLocation>()));

    a.When(x => x.SetBlock(Arg.Any<int>(), Arg.Any<BlockPos>()))
      .Do(ci => DoSetBlock(ci.ArgAt<int>(0), ci.ArgAt<BlockPos>(1)));
    // The placement overload; left unwired, a real TryPlaceBlock would place nothing.
    a.When(x =>
        x.SetBlock(Arg.Any<int>(), Arg.Any<BlockPos>(), Arg.Any<ItemStack>())
      )
      .Do(ci =>
        DoSetBlock(
          ci.ArgAt<int>(0),
          ci.ArgAt<BlockPos>(1),
          ci.ArgAt<ItemStack>(2)
        )
      );
    a.When(x => x.SetBlock(Arg.Any<int>(), Arg.Any<BlockPos>(), Arg.Any<int>()))
      .Do(ci =>
        DoSetBlock(ci.ArgAt<int>(0), ci.ArgAt<BlockPos>(1), ci.ArgAt<int>(2))
      );
    a.When(x => x.ExchangeBlock(Arg.Any<int>(), Arg.Any<BlockPos>()))
      .Do(ci => DoExchangeBlock(ci.ArgAt<int>(0), ci.ArgAt<BlockPos>(1)));
    a.When(x => x.MarkBlockDirty(Arg.Any<BlockPos>()))
      .Do(ci => MarkRedraw(ci.Arg<BlockPos>()));
    a.When(x => x.MarkBlockEntityDirty(Arg.Any<BlockPos>()))
      .Do(ci => _serverDirty.Add(ci.Arg<BlockPos>()));
    a.When(x =>
        x.SpawnBlockEntity(
          Arg.Any<string>(),
          Arg.Any<BlockPos>(),
          Arg.Any<ItemStack>()
        )
      )
      .Do(ci =>
        DoSpawnBlockEntity(ci.ArgAt<string>(0), ci.ArgAt<BlockPos>(1))
          ?.OnBlockPlaced(ci.ArgAt<ItemStack>(2))
      );
    a.When(x =>
        x.BreakBlock(Arg.Any<BlockPos>(), Arg.Any<IPlayer>(), Arg.Any<float>())
      )
      .Do(ci =>
        DoBreak(ci.ArgAt<BlockPos>(0), ci.ArgAt<IPlayer>(1), ci.ArgAt<float>(2))
      );
    a.When(x => x.RemoveBlockEntity(Arg.Any<BlockPos>()))
      .Do(ci => DoRemoveBlockEntity(ci.Arg<BlockPos>()));

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

  private ICachingBlockAccessor BuildCachingAccessor() {
    var a = Made<ICachingBlockAccessor>();
    a.GetBlock(Arg.Any<BlockPos>())
      .Returns(ci => ReadBlock(ci.Arg<BlockPos>()));
    a.IsValidPos(Arg.Any<BlockPos>()).Returns(true);
    a.LastChunkLoaded.Returns(true);
    return a;
  }

  private IServerWorldAccessor BuildWorld() {
    var w = Made<IServerWorldAccessor>();
    w.Side.Returns(EnumAppSide.Server);
    w.PlayerByUid(Arg.Any<string>())
      .Returns(ci => _playersByUid.GetValueOrDefault(ci.Arg<string>()));
    w.BlockAccessor.Returns(Accessor);
    w.Calendar.Returns(Calendar);
    w.Logger.Returns(Log);
    w.Config.Returns(Config.Tree);
    // Particle/sound helpers read world.Rand.
    w.Rand.Returns(new Random(1));
    // A mod system's own tick (MechanicalPowerMod's 20 ms network step) runs beside the block
    // entities'.
    w.RegisterGameTickListener(
        Arg.Any<System.Action<float>>(),
        Arg.Any<int>(),
        Arg.Any<int>()
      )
      .Returns(ci =>
        AddTickListener(ci.Arg<System.Action<float>>(), ci.ArgAt<int>(1))
      );
    w.When(x => x.UnregisterGameTickListener(Arg.Any<long>()))
      .Do(ci => RemoveTickListener(ci.Arg<long>()));
    w.RegisterCallback(Arg.Any<System.Action<float>>(), Arg.Any<int>())
      .Returns(ci =>
        AddCallback(ci.Arg<System.Action<float>>(), ci.ArgAt<int>(1))
      );
    w.When(x => x.UnregisterCallback(Arg.Any<long>()))
      .Do(ci => RemoveCallback(ci.Arg<long>()));
    // No land claims: every player may build and use everywhere until a test restubs TryAccess.
    w.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        Arg.Any<BlockPos>(),
        Arg.Any<EnumBlockAccessFlags>()
      )
      .Returns(true);
    // Vanilla's RoomRegistry walks the cells through a caching accessor; every cell is loaded and
    // valid.
    w.GetCachingBlockAccessor(Arg.Any<bool>(), Arg.Any<bool>())
      .Returns(_ => BuildCachingAccessor());
    w.GetBlock(Arg.Any<AssetLocation>())
      .Returns(ci => GetByCode(ci.Arg<AssetLocation>()));
    w.GetBlock(Arg.Any<int>())
      .Returns(ci =>
        _blocksById.TryGetValue(ci.Arg<int>(), out var b) ? b : Air
      );
    w.GetItem(Arg.Any<AssetLocation>())
      .Returns(ci => GetItem(ci.Arg<AssetLocation>()));
    w.GetItem(Arg.Any<int>()).Returns(ci => GetItem(ci.Arg<int>()));
    // Wildcard searches over the registries, as the game answers them.
    w.SearchItems(Arg.Any<AssetLocation>())
      .Returns(ci =>
        _itemsByCode
          .Values.Where(i =>
            WildcardUtil.Match(ci.Arg<AssetLocation>(), i.Code)
          )
          .ToArray()
      );
    w.SearchBlocks(Arg.Any<AssetLocation>())
      .Returns(ci =>
        _blocksById
          .Values.Where(b =>
            b.Code != null
            && WildcardUtil.Match(ci.Arg<AssetLocation>(), b.Code)
          )
          .ToArray()
      );
    w.When(x =>
        x.SpawnItemEntity(
          Arg.Any<ItemStack>(),
          Arg.Any<Vec3d>(),
          Arg.Any<Vec3d>()
        )
      )
      .Do(ci => {
        Drops.Add(ci.Arg<ItemStack>());
        Spawns.Add((ci.Arg<ItemStack>(), ci.ArgAt<Vec3d>(1)));
      });
    // The overload Block.SpawnDropsAndRemoveBlock spawns a broken block's drops through.
    w.When(x =>
        x.SpawnItemEntity(
          Arg.Any<ItemStack>(),
          Arg.Any<BlockPos>(),
          Arg.Any<Vec3d>()
        )
      )
      .Do(ci => {
        Drops.Add(ci.Arg<ItemStack>());
        Spawns.Add((ci.Arg<ItemStack>(), ci.Arg<BlockPos>().ToVec3d()));
      });
#if GAME_GE_1_22
    w.GridRecipes.Returns(_gridRecipes);
    w.FastSearchRecipesByIngredient.Returns(_fastSearchRecipes);
#endif
    // The full registries a content check enumerates; computed on each read.
    w.Blocks.Returns(ci => (IList<Block>)_blocksById.Values.ToList());
    w.Items.Returns(ci => (IList<Item>)_itemsByCode.Values.ToList());
    w.Collectibles.Returns(ci =>
      _itemsByCode
        .Values.Cast<CollectibleObject>()
        .Concat(_blocksById.Values)
        .ToList()
    );
    return w;
  }

  private ICoreServerAPI BuildApi() {
    var api = Made<ICoreServerAPI>();
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
    Handled(api, new ModConfigCallHandler(ConfigFiles));

    // Empty rather than unconfigured, so a content check reads "nothing shipped" rather than null.
    var assets = Made<IAssetManager>();
    assets
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns([]);
    api.Assets.Returns(assets);
    coreApi.Assets.Returns(assets);

    // Vanilla collectibles cache their meshes and stack lists here from OnLoaded on.
    var cache = new Dictionary<string, object>();
    api.ObjectCache.Returns(cache);
    coreApi.ObjectCache.Returns(cache);

    var events = Made<IServerEventAPI>();
    api.Event.Returns(events);
    coreApi.Event.Returns(events);
#if GAME_GE_1_22
    WireRecipeMatching(events);
#endif

    // RegisterChannel hands out this world's memoised pair; see Channels.
    var network = Made<IServerNetworkAPI>();
    network
      .RegisterChannel(Arg.Any<string>())
      .Returns(ci => Channels(ci.Arg<string>()).Server);
    api.Network.Returns(network);
    coreApi.Network.Returns(network);

    // An inventory's InvNetworkUtil is the engine's own, so a block entity's slot packets run the
    // server's slot protocol.
    WireInventoryNetworkUtil(api);

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
      .Do(ci => RemoveTickListener(ci.Arg<long>()));

    // Delayed callbacks registered by a block entity or the API; honours UnregisterCallback so a
    // removed entity's callbacks never fire.
    events
      .RegisterCallback(Arg.Any<System.Action<float>>(), Arg.Any<int>())
      .Returns(ci =>
        AddCallback(ci.Arg<System.Action<float>>(), ci.ArgAt<int>(1))
      );
    events
      .When(x => x.UnregisterCallback(Arg.Any<long>()))
      .Do(ci => RemoveCallback(ci.Arg<long>()));

    return api;
  }

  private ICoreClientAPI BuildClientApi() {
    var api = Made<ICoreClientAPI>();
    var coreApi = (ICoreAPI)api;

    api.Side.Returns(EnumAppSide.Client);
    IClientWorldAccessor world = BuildClientWorld(api);
    api.World.Returns(world);
    coreApi.World.Returns(world);
    api.Logger.Returns(Log);
    coreApi.Logger.Returns(Log);

    // RegisterChannel hands out this world's memoised pair, same as Api's server side.
    var network = Made<IClientNetworkAPI>();
    network
      .RegisterChannel(Arg.Any<string>())
      .Returns(ci => Channels(ci.Arg<string>()).Client);
    api.Network.Returns(network);
    coreApi.Network.Returns(network);

    WireInventoryNetworkUtil(api);
    WireClientFake(api);

    return api;
  }

  private static void WireInventoryNetworkUtil(ICoreAPI api) =>
    api
      .ClassRegistry.CreateInvNetworkUtil(
        Arg.Any<InventoryBase>(),
        Arg.Any<ICoreAPI>()
      )
      .Returns(ci => new Vintagestory.Common.InventoryNetworkUtil(
        ci.Arg<InventoryBase>(),
        ci.Arg<ICoreAPI>()
      ));

  private IClientWorldAccessor BuildClientWorld(ICoreClientAPI api) {
    var w = Made<IClientWorldAccessor>();
    w.Side.Returns(EnumAppSide.Client);
    w.Api.Returns(api);
    IBlockAccessor clientAccessor = BuildClientAccessor();
    w.BlockAccessor.Returns(clientAccessor);
    // IClientWorldAccessor re-declares Calendar as IClientGameCalendar; code reading it through
    // IWorldAccessor gets the server world's calendar.
    ((IWorldAccessor)w).Calendar.Returns(Calendar);
    w.Logger.Returns(Log);
    w.Config.Returns(Config.Tree);
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
    w.Collectibles.Returns(ci =>
      _itemsByCode
        .Values.Cast<CollectibleObject>()
        .Concat(_blocksById.Values)
        .ToList()
    );
#if GAME_GE_1_22
    w.GridRecipes.Returns(_gridRecipes);
    w.FastSearchRecipesByIngredient.Returns(_fastSearchRecipes);
#endif
    return w;
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

  private void RemoveTickListener(long id) {
    _tickListeners.Remove(id);
    _tickAccumMs.Remove(id);
  }

  private long AddCallback(System.Action<float> callback, int delayMs) {
    long id = ++_nextListenerId;
    _callbacks[id] = new DelayedCallback(callback, _clockMs + delayMs);
    return id;
  }

  private void RemoveCallback(long id) => _callbacks.Remove(id);

  private void AdvanceCallbacks(long ms) {
    _clockMs += ms;
    // Snapshot: a callback may unregister others, and one it registers is due no earlier than the next step.
    foreach (long id in _callbacks.Keys.ToList()) {
      if (
        !_callbacks.TryGetValue(id, out DelayedCallback due)
        || due.DueMs > _clockMs
      )
        continue; // already removed by an earlier callback this step
      _callbacks.Remove(id);
      due.Callback((_clockMs - due.DueMs) / 1000f);
    }
  }

  private void DoSetBlock(int id, BlockPos pos, ItemStack? placedBy) {
    if (
      _blocksById.TryGetValue(id, out Block? placed) && !placed.ForFluidsLayer
    ) {
      WriteCell(id, pos, spawnEntity: false);
      placed.OnBlockPlaced(World, pos, placedBy);
      return;
    }
    DoSetBlock(id, pos);
  }

  private void DoSetBlock(int id, BlockPos pos) =>
    WriteCell(id, pos, spawnEntity: true);

  private void WriteCell(int id, BlockPos pos, bool spawnEntity) {
    if (
      _blocksById.TryGetValue(id, out Block? fluid)
      && id != 0
      && fluid.ForFluidsLayer
    ) {
      DoSetBlock(0, pos);
      _fluids[pos] = fluid;
      return;
    }

    if (
      RunsRemovalHooks
      && _blocks.TryGetValue(pos, out Block? replaced)
      && replaced.BlockId != id
    )
      replaced.OnBlockRemoved(World, pos);

    if (
      RunsRemovalHooks
      && _blocks.TryGetValue(pos, out Block? previous)
      && previous.BlockId != id
      && _blockEntities.Remove(pos, out BlockEntity? removed)
    )
      removed.OnBlockRemoved();

    if (id == 0) {
      _blocks.Remove(pos);
      _blockEntities.Remove(pos);
      _unloadedEntities.Remove(pos);
      return;
    }
    if (!_blocksById.TryGetValue(id, out var b))
      return;
    _blocks[pos] = b;

    // Engine parity: placing a block with an entity class (re)creates its block entity when a
    // factory is registered for it.
    if (
      spawnEntity
      && b.EntityClass is { } entityClass
      && (
        !_blockEntities.TryGetValue(pos, out var existing)
        || existing.Block != b
      )
    ) {
      existing?.OnBlockUnloaded();
      DoSpawnBlockEntity(entityClass, pos);
    }
  }

  private void DoSetBlock(int id, BlockPos pos, int layer) {
    switch (layer) {
      case BlockLayersAccess.Solid:
        DoSetBlock(id, pos);
        return;
      case BlockLayersAccess.Fluid when id == 0:
        _fluids.Remove(pos);
        return;
      case BlockLayersAccess.Fluid:
        if (_blocksById.TryGetValue(id, out Block? b))
          _fluids[pos] = b;
        return;
      default:
        throw new ArgumentOutOfRangeException(
          nameof(layer),
          layer,
          "SetBlock writes the Solid or the Fluid layer"
        );
    }
  }

  private BlockEntity? DoSpawnBlockEntity(string classname, BlockPos pos) {
    Block block = GetBlock(pos);
    BlockEntity? be = _beFactories.TryGetValue(classname, out var factory)
      ? factory()
      : CreateRegisteredBlockEntity(classname, block);
    if (be == null)
      return null;
    be.Pos = pos.Copy();
    be.Block = block;
    if (SpawnInitializesBeforeRegistering) {
      be.Initialize(Api);
      _blockEntities[pos] = be;
      return be;
    }
    _blockEntities[pos] = be;
    be.Initialize(Api);
    return be;
  }

  private void DoExchangeBlock(int id, BlockPos pos) {
    if (!_blocksById.TryGetValue(id, out var b))
      return;
    _blocks[pos] = b;
    if (_blockEntities.TryGetValue(pos, out var be))
      be.OnExchanged(b);
  }

  private void DoBreak(BlockPos pos, IPlayer? byPlayer, float dropMultiplier) {
    if (BreakRunsBlockHooks) {
      GetBlock(pos).OnBlockBroken(World, pos, byPlayer, dropMultiplier);
      return;
    }

    // Routes through the real break lifecycle: drops contents, runs OnBlockRemoved.
    if (_blockEntities.TryGetValue(pos, out var be)) {
      be.OnBlockBroken();
      be.OnBlockRemoved();
    }
    _blocks.Remove(pos);
    _blockEntities.Remove(pos);
  }

  private void DoRemoveBlockEntity(BlockPos pos) {
    if (!RunsRemovalHooks || !_blockEntities.TryGetValue(pos, out var be))
      return;
    _blockEntities.Remove(pos);
    be.OnBlockRemoved();
  }

  #endregion
}
