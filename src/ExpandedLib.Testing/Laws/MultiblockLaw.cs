using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>A multiblock turns its layout with its side, completes in every facing it is placed in,
/// its cells filled with the registered blocks of the mods and stand-ins for <c>game:</c> ones, and
/// reads each peripheral at the cell its completion check reads.</summary>
public static class MultiblockLaw {
  internal const string Name = "multiblock";

  /// <summary>Sets every variant of each block of <paramref name="domain"/> whose entity is a
  /// <see cref="BlockEntityMultiblockStructure"/> with a layout at a fresh cell through the
  /// accessor's <c>SetBlock</c>, runs its <see cref="Block.OnBlockPlaced"/>, fills the cells its
  /// entity reports missing and runs one monitor tick.</summary>
  /// <remarks>The rig stands at the angle the variant's <c>side</c> (else <c>orientation</c>) gives
  /// through <see cref="ExOrientation.AngleFromSide"/>, plus the offset the blocktype's first variant
  /// turns by. A non-<c>game</c> cell takes the first registered block that satisfies it, else stays
  /// empty; a <c>game</c> cell takes a stand-in. A finding is a placement that throws or raises no
  /// such entity, a layout turned elsewhere than that angle, a cell no registered block satisfies,
  /// an incomplete structure, and a peripheral (<c>GetGlobalPos</c>) read elsewhere than it
  /// completes.</remarks>
  /// <param name="world">Every variant of the blocks judged and of the blocks their layouts name
  /// (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are placed.</param>
  /// <returns>The law's blocktypes, structures stood up and findings, each keyed by the variant's
  /// code.</returns>
  public static BlockLaws.Law Run(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    Block[] registered =
    [
      .. world.World.Blocks.Where(b =>
        b?.Code != null && b.Id != 0 && b.Code.Domain != "game"
      ),
    ];
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      Block[] layouts =
      [
        .. type.Where(b => BlockLaws.Signals(world, b).Layout != null),
      ];
      if (layouts.Length == 0)
        continue;
      blocks++;
      (Block Variant, int Offset)? frame = null;
      foreach (Block block in layouts) {
        cases++;
        BlockPos at = sites.Next();
        try {
          world.Accessor.SetBlock(block.BlockId, at);
          block.OnBlockPlaced(world.World, at, new ItemStack(block));
        } catch (System.Exception e) {
          findings.Add($"{block.Code} placed threw {BlockLaws.Describe(e)}");
          continue;
        }
        if (
          world.GetBlockEntity(at) is not BlockEntityMultiblockStructure anchor
        ) {
          findings.Add($"{block.Code} placed raised no multiblock entity");
          continue;
        }

        string? side = block.Variant?["side"] ?? block.Variant?["orientation"];
        int turned = ExOrientation.AngleFromSide(side);
        frame ??= (block, Normal(anchor.LayoutAngle - turned));
        int expected = Normal(turned + frame.Value.Offset);

        var missing = new List<BlockEntityMultiblockStructure.MissingCell>();
        var unmade = new List<BlockPos>();
        anchor.IncompleteBlockCount(missing.Add);
        foreach (BlockEntityMultiblockStructure.MissingCell cell in missing) {
          if (cell.Wanted.Domain == "game" || world.GetBlock(cell.At).Id != 0)
            continue;
          if (FillReal(world, anchor, cell, registered))
            continue;
          unmade.Add(cell.At);
          findings.Add(
            $"{block.Code} wants {cell.Wanted} at {Local(anchor, cell.At)}, which no registered "
              + "block satisfies"
          );
        }

        try {
          StructureRig.Around(world, anchor, expected).Raise();
        } catch (InvalidOperationException) {
          findings.Add(
            $"{block.Code} turns its layout to {anchor.LayoutAngle}, not the {expected} its side "
              + $"'{side}' gives at the offset {frame.Value.Variant.Code} turns by"
          );
          continue;
        }
        foreach (BlockPos cell in unmade)
          world.Accessor.SetBlock(0, cell);
        anchor.DriveMonitorTick();
        if (!anchor.StructureComplete)
          findings.Add(
            $"{block.Code} does not complete at angle {anchor.LayoutAngle}: "
              + $"{anchor.IncompleteBlockCount()} cell(s) unsatisfied"
          );

        var strays = anchor
          .LayoutCells.Where(c =>
            !anchor.PeripheralCell(c.Local.X, c.Local.Y, c.Local.Z).Equals(c.At)
          )
          .ToList();
        if (strays.Count > 0) {
          var first = strays[0];
          findings.Add(
            $"{block.Code} reads {strays.Count} layout cell(s) as peripherals elsewhere than it "
              + $"completes them: {first.Local} at "
              + $"{anchor.PeripheralCell(first.Local.X, first.Local.Y, first.Local.Z)}, not {first.At}"
          );
        }
      }
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  /// <summary>Places at <paramref name="cell"/> the first block of <paramref name="registered"/>
  /// its wanted code matches and <paramref name="anchor"/> accepts there; clears the cell and
  /// returns false when none does.</summary>
  private static bool FillReal(
    TestWorld world,
    BlockEntityMultiblockStructure anchor,
    BlockEntityMultiblockStructure.MissingCell cell,
    Block[] registered
  ) {
    foreach (
      Block candidate in registered.Where(b =>
        WildcardUtil.Match(cell.Wanted, b.Code)
      )
    ) {
      world.Place(cell.At, candidate);
      bool still = false;
      anchor.IncompleteBlockCount(m => still |= m.At.Equals(cell.At));
      if (!still)
        return true;
    }
    world.Accessor.SetBlock(0, cell.At);
    return false;
  }

  private static int Normal(int angle) => ((angle % 360) + 360) % 360;

  private static (int X, int Y, int Z) Local(
    BlockEntityMultiblockStructure anchor,
    BlockPos at
  ) => anchor.LayoutCells.FirstOrDefault(c => c.At.Equals(at)).Local;
}
