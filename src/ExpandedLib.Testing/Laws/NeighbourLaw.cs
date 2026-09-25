using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>A block stands as it was after a neighbour comes and goes: a solid block set against
/// each of its free faces and cleared again, each change announced to its neighbours, throws
/// nothing, logs nothing and leaves every cell of the block with its code and entity tree.</summary>
public static class NeighbourLaw {
  internal const string Name = "neighbour";

  /// <summary>Sets every variant of each block of <paramref name="domain"/> at a fresh cell of
  /// <paramref name="world"/> on a solid block, as <see cref="InfoLaw.Run"/> does, stands the solid
  /// block under each filler cell that has air below, then on each free face sets the solid
  /// block, runs <see cref="TestWorld.NotifyNeighbours"/>, clears it and runs it again.</summary>
  /// <remarks>The block's cells are its own and those of the fillers linked to it; a free face is
  /// one of theirs whose neighbour is air. A finding is a set or clear that throws or logs, a cell
  /// holding another code afterwards, an entity gone, and a tree with a key lost, added, of
  /// another type or of another value (<c>ExTree.Differences</c>). A variant whose placement throws
  /// or logs is <see cref="InfoLaw"/>'s finding and is skipped; a variant is judged no further
  /// after its first finding. A block whose footprint holds the cell below its own is placed on
  /// air; no support is announced to the block.</remarks>
  /// <param name="world">A world holding every variant of the blocks judged
  /// (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are placed.</param>
  /// <returns>The law's blocktypes, faces tried and findings, each keyed by the variant's
  /// code.</returns>
  public static BlockLaws.Law Run(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    Block solid = BlockLaws.Solid(world);
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      blocks++;
      foreach (Block block in type) {
        BlockPos site = sites.Next();
        if (
          !BlockLaws
            .Signals(world, block)
            .Footprint.Any(f => f.Offset is { X: 0, Y: -1, Z: 0 })
        )
          world.Accessor.SetBlock(solid.BlockId, site.DownCopy());
        var step = new BlockLaws.EntityCase(world, block, site, findings);
        if (!step.Place(judged: false))
          continue;
        BlockPos[] cells =
        [
          step.At.Copy(),
          .. BlockLaws.FillersOf(world, step.At),
        ];
        foreach (BlockPos cell in cells)
          if (world.GetBlock(cell.DownCopy()).Id == 0)
            world.Accessor.SetBlock(solid.BlockId, cell.DownCopy());
        (string Code, TreeAttribute? Tree)[] before =
        [
          .. cells.Select(c => State(world, step, c)),
        ];
        Judge(world, step, solid, cells, before, findings, ref cases);
      }
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  private static void Judge(
    TestWorld world,
    BlockLaws.EntityCase step,
    Block solid,
    BlockPos[] cells,
    (string Code, TreeAttribute? Tree)[] before,
    List<string> findings,
    ref int cases
  ) {
    foreach (BlockPos cell in cells)
      foreach (BlockFacing face in BlockFacing.ALLFACES) {
        BlockPos next = cell.AddCopy(face);
        if (world.GetBlock(next).Id != 0)
          continue;
        cases++;
        string what = cell.Equals(step.At)
          ? $"with a neighbour on its {face.Code} face"
          : $"with a neighbour on the {face.Code} face of its cell at "
            + Offset(step.At, cell);
        int earlier = findings.Count;
        if (step.Neighbour(next, solid, what, judged: true))
          for (int i = 0; i < cells.Length; i++)
            Compare(world, step, cells[i], before[i], what, findings);
        if (findings.Count > earlier)
          return;
      }
  }

  private static void Compare(
    TestWorld world,
    BlockLaws.EntityCase step,
    BlockPos cell,
    (string Code, TreeAttribute? Tree) before,
    string what,
    List<string> findings
  ) {
    string where = cell.Equals(step.At)
      ? "its cell"
      : $"its cell at {Offset(step.At, cell)}";
    (string code, TreeAttribute? tree) = State(world, step, cell);
    if (code != before.Code) {
      findings.Add(
        $"{step.Block.Code} {what} left {where} holding {code}, not {before.Code}"
      );
      return;
    }
    if (before.Tree == null)
      return;
    if (tree == null) {
      findings.Add(
        $"{step.Block.Code} {what} left {where} without its block entity"
      );
      return;
    }
    string[] keys =
    [
      .. ExTree
        .Differences(before.Tree, tree, tree)
        .Concat(ExTree.Differences(tree, before.Tree, new TreeAttribute()))
        .Select(line => line[..line.IndexOf(": ")])
        .Distinct(),
    ];
    if (keys.Length > 0)
      findings.Add(
        $"{step.Block.Code} {what} changed the tree of {where} at "
          + string.Join(", ", keys)
      );
  }

  /// <summary>The code at <paramref name="cell"/> and the tree its entity writes; a null tree for
  /// a cell without an entity or whose write throws or logs.</summary>
  private static (string Code, TreeAttribute? Tree) State(
    TestWorld world,
    BlockLaws.EntityCase step,
    BlockPos cell
  ) =>
    (
      world.GetBlock(cell).Code?.ToString() ?? "",
      world.GetBlockEntity(cell) is { } entity
        ? step.Tree(entity, "neighbour", judged: false)
        : null
    );

  private static string Offset(BlockPos from, BlockPos to) =>
    $"({to.X - from.X}, {to.Y - from.Y}, {to.Z - from.Z})";
}
