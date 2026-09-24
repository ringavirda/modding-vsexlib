using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>Runs the block laws over every block of a domain, each law over the blocks whose
/// signals (<c>BlockSignals</c>) it applies to, in every variant: <see cref="PlacementLaw"/>,
/// <see cref="BreakLaw"/> (1.22 and later), <see cref="MultiblockLaw"/> and
/// <see cref="MegablockLaw"/>.</summary>
public static class BlockLaws {
  /// <summary>What one law covered and what it found.</summary>
  /// <param name="Name">The law's name.</param>
  /// <param name="Blocks">Blocktypes (codes less their variant parts) the law applied to.</param>
  /// <param name="Cases">Cases it ran: placements, breaks, or structures stood up.</param>
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
  /// <returns>The laws named <c>placement</c>, <c>break</c> (from 1.22 only), <c>multiblock</c>
  /// and <c>megablock</c>, in that order.</returns>
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
    return new Result(laws);
  }

  /// <summary>A world holding the install's vanilla classes, those of
  /// <paramref name="assemblies"/>, a started <see cref="MechanicalPowerMod"/>, exlib's structure
  /// filler and every variant of <paramref name="defs"/>.</summary>
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
  /// blocktype, each group in registration order, the groups ordered by blocktype.</summary>
  internal static IEnumerable<IGrouping<string, Block>> Blocktypes(
    TestWorld world,
    string domain
  ) =>
    world
      .World.Blocks.Where(b => b?.Code?.Domain == domain)
      .GroupBy(TypeOf)
      .OrderBy(g => g.Key, StringComparer.Ordinal);

  /// <summary>The signals of <paramref name="block"/>, read against <paramref name="world"/>'s
  /// class registry.</summary>
  internal static BlockSignals Signals(TestWorld world, Block block) =>
    BlockSignals.Of(block, world.Api.ClassRegistry);

  /// <summary>The blocktype of <paramref name="block"/>: its code less its variant parts.</summary>
  internal static string TypeOf(Block block) =>
    $"{block.Code.Domain}:{block.CodeWithoutParts(block.Variant?.Count ?? 0)}";

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

  /// <summary>The first line of <paramref name="e"/>'s stack beside its type and message.</summary>
  internal static string Describe(Exception e) {
    string? frame = e
      .StackTrace?.Split('\n')
      .Select(l => l.Trim())
      .FirstOrDefault();
    return $"{e.GetType().Name}: {e.Message} ({frame})";
  }
}
