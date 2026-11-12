using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Casting;

/// <summary>Finds the blocks on a sand station entity that cannot work: an unregistered station or no molten cell.</summary>
internal static class SandStationCheck {
  /// <summary>Judges every block whose entity class is <see cref="BlockEntitySandStation"/> or a subclass.</summary>
  /// <param name="blocks">The loaded blocks; those without a code are skipped.</param>
  /// <param name="classes">The class registry the entity and behaviour names resolve in.</param>
  /// <returns>One line per failing block, <c>&lt;code&gt;: &lt;reason&gt;</c>.</returns>
  public static IEnumerable<string> Run(
    IEnumerable<Block> blocks,
    IClassRegistryAPI classes
  ) {
    foreach (Block block in blocks) {
      if (
        block is not { Code: not null, EntityClass: { } key }
        || classes.GetBlockEntity(key) is not { } type
        || !typeof(BlockEntitySandStation).IsAssignableFrom(type)
      )
        continue;

      string? station = block.Attributes?["sandStation"]?[
        "station"
      ]?.AsString();
      if (station == null)
        yield return $"{block.Code}: it names no station in attributes.sandStation.station";
      else if (!CastingStationRegistry.Shared.TryGet(station, out _))
        yield return $"{block.Code}: station '{station}' is not registered";

      if (!HostsCell(block, classes))
        yield return $"{block.Code}: it declares no BEBehaviorMoltenCell";

      foreach (string line in LaunderFaceLines(block))
        yield return line;
    }
  }

  private static IEnumerable<string> LaunderFaceLines(Block block) {
    var footprint = (
      block is IFillerHost host
        ? StructureFillers.ReadOffsets(host.FillerOffsets).Select(o => o.Offset)
        : []
    )
      .Select(o => (o.X, o.Y, o.Z))
      .Append((0, 0, 0))
      .ToHashSet();
    var declared = new HashSet<(int, int, int, int)> { (0, 0, 0, 0) };
    foreach (
      JsonObject entry in block.Attributes?["sandStation"]?[
        "launderFaces"
      ]?.AsArray() ?? []
    ) {
      var cell = (entry["x"].AsInt(), entry["y"].AsInt(), entry["z"].AsInt());
      int turns = entry["turns"].AsInt() & 3;
      if (!footprint.Contains(cell))
        yield return $"{block.Code}: launder face at {cell} is not a cell of its footprint";
      // In the footprint's own frame the principal's face looks north.
      BlockFacing facing = Enumerable
        .Range(0, turns)
        .Aggregate(BlockFacing.NORTH, (f, _) => f.GetCW());
      if (
        footprint.Contains(
          (
            cell.Item1 + facing.Normali.X,
            cell.Item2 + facing.Normali.Y,
            cell.Item3 + facing.Normali.Z
          )
        )
      )
        yield return $"{block.Code}: launder face at {cell} faces into its own footprint";
      if (!declared.Add((cell.Item1, cell.Item2, cell.Item3, turns)))
        yield return $"{block.Code}: launder face at {cell} turned {turns} is declared twice";
    }
  }

  private static bool HostsCell(Block block, IClassRegistryAPI classes) =>
    (block.BlockEntityBehaviors ?? []).Any(b => IsCell(classes, b.Name))
    || (
      block is IFillerHost host
      && StructureFillers
        .ReadOffsets(host.FillerOffsets)
        .Any(o => (o.Behaviors ?? []).Any(b => IsCell(classes, b.Code)))
    );

  private static bool IsCell(IClassRegistryAPI classes, string name) =>
    classes.GetBlockEntityBehaviorClass(name) is { } type
    && typeof(BEBehaviorMoltenCell).IsAssignableFrom(type);
}
