using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>Runs the block laws over every block of a domain, each law over the blocks whose
/// signals (<c>BlockSignals</c>) it applies to, in every variant; <see cref="Run"/> names them in
/// order.</summary>
public static class BlockLaws {
  /// <summary>What one law covered and what it found.</summary>
  /// <param name="Name">The law's name.</param>
  /// <param name="Blocks">Blocktypes (codes less their variant parts, with their variant group
  /// names) the law applied to.</param>
  /// <param name="Cases">Cases it ran: placements, breaks, structures stood up, block entities
  /// read or reloaded, faces a neighbour came and went on, network pairs walked, or clicks
  /// made.</param>
  /// <param name="Findings">One line per finding, each starting with the variant code it is
  /// about and a space.</param>
  public sealed record Law(
    string Name,
    int Blocks,
    int Cases,
    IReadOnlyList<string> Findings
  ) {
    /// <summary>The law's name, blocktypes, cases and finding count.</summary>
    public override string ToString() =>
      $"{Name}: {Blocks} blocktypes, {Cases} cases, {Findings.Count} finding(s)";
  }

  /// <summary>Every law's result, in the order the laws ran.</summary>
  /// <param name="Laws">One entry per law run.</param>
  public sealed record Result(IReadOnlyList<Law> Laws) {
    /// <summary>The result of the law named <paramref name="name"/>.</summary>
    /// <exception cref="KeyNotFoundException">No law of that name ran.</exception>
    public Law this[string name] =>
      Laws.FirstOrDefault(l => l.Name == name)
      ?? throw new KeyNotFoundException($"No law named '{name}' ran.");

    /// <summary>One line per law, then its findings, indented.</summary>
    public override string ToString() =>
      string.Join(
        "\n",
        Laws.Select(l => l + string.Concat(l.Findings.Select(f => "\n  " + f)))
      );
  }

  /// <summary>The key of a finding: the variant code it starts with.</summary>
  /// <param name="finding">One line of <see cref="Law.Findings"/>.</param>
  /// <returns>The text before the first space, or the whole line without one.</returns>
  [CheckHelper(
    "a key for FindingLists.Assert; the laws' planted tests key their findings by it"
  )]
  public static string CodeOf(string finding) {
    int space = finding.IndexOf(' ');
    return space < 0 ? finding : finding[..space];
  }

  /// <summary>Stands every variant of <paramref name="defs"/>' blocks up and runs each law over the
  /// blocks of <paramref name="domain"/>.</summary>
  /// <remarks>Each law but the break law gets a world of its own; the break law stands one per
  /// break. The interaction law's world also holds the item definitions.</remarks>
  /// <param name="domain">The domain whose blocks are judged; other domains' definitions stand
  /// beside them, for layouts and help to name.</param>
  /// <param name="defs">Block and item definitions; others, and exlib's filler, are
  /// skipped.</param>
  /// <param name="assemblies">Where the definitions' classes live, exlib's included.</param>
  /// <param name="prepare">Registers on each world the network types and mod systems the blocks
  /// need, before any is defined.</param>
  /// <returns>In order, the laws <c>placement</c>, <c>break</c> (1.22 on), <c>multiblock</c>,
  /// <c>megablock</c>, <c>info</c>, <c>reload</c>, <c>neighbour</c>, <c>network</c>,
  /// <c>interaction</c>.</returns>
  /// <exception cref="InvalidOperationException">No game install resolves, or a block's entity or
  /// behaviour throws while its signals are read.</exception>
  public static Result Run(
    string domain,
    IEnumerable<IExDef> defs,
    IReadOnlyList<Assembly> assemblies,
    Action<TestWorld>? prepare = null
  ) {
    IExDef[] given = [.. defs];
    ExBlockDef[] all = [.. given.OfType<ExBlockDef>()];
    ExItemDef[] items = [.. given.OfType<ExItemDef>()];
    var laws = new List<Law>
    {
      PlacementLaw.Run(Stand(all, assemblies, prepare), domain),
    };
#if GAME_GE_1_22
    laws.Add(
      BreakLaw.Run(all.Where(d => d.Domain == domain), assemblies, prepare)
    );
#endif
    laws.Add(MultiblockLaw.Run(Stand(all, assemblies, prepare), domain));
    laws.Add(MegablockLaw.Run(Stand(all, assemblies, prepare), domain));
    laws.Add(InfoLaw.Run(Stand(all, assemblies, prepare), domain));
    laws.Add(ReloadLaw.Run(Stand(all, assemblies, prepare), domain));
    laws.Add(NeighbourLaw.Run(Stand(all, assemblies, prepare), domain));
    laws.Add(NetworkLaw.Run(Stand(all, assemblies, prepare), domain));
    laws.Add(
      InteractionLaw.Run(Stand(all, assemblies, prepare, items), domain)
    );
    return new Result(laws);
  }

  /// <summary>A world holding the install's vanilla classes, those of
  /// <paramref name="assemblies"/>, a started <see cref="MechanicalPowerMod"/> and
  /// <see cref="RoomRegistry"/>, a climate of 20 C everywhere, every variant of
  /// <paramref name="items"/>, then exlib's structure filler and every variant of
  /// <paramref name="defs"/>.</summary>
  internal static TestWorld Stand(
    IEnumerable<ExBlockDef> defs,
    IReadOnlyList<Assembly> assemblies,
    Action<TestWorld>? prepare,
    IEnumerable<ExItemDef>? items = null
  ) {
    var world = new TestWorld();
    world.RegisterVanillaClasses();
    world.RegisterClasses([.. assemblies]);
    var power = new MechanicalPowerMod();
    world.Mods.Register(power);
    power.Start(world.Api);
    // A container's info reads the climate: 20 C everywhere, at any date.
    world
      .Accessor.GetClimateAt(
        Arg.Any<BlockPos>(),
        Arg.Any<EnumGetClimateMode>(),
        Arg.Any<double>()
      )
      .Returns(_ => new ClimateCondition { Temperature = 20 });
    var rooms = new RoomRegistry();
    world.Mods.Register(rooms);
    rooms.Start(world.Api);
#if GAME_GE_1_21
    // The registry caches its accessor per thread; a later world would read an earlier one's cells.
    ReflectionHelpers.SetStaticField(
      typeof(RoomRegistry),
      "blockAccessor",
      null
    );
#endif
    prepare?.Invoke(world);
    if (items != null)
      world.LoadVanilla(VanillaBlocks, VanillaItems);
    world.DefineItems(items ?? []);

    ExBlockDef filler = ExDefinitions
      .DefinitionsOf(typeof(BlockStructureFiller), "exlib")
      .Single();
    world.DefineBlocks(
      defs.Where(d => d.Domain != filler.Domain || d.Code != filler.Code)
        .Prepend(filler)
    );
    if (items != null) {
      StandInIngredients(world);
      var reinforcement = new ModSystemBlockReinforcement();
      world.Mods.Register(reinforcement);
      reinforcement.Start(world.Api);
    }
    return world;
  }

  /// <summary>The vanilla blocktypes the worlds of the hand laws load: the containers and vessels
  /// the family's help names (buckets, tool molds, crucibles) and torches, which ignite.</summary>
  internal static readonly string[] VanillaBlocks =
  [
    "bucket",
    "torch",
    "toolmold",
    "crucible",
  ];

  /// <summary>The vanilla itemtypes the worlds of the hand laws load: the tools and materials the
  /// family's help names.</summary>
  internal static readonly string[] VanillaItems =
  [
    "chisel",
    "wrench",
    "firestarter",
    "waterportion",
    "clay",
  ];

  /// <summary>Registers a stand-in in <paramref name="world"/> for each construction ingredient
  /// its blocks' stages name that nothing there answers to: a bare item, or a plain block under the
  /// next free id. A whole code stands for itself; a wildcard stands for each of its allowed
  /// variants, and gives each the variant under the key the stage stores, where it names none; a
  /// code holding a stored key (<c>{</c>) is skipped.</summary>
  private static void StandInIngredients(TestWorld world) {
    foreach (
      JsonObject ingredient in world
        .World.Blocks.Where(b => b?.BlockEntityBehaviors != null)
        .SelectMany(b => b.BlockEntityBehaviors)
        .Where(b => b.Name == BlockSignals.ConstructionBehavior)
        .SelectMany(b => b.properties?["stages"].AsArray() ?? [])
        .SelectMany(stage => stage["requireStacks"].AsArray() ?? [])
    ) {
      string? code = ingredient["code"].AsString();
      if (code == null || code.Contains('{'))
        continue;
      bool block = ingredient["type"].AsString("item") == "block";
      string? key = ingredient["storeWildCard"].AsString();
      if (!code.Contains('*')) {
        StandIn(world, code, block);
        continue;
      }
      foreach (string variant in ingredient["allowedVariants"].AsArray<string>() ?? [])
        if (
          StandIn(world, code.Replace("*", variant), block) is { } collectible
          && key != null
          && collectible.Variant[key] == null
        )
          collectible.VariantStrict[key] = variant;
    }
  }

  private static CollectibleObject? StandIn(
    TestWorld world,
    string code,
    bool block
  ) {
    var location = new AssetLocation(code);
    if (!block)
      return world.GetItem(location)
        ?? world.RegisterItem(location.ToString());
    if (world.World.GetBlock(location) is { } known)
      return known;
    Block standIn = TestBlocks.Configure(
      new Block(),
      location.ToString(),
      world.World.Blocks.Max(b => b?.BlockId ?? 0) + 1
    );
    ReflectionHelpers.SetField(standIn, "api", world.Api);
    world.Register(standIn);
    return standIn;
  }

  /// <summary>The blocks of <paramref name="domain"/> in <paramref name="world"/> grouped by
  /// blocktype and variant group names, so two definitions sharing a code and differing in their
  /// groups stay apart; each group in registration order, the groups ordered by key.</summary>
  internal static IEnumerable<IGrouping<string, Block>> Blocktypes(
    TestWorld world,
    string domain
  ) =>
    world
      .World.Blocks.Where(b => b?.Code?.Domain == domain)
      .GroupBy(b => $"{TypeOf(b)}({string.Join(",", b.Variant?.Keys ?? [])})")
      .OrderBy(g => g.Key, StringComparer.Ordinal);

  /// <summary>The signals of <paramref name="block"/>, read against <paramref name="world"/>'s
  /// class registry.</summary>
  internal static BlockSignals Signals(TestWorld world, Block block) =>
    BlockSignals.Of(block, world.Api.ClassRegistry);

  /// <summary>The blocktype of <paramref name="block"/>: its code less its variant parts, the whole
  /// code for a block without variant groups.</summary>
  internal static string TypeOf(Block block) =>
    block.Variant is not { Count: > 0 } variant
      ? block.Code.ToString()
      : $"{block.Code.Domain}:{block.CodeWithoutParts(variant.Count)}";

  /// <summary>The cells holding a <see cref="BlockStructureFiller"/> whose entity names
  /// <paramref name="principal"/> as its principal.</summary>
  internal static BlockPos[] FillersOf(TestWorld world, BlockPos principal) =>
    [
      .. world
        .BlockEntities.Where(e =>
          e.Value is BlockEntityStructureFiller { Principal: { } p }
          && p.Equals(principal)
          && world.GetBlock(e.Key) is BlockStructureFiller
        )
        .Select(e => e.Key.Copy()),
    ];

  /// <summary>The full solid block the neighbour law sets against a face and stands a block on:
  /// a stand-in coded <see cref="SolidCode"/>, registered in <paramref name="world"/> on first
  /// use under the next free id.</summary>
  internal static Block Solid(TestWorld world) {
    if (world.World.GetBlock(new AssetLocation(SolidCode)) is { } known)
      return known;
    Block solid = TestBlocks.Configure(
      new Block(),
      SolidCode,
      world.World.Blocks.Max(b => b?.BlockId ?? 0) + 1
    );
    ReflectionHelpers.SetField(solid, "api", world.Api);
    world.Register(solid);
    return solid;
  }

  internal const string SolidCode = "game:rock-granite";

  /// <summary>Hands out cells for one case each, 32 blocks apart along X and Z, from
  /// (64, 64, 64).</summary>
  internal sealed class Sites {
    private int _next;

    /// <summary>The next unused site.</summary>
    public BlockPos Next() {
      int n = _next++;
      return new BlockPos(64 + 32 * (n % 64), 64, 64 + 32 * (n / 64));
    }
  }

  /// <summary>One variant's block entity, stood up at its own site and stepped through the
  /// lifecycle the info and reload laws judge. Each step's throw and each log entry it wrote is a
  /// fault keyed by the variant's code; a logged entry is declared expected to the world's logger,
  /// so the law's finding, not the log rule, reports it.</summary>
  internal sealed class EntityCase {
    /// <summary>Block entity time one tick step advances, in milliseconds.</summary>
    internal const int TickMs = 5000;

    private readonly TestWorld _world;
    private readonly HashSet<long> _listenersBefore;
    private readonly List<string>? _faults;

    /// <param name="faults">Where the faults of steps run with <c>judged</c> set go; null drops
    /// them.</param>
    internal EntityCase(
      TestWorld world,
      Block block,
      BlockPos at,
      List<string>? faults
    ) {
      _world = world;
      Block = block;
      At = at;
      _faults = faults;
      _listenersBefore = world.TickListenerIds();
    }

    internal Block Block { get; }

    internal BlockPos At { get; }

    /// <summary>The block entity standing at <see cref="At"/>, or null.</summary>
    internal BlockEntity? Entity => _world.GetBlockEntity(At);

    /// <summary>Sets the block through the accessor's <c>SetBlock</c>, which spawns and initialises
    /// its entity, and runs its <see cref="Block.OnBlockPlaced"/> with <paramref name="from"/>, a
    /// plain stack of the block when null.</summary>
    internal bool Place(bool judged, ItemStack? from = null) =>
      Step(
        from == null
          ? "placed"
          : "placed from a stack carrying blockEntityAttributes",
        judged,
        () => {
          _world.Accessor.SetBlock(Block.BlockId, At);
          Block.OnBlockPlaced(_world.World, At, from ?? new ItemStack(Block));
        }
      );

    /// <summary>Advances the tick listeners registered since the case began by
    /// <see cref="TickMs"/>.</summary>
    internal bool Tick(bool judged) {
      HashSet<long> own = _world.TickListenerIds();
      own.ExceptWith(_listenersBefore);
      return Step(
        "ticked",
        judged,
        () => _world.AdvanceBlockEntityTime(TickMs, own)
      );
    }

    /// <summary>The tree an entity of the block writes when it is spawned and initialised at
    /// <paramref name="spare"/> with no placement, tick or load, as the reloaded instance is before
    /// its tree is read; the cell is cleared after. Null when a step throws or logs.</summary>
    internal TreeAttribute? FreshTree(BlockPos spare, bool judged) {
      var tree = new TreeAttribute();
      bool clean = Step(
        "fresh",
        judged,
        () => {
          _world.Accessor.SetBlock(Block.BlockId, spare);
          _world.GetBlockEntity(spare)?.ToTreeAttributes(tree);
          _world.Accessor.SetBlock(0, spare);
        }
      );
      return clean ? tree : null;
    }

    /// <summary>Sets <paramref name="neighbour"/> at <paramref name="cell"/> and clears it again,
    /// each change followed by <see cref="TestWorld.NotifyNeighbours"/>.</summary>
    internal bool Neighbour(
      BlockPos cell,
      Block neighbour,
      string what,
      bool judged
    ) =>
      Step(
        what,
        judged,
        () => {
          _world.Accessor.SetBlock(neighbour.BlockId, cell);
          _world.NotifyNeighbours(cell);
          _world.Accessor.SetBlock(0, cell);
          _world.NotifyNeighbours(cell);
        }
      );

    /// <summary>Runs <see cref="TestWorld.Reload"/> at <see cref="At"/>.</summary>
    internal bool Reload(bool judged) =>
      Step("reloaded", judged, () => _world.Reload(At));

    /// <summary>The text the entity's <see cref="BlockEntity.GetBlockInfo"/> writes for
    /// <paramref name="player"/>, or null when it throws or logs.</summary>
    internal string? Info(IPlayer player, string when, bool judged) {
      BlockEntity entity = Entity!;
      var text = new StringBuilder();
      return Step(
        $"info {when}",
        judged,
        () => entity.GetBlockInfo(player, text)
      )
        ? text.ToString()
        : null;
    }

    /// <summary>The tree <paramref name="entity"/> writes, or null when writing it throws or
    /// logs.</summary>
    internal TreeAttribute? Tree(BlockEntity entity, string when, bool judged) {
      var tree = new TreeAttribute();
      return Step($"{when} tree", judged, () => entity.ToTreeAttributes(tree))
        ? tree
        : null;
    }

    /// <summary>What the block at <paramref name="cell"/> shows <paramref name="player"/> through
    /// <see cref="Block.GetPlacedBlockInteractionHelp"/> for <paramref name="selection"/>; empty
    /// when it throws or logs, which is never a fault, since the engine asks only on a
    /// client.</summary>
    internal WorldInteraction[] Help(
      BlockPos cell,
      BlockSelection selection,
      IPlayer player
    ) {
      WorldInteraction[]? help = null;
      return Step(
        "help",
        false,
        () =>
          help = _world
            .GetBlock(cell)
            .GetPlacedBlockInteractionHelp(_world.World, selection, player)
      )
        ? help ?? []
        : [];
    }

    /// <summary>Right-clicks <paramref name="selection"/> as the engine does on the server: the
    /// cell's block's <see cref="Block.OnBlockInteractStart"/>, and when that declines, the held
    /// collectible's <see cref="CollectibleObject.OnHeldInteractStart"/>; whichever takes the
    /// click is stepped and stopped at <see cref="UseSeconds"/>, the held side through whatever
    /// the slot holds once the start has run.</summary>
    /// <returns>Whether the block or the held collectible took the click; null when a step
    /// threw or logged.</returns>
    internal bool? Use(
      BlockSelection selection,
      IPlayer player,
      string what,
      bool judged
    ) {
      bool taken = false;
      Block block = _world.GetBlock(selection.Position);
      return Step(
        what,
        judged,
        () => {
          taken = block.OnBlockInteractStart(_world.World, player, selection);
          if (taken) {
            block.OnBlockInteractStep(UseSeconds, _world.World, player, selection);
            block.OnBlockInteractStop(UseSeconds, _world.World, player, selection);
            return;
          }
          ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
          if (slot?.Itemstack?.Collectible is not { } held)
            return;
          var handling = EnumHandHandling.NotHandled;
          held.OnHeldInteractStart(
            slot,
            player.Entity,
            selection,
            null,
            true,
            ref handling
          );
          taken = handling != EnumHandHandling.NotHandled;
          if (!taken || slot.Itemstack?.Collectible is not { } now)
            return;
          now.OnHeldInteractStep(UseSeconds, slot, player.Entity, selection, null);
          now.OnHeldInteractStop(UseSeconds, slot, player.Entity, selection, null);
        }
      )
        ? taken
        : null;
    }

    /// <summary>How long a click is held, in seconds.</summary>
    internal const float UseSeconds = 1f;

    /// <summary>Breaks the block at <see cref="At"/> through the accessor's <c>BreakBlock</c> for
    /// <paramref name="player"/>.</summary>
    internal bool Break(IPlayer player, bool judged) =>
      Step(
        "broken",
        judged,
        () => _world.Accessor.BreakBlock(At, player, 1f)
      );

    /// <summary>Runs <paramref name="act"/>; false when it threw or logged.</summary>
    private bool Step(string what, bool judged, Action act) {
      int logged = _world.Log.Entries.Count;
      bool clean = true;
      try {
        act();
      } catch (Exception e) {
        clean = false;
        if (judged)
          _faults?.Add($"{Block.Code} {what} threw {Describe(e)}");
      }
      foreach (
        (EnumLogType type, string message) in _world
          .Log.Entries.Skip(logged)
          .ToList()
      ) {
        _world.Log.Expect(type, message);
        // The engine audits every stack a player takes or gives; that is no fault.
        if (type == EnumLogType.Audit)
          continue;
        clean = false;
        if (judged)
          _faults?.Add($"{Block.Code} {what} logged {type}: {message}");
      }
      return clean;
    }
  }

  /// <summary>A structure's cell named by its offset from the principal: "its cell" for the
  /// principal's own.</summary>
  internal static string CellName(Vec3i offset) =>
    offset is { X: 0, Y: 0, Z: 0 }
      ? "its cell"
      : $"its cell at ({offset.X}, {offset.Y}, {offset.Z})";

  /// <summary>The first line of <paramref name="e"/>'s stack beside its type and message.</summary>
  internal static string Describe(Exception e) {
    string? frame = e
      .StackTrace?.Split('\n')
      .Select(l => l.Trim())
      .FirstOrDefault();
    return $"{e.GetType().Name}: {e.Message} ({frame})";
  }
}
