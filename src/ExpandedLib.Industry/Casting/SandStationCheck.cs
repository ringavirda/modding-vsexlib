using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Structures;
using Vintagestory.API.Common;

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
