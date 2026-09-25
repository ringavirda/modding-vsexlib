using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>Runs the block laws over every block of a domain, each law over the blocks whose
/// signals (<c>BlockSignals</c>) it applies to, in every variant: <see cref="PlacementLaw"/>,
/// <see cref="BreakLaw"/> (1.22 and later), <see cref="MultiblockLaw"/>,
/// <see cref="MegablockLaw"/>, <see cref="InfoLaw"/> and <see cref="ReloadLaw"/>.</summary>
public static class BlockLaws {
  /// <summary>What one law covered and what it found.</summary>
  /// <param name="Name">The law's name.</param>
  /// <param name="Blocks">Blocktypes (codes less their variant parts, with their variant group
  /// names) the law applied to.</param>
  /// <param name="Cases">Cases it ran: placements, breaks, structures stood up, or block entities
  /// read or reloaded.</param>
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
  /// <remarks>Each law but the break law gets a world of its own, stood up as the census stands
  /// one; the break law stands a fresh world per break.</remarks>
  /// <param name="domain">The domain whose blocks are judged; definitions of other domains are
  /// stood up beside them, so a layout can name their blocks.</param>
  /// <param name="defs">The definitions to stand up; one of exlib's structure filler is
  /// skipped.</param>
  /// <param name="assemblies">The assemblies whose registered classes the definitions name,
  /// exlib's own included.</param>
  /// <param name="prepare">Runs on each world before any block is defined, to register the
  /// network types and mod systems the blocks need.</param>
  /// <returns>The laws named <c>placement</c>, <c>break</c> (from 1.22 only), <c>multiblock</c>,
  /// <c>megablock</c>, <c>info</c> and <c>reload</c>, in that order.</returns>
  /// <exception cref="InvalidOperationException">No game install resolves, or a block's entity or
  /// behaviour throws while its signals are read.</exception>
  public static Result Run(
    string domain,
    IEnumerable<ExBlockDef> defs,
    IReadOnlyList<Assembly> assemblies,
    Action<TestWorld>? prepare = null
  ) {
    ExBlockDef[] all = [.. defs];
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
    return new Result(laws);
  }

  /// <summary>A world holding the install's vanilla classes, those of
  /// <paramref name="assemblies"/>, a started <see cref="MechanicalPowerMod"/> and
  /// <see cref="RoomRegistry"/>, a climate of 20 C everywhere, exlib's structure filler and every
  /// variant of <paramref name="defs"/>.</summary>
  internal static TestWorld Stand(
    IEnumerable<ExBlockDef> defs,
    IReadOnlyList<Assembly> assemblies,
    Action<TestWorld>? prepare
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
    // The registry caches its accessor per thread; a later world would read an earlier one's cells.
    ReflectionHelpers.SetStaticField(
      typeof(RoomRegistry),
      "blockAccessor",
      null
    );
    var rooms = new RoomRegistry();
    world.Mods.Register(rooms);
    rooms.Start(world.Api);
    prepare?.Invoke(world);

    ExBlockDef filler = ExDefinitions
      .DefinitionsOf(typeof(BlockStructureFiller), "exlib")
      .Single();
    world.DefineBlocks(
      defs.Where(d => d.Domain != filler.Domain || d.Code != filler.Code)
        .Prepend(filler)
    );
    return world;
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
    /// its entity, and runs its <see cref="Block.OnBlockPlaced"/>.</summary>
    internal bool Place(bool judged) =>
      Step(
        "placed",
        judged,
        () => {
          _world.Accessor.SetBlock(Block.BlockId, At);
          Block.OnBlockPlaced(_world.World, At, new ItemStack(Block));
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
        clean = false;
        _world.Log.Expect(type, message);
        if (judged)
          _faults?.Add($"{Block.Code} {what} logged {type}: {message}");
      }
      return clean;
    }
  }

  /// <summary>The first line of <paramref name="e"/>'s stack beside its type and message.</summary>
  internal static string Describe(Exception e) {
    string? frame = e
      .StackTrace?.Split('\n')
      .Select(l => l.Trim())
      .FirstOrDefault();
    return $"{e.GetType().Name}: {e.Message} ({frame})";
  }
}
