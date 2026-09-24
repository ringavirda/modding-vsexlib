using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>Counts, per signal, the blocks of a domain that carry it: the facts the block laws and
/// the live generic layer choose their cases by (block class, entity class, behaviours, the groups
/// placement writes, footprint, layout, nosnow cells, construction stages, network membership,
/// mpenergy membership, mechanical power connector, inventory).</summary>
public static class BlockSignalCensus {
  /// <summary>One signal's count.</summary>
  /// <param name="Signal">The signal's name.</param>
  /// <param name="Blocktypes">Blocktypes (codes less their variant parts) with a variant that
  /// carries it.</param>
  /// <param name="Blocks">Registered variants that carry it.</param>
  public sealed record Count(string Signal, int Blocktypes, int Blocks);

  /// <summary>A domain's census.</summary>
  /// <param name="Domain">The domain counted.</param>
  /// <param name="Blocktypes">Blocktypes of the domain read.</param>
  /// <param name="Blocks">Registered variants of the domain read.</param>
  /// <param name="Counts">One entry per signal, every signal listed, in a fixed order.</param>
  public sealed record Result(
    string Domain,
    int Blocktypes,
    int Blocks,
    IReadOnlyList<Count> Counts
  ) {
    /// <summary>The count of <paramref name="signal"/>.</summary>
    /// <exception cref="KeyNotFoundException">No signal of that name exists.</exception>
    public Count this[string signal] =>
      Counts.FirstOrDefault(c => c.Signal == signal)
      ?? throw new KeyNotFoundException($"No signal named '{signal}'.");

    /// <summary>One header line, then one line per signal: its name, blocktypes and
    /// variants.</summary>
    public override string ToString() =>
      $"{Domain}: {Blocktypes} blocktypes, {Blocks} variants"
      + string.Concat(
        Counts.Select(c =>
          $"\n  {c.Signal,-14} {c.Blocktypes,4} blocktypes {c.Blocks,6} variants"
        )
      );
  }

  /// <summary>Stands every variant of <paramref name="defs"/>' blocks up in one world and counts
  /// the blocks of <paramref name="domain"/>.</summary>
  /// <remarks>The world registers the classes the install's vanilla mod systems register, then
  /// those of <paramref name="assemblies"/>, and starts a <see cref="MechanicalPowerMod"/>; it
  /// then defines exlib's structure filler and each variant as
  /// <see cref="TestWorld.DefineBlock"/> does, resolving drops once all are defined.</remarks>
  /// <param name="domain">The domain counted; definitions of other domains are stood up but not
  /// counted.</param>
  /// <param name="defs">The definitions to stand up; one of exlib's structure filler is
  /// skipped, since the world defines it first.</param>
  /// <param name="assemblies">The assemblies whose registered classes the definitions name,
  /// exlib's own included.</param>
  /// <param name="prepare">Runs on the world before any block is defined, to register the network
  /// types and mod systems the blocks need.</param>
  /// <exception cref="InvalidOperationException">No game install resolves, or a block's entity or
  /// behaviour throws while constructed.</exception>
  public static Result Run(
    string domain,
    IEnumerable<ExBlockDef> defs,
    IReadOnlyList<Assembly> assemblies,
    Action<TestWorld>? prepare = null
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
    return Run(world, domain);
  }

  /// <summary>Counts the blocks of <paramref name="domain"/> that <paramref name="world"/>
  /// registers, read against its class registry.</summary>
  /// <param name="world">A world whose blocks were registered through
  /// <see cref="TestWorld.DefineBlock"/> or <see cref="TestWorld.LoadAssets"/>.</param>
  /// <exception cref="InvalidOperationException">A block's entity or behaviour throws while
  /// constructed.</exception>
  public static Result Run(TestWorld world, string domain) {
    BlockSignals[] read =
    [
      .. world
        .World.Blocks.Where(b => b?.Code?.Domain == domain)
        .Select(b => BlockSignals.Of(b, world.Api.ClassRegistry)),
    ];
    return new Result(
      domain,
      read.Select(s => TypeOf(s.Block)).Distinct().Count(),
      read.Length,
      [
        .. BlockSignals
          .AllNames()
          .Select(name =>
          {
            BlockSignals[] carrying =
            [
              .. read.Where(s => s.Names().Contains(name)),
            ];
            return new Count(
              name,
              carrying.Select(s => TypeOf(s.Block)).Distinct().Count(),
              carrying.Length
            );
          }),
      ]
    );
  }

  private static string TypeOf(Block block) =>
    $"{block.Code.Domain}:{block.CodeWithoutParts(block.Variant?.Count ?? 0)}";
}
