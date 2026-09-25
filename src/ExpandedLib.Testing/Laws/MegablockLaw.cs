using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>A megablock's fillers exist exactly while its principal does: placing the principal
/// raises one filler on every cell its footprint declares, turned to its facing, and removing it by
/// the world clears them all.</summary>
public static class MegablockLaw {
  internal const string Name = "megablock";

  private static readonly int[] Angles = [0, 90, 180, 270];

  /// <summary>Sets every variant of each block of <paramref name="domain"/> that declares a
  /// footprint at a fresh cell of <paramref name="world"/> through the accessor's
  /// <c>SetBlock</c>, runs its <see cref="Block.OnBlockPlaced"/>, reads the fillers linked to it,
  /// then clears the principal through <c>SetBlock</c>, the path an explosion or a world edit takes,
  /// which never runs <see cref="Block.OnBlockBroken"/>.</summary>
  /// <remarks>A finding is a placement that throws, a filler count other than the footprint's, fillers
  /// standing elsewhere than the footprint turned to the variant's
  /// <see cref="BlockFilledMegastructure.StructureAngle"/> puts them (for another
  /// <see cref="IFillerHost"/>, where no facing of it puts them), a removal that throws, and a
  /// filler left linked to the removed principal.</remarks>
  /// <param name="world">A world holding every variant of the blocks judged and exlib's structure
  /// filler (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are placed.</param>
  /// <returns>The law's blocktypes, variants placed and findings, each keyed by the variant's
  /// code.</returns>
  public static BlockLaws.Law Run(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      Block[] hosts =
      [
        .. type.Where(b =>
          b is IFillerHost && BlockLaws.Signals(world, b).Footprint.Count > 0
        ),
      ];
      if (hosts.Length == 0)
        continue;
      blocks++;
      foreach (Block block in hosts) {
        cases++;
        BlockPos at = sites.Next();
        int declared = StructureFillers
          .ReadOffsets(((IFillerHost)block).FillerOffsets)
          .Count;
        try {
          world.Accessor.SetBlock(block.BlockId, at);
          block.OnBlockPlaced(world.World, at, new ItemStack(block));
        } catch (System.Exception e) {
          findings.Add($"{block.Code} placed threw {BlockLaws.Describe(e)}");
          continue;
        }

        BlockPos[] standing = FillersOf(world, at);
        if (standing.Length != declared)
          findings.Add(
            $"{block.Code} placed raised {standing.Length} of its {declared} filler cells"
          );
        else if (block is BlockFilledMegastructure host) {
          if (!Covers(host, at, host.StructureAngle, standing))
            findings.Add(
              $"{block.Code} placed raised fillers elsewhere than its footprint turned to "
                + $"{host.StructureAngle} puts them: "
                + string.Join(", ", standing.Select(p => p.ToString()))
            );
        } else if (
            !Angles.Any(angle => Covers((IFillerHost)block, at, angle, standing))
          )
          findings.Add(
            $"{block.Code} placed raised fillers where no facing of its footprint puts them: "
              + string.Join(", ", standing.Select(p => p.ToString()))
          );

        try {
          world.Accessor.SetBlock(0, at);
        } catch (System.Exception e) {
          findings.Add(
            $"{block.Code} removed by the world threw {BlockLaws.Describe(e)}"
          );
          continue;
        }
        BlockPos[] left = FillersOf(world, at);
        if (left.Length > 0)
          findings.Add(
            $"{block.Code} removed by the world left {left.Length} filler cells standing"
          );
      }
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  private static bool Covers(
    IFillerHost host,
    BlockPos at,
    int angle,
    BlockPos[] standing
  ) =>
    StructureFillers
      .FootprintCells(host, at, angle)
      .Select(c => c.Pos)
      .ToHashSet()
      .SetEquals(standing);

  private static BlockPos[] FillersOf(TestWorld world, BlockPos principal) =>
    [
      .. world
        .BlockEntities.Where(e =>
          e.Value is BlockEntityStructureFiller { Principal: { } p }
          && p.Equals(principal)
          && world.GetBlock(e.Key) is BlockStructureFiller
        )
        .Select(e => e.Key.Copy()),
    ];
}
