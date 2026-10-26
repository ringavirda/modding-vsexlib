using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
    Block[] registered = Registered(world);
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

        List<BlockPos> unmade = [];
        foreach (
          BlockEntityMultiblockStructure.MissingCell cell in Fill(
            world,
            anchor,
            registered
          ).Unmade
        ) {
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

  /// <summary>The blocks of <paramref name="world"/> outside <c>game</c>, which a layout cell is
  /// filled from.</summary>
  internal static Block[] Registered(TestWorld world) =>
    [
      .. world.World.Blocks.Where(b =>
        b?.Code != null && b.Id != 0 && b.Code.Domain != "game"
      ),
    ];

  /// <summary>The variants of <paramref name="type"/> with a layout, the first of each combination
  /// of their variant groups other than <c>side</c> and <c>orientation</c>: each structure in one
  /// facing.</summary>
  internal static IEnumerable<Block> OneFacing(
    TestWorld world,
    IEnumerable<Block> type
  ) => FirstFacing(type.Where(b => BlockLaws.Signals(world, b).Layout != null));

  /// <summary>The first of the variants in <paramref name="variants"/> for each combination of
  /// their variant groups other than <c>side</c> and <c>orientation</c>.</summary>
  internal static IEnumerable<Block> FirstFacing(IEnumerable<Block> variants) =>
    variants.DistinctBy(b =>
      string.Join(
        ",",
        (
          b.Variant?.Where(v => v.Key is not ("side" or "orientation")) ?? []
        ).Select(v => $"{v.Key}={v.Value}")
      )
    );

  /// <summary>Stands the structure <paramref name="block"/> anchors up formed at
  /// <paramref name="at"/>: the anchor placed as <see cref="BlockLaws.EntityCase.Place"/> places a
  /// block, each cell it reports missing filled as <see cref="Run"/> fills it and then placed the
  /// same way unless an earlier placement has replaced it (a megablock's fillers), with its entity, the <c>game</c> cells raised by <see cref="StructureRig"/> at the
  /// angle the anchor turns its layout to, and one monitor tick.</summary>
  /// <param name="registered">The blocks cells are filled from (<see cref="Registered"/>).</param>
  /// <param name="faults">Where a placement's throw or log goes, keyed by the placed block's code;
  /// null leaves it unjudged.</param>
  /// <returns>The anchor's case; null when a placement threw or logged, a cell has no registered
  /// block to satisfy it, or the monitor does not see the structure complete, each of which
  /// <see cref="Run"/> judges.</returns>
  internal static BlockLaws.EntityCase? Formed(
    TestWorld world,
    Block block,
    BlockPos at,
    Block[] registered,
    List<string>? faults
  ) {
    var step = new BlockLaws.EntityCase(world, block, at, faults);
    if (
      !step.Place(judged: faults != null)
      || step.Entity is not BlockEntityMultiblockStructure anchor
    )
      return null;
    (List<BlockEntityMultiblockStructure.MissingCell> filled, var unmade) =
      Fill(world, anchor, registered);
    if (unmade.Count > 0)
      return null;
    var standing = filled
      .Select(c => (world.GetBlock(c.At), world.GetBlockEntity(c.At)))
      .ToList();
    for (int i = 0; i < filled.Count; i++) {
      BlockEntityMultiblockStructure.MissingCell cell = filled[i];
      if (
        world.GetBlock(cell.At) != standing[i].Item1
        || world.GetBlockEntity(cell.At) != standing[i].Item2
      )
        continue;
      if (
        !new BlockLaws.EntityCase(
          world,
          world.GetBlock(cell.At),
          cell.At,
          faults
        ).Place(judged: faults != null)
      )
        return null;
    }
    StructureRig.Around(world, anchor, anchor.LayoutAngle).Raise();
    anchor.DriveMonitorTick();
    return anchor.StructureComplete ? step : null;
  }

  /// <summary>The cell of the structure <paramref name="step"/> anchors, then each of its layout
  /// cells holding a block outside <c>game</c>: the cells of a formed structure a player meets,
  /// less the rig's stand-ins, which carry no behaviour.</summary>
  internal static BlockPos[] FormedCells(
    TestWorld world,
    BlockLaws.EntityCase step
  ) =>
    step.Entity is BlockEntityMultiblockStructure anchor
      ?
      [
        step.At.Copy(),
        .. anchor
          .LayoutCells.Select(c => c.At)
          .Where(c =>
            !c.Equals(step.At)
            && world.GetBlock(c) is { Id: not 0, Code.Domain: not "game" }
          ),
      ]
      : [step.At.Copy()];

  private static readonly ConditionalWeakTable<
    Block[],
    ConcurrentDictionary<string, Block[]>
  > Matches = new();

  /// <summary>The blocks of <paramref name="registered"/> a layout cell wanting
  /// <paramref name="wanted"/> accepts, in registration order; looked up once for each distinct
  /// wanted code and array.</summary>
  private static Block[] MatchesOf(Block[] registered, AssetLocation wanted) =>
    Matches
      .GetValue(registered, _ => new())
      .GetOrAdd(
        wanted.ToString(),
        _ => [.. registered.Where(b => WildcardUtil.Match(wanted, b.Code))]
      );

  /// <summary>Fills each empty cell outside <c>game</c> that <paramref name="anchor"/> wants with
  /// the first registered block its wanted code matches, all at once and checked by one scan of
  /// the layout, so the work does not grow with the codes a glyph accepts; a cell the anchor
  /// still lacks is tried against the other matching blocks one by one and cleared when none
  /// satisfies it.</summary>
  /// <returns>The cells filled, and the cells no registered block satisfies.</returns>
  internal static (
    List<BlockEntityMultiblockStructure.MissingCell> Filled,
    List<BlockEntityMultiblockStructure.MissingCell> Unmade
  ) Fill(
    TestWorld world,
    BlockEntityMultiblockStructure anchor,
    Block[] registered
  ) {
    var empty = new List<BlockEntityMultiblockStructure.MissingCell>();
    anchor.IncompleteBlockCount(c => {
      if (c.Wanted.Domain != "game" && world.GetBlock(c.At).Id == 0)
        empty.Add(c);
    });
    foreach (BlockEntityMultiblockStructure.MissingCell cell in empty)
      if (MatchesOf(registered, cell.Wanted) is { Length: > 0 } matches)
        world.Place(cell.At, matches[0]);

    var lacking = new HashSet<BlockPos>();
    anchor.IncompleteBlockCount(c => lacking.Add(c.At));
    var filled = new List<BlockEntityMultiblockStructure.MissingCell>();
    var unmade = new List<BlockEntityMultiblockStructure.MissingCell>();
    foreach (BlockEntityMultiblockStructure.MissingCell cell in empty)
      if (
        !lacking.Contains(cell.At) || FillOther(world, anchor, cell, registered)
      )
        filled.Add(cell);
      else
        unmade.Add(cell);
    return (filled, unmade);
  }

  private static bool FillOther(
    TestWorld world,
    BlockEntityMultiblockStructure anchor,
    BlockEntityMultiblockStructure.MissingCell cell,
    Block[] registered
  ) {
    foreach (Block candidate in MatchesOf(registered, cell.Wanted).Skip(1)) {
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
