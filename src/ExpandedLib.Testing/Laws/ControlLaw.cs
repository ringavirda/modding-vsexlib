#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>A block's controls are declared whole: every id a cell or door names is in its
/// <c>controls</c> table, every table row has a box, no control box lies over another control's or
/// the body's, and the placed block selects each control box with its id.</summary>
public static class ControlLaw
{
  internal const string Name = "control";

  private const float Overlap = 1e-4f;

  private static readonly Cuboidf FullCube = new(0f, 0f, 0f, 1f, 1f, 1f);

  private static readonly int[] Angles = [0, 90, 180, 270];

  /// <summary>Reads the <c>controls</c> table, <c>principalCell</c> and <c>fillerOffsets</c> boxes of
  /// every block of <paramref name="domain"/> that declares a control, a principal cell or a cell row
  /// with selection, control or door keys, then places each of its variants and reads the selection
  /// boxes of every cell that holds a control.</summary>
  /// <remarks>A finding, at most one line per check and variant: a control or door id a cell names that the
  /// table lacks; a door on a control whose kind is not <c>door</c>; a table id no control box and no
  /// door carries; an alsoId that is itself a table id; in one cell, a control box overlapping another
  /// control's box or a body box (the cell's selection boxes that are no control's, or its collision
  /// boxes when it declares no selection) by more than 1e-4 on every axis; top-level collision or
  /// selection boxes beside <c>principalCell</c> (which cannot be told from none when they equal the
  /// game's default cube); a placed cell whose
  /// selection lacks a control's id. A control whose table <c>stage</c> is above 0 is hidden at
  /// placement and is left out of the last check. A variant that fails to place is left to the placement
  /// law. The variants of a block are its facings.</remarks>
  /// <param name="world">A world holding every variant of the blocks judged and exlib's structure
  /// filler (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are judged.</param>
  /// <returns>The law's blocktypes, variants judged and findings, each keyed by the variant's
  /// code.</returns>
  public static BlockLaws.Law Run(TestWorld world, string domain)
  {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    )
    {
      Block[] declaring =
      [
        .. type.Where(b => BlockLaws.Signals(world, b).Controls),
      ];
      if (declaring.Length == 0)
        continue;
      blocks++;
      foreach (Block block in declaring)
      {
        cases++;
        string code = block.Code.ToString();
        IReadOnlyList<(string Label, CellBoxes Boxes)> cells = CellsOf(
          world,
          block
        );
        findings.AddRange(Declared(code, block, cells));
        findings.AddRange(Placed(world, block, cells, sites.Next()));
      }
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  private static IReadOnlyList<(string Label, CellBoxes Boxes)> CellsOf(
    TestWorld world,
    Block block
  )
  {
    var cells = new List<(string, CellBoxes)>();
    if (ControlBoxes.PrincipalRow(block) is { } principal)
      cells.Add(("principal", principal));
    if (block is IFillerHost host)
      foreach (
        FillerOffset cell in StructureFillers.ReadOffsets(host.FillerOffsets)
      )
        if (cell.Boxes != null)
          cells.Add(
            ($"({cell.Offset.X}, {cell.Offset.Y}, {cell.Offset.Z})", cell.Boxes)
          );
    return cells;
  }

  private static IEnumerable<string> Declared(
    string code,
    Block block,
    IReadOnlyList<(string Label, CellBoxes Boxes)> cells
  )
  {
    IReadOnlyList<BlockControl> table = BlockControls.Of(block);
    var ids = table.Select(c => c.Id).ToHashSet();

    string[] named =
    [
      .. cells
        .SelectMany(c =>
          (c.Boxes.Controls ?? [])
            .Select(x => x.Id)
            .Concat((c.Boxes.Doors ?? []).Select(d => d.Control))
        )
        .Where(id => !ids.Contains(id))
        .Distinct(),
    ];
    if (named.Length > 0)
      yield return $"{code} names controls its table does not list: {string.Join(", ", named)}";

    string[] notDoors =
    [
      .. cells
        .SelectMany(c => c.Boxes.Doors ?? [])
        .Select(d => d.Control)
        .Distinct()
        .Where(id => BlockControls.Find(block, id) is { Kind: not "door" })
        .Select(id => $"{id} ({BlockControls.Find(block, id)!.Kind})"),
    ];
    if (notDoors.Length > 0)
      yield return $"{code} has a door on a control whose kind is not door: {string.Join(", ", notDoors)}";

    string[] unboxed =
    [
      .. table
        .Select(c => c.Id)
        .Where(id =>
          !cells.Any(c =>
            (c.Boxes.Controls ?? []).Any(x => x.Id == id)
            || (c.Boxes.Doors ?? []).Any(d => d.Control == id)
          )
        ),
    ];
    if (unboxed.Length > 0)
      yield return $"{code} lists controls with no box: {string.Join(", ", unboxed)}";

    string[] aliased =
    [
      .. table.SelectMany(c =>
        c.AlsoIds.Where(ids.Contains).Select(also => $"{c.Id} ({also})")
      ),
    ];
    if (aliased.Length > 0)
      yield return $"{code} has alsoIds that are controls themselves: {string.Join(", ", aliased)}";

    string[] overlapping =
    [
      .. cells.Where(c => Overlapping(c.Boxes)).Select(c => c.Label),
    ];
    if (overlapping.Length > 0)
      yield return $"{code} has a control box over another control or the body in cells: {string.Join(", ", overlapping)}";

    if (
      ControlBoxes.PrincipalRow(block) != null
      && (!IsDefault(block.CollisionBoxes) || !IsDefault(block.SelectionBoxes))
    )
      yield return $"{code} declares collisionboxes or selectionboxes beside principalCell";
  }

  private static bool IsDefault(Cuboidf[]? boxes) =>
    boxes is { Length: 1 } && !Differs(boxes[0], FullCube);

  private static bool Differs(Cuboidf a, Cuboidf b) =>
    Math.Abs(a.X1 - b.X1) > Overlap
    || Math.Abs(a.Y1 - b.Y1) > Overlap
    || Math.Abs(a.Z1 - b.Z1) > Overlap
    || Math.Abs(a.X2 - b.X2) > Overlap
    || Math.Abs(a.Y2 - b.Y2) > Overlap
    || Math.Abs(a.Z2 - b.Z2) > Overlap;

  private static bool Overlapping(CellBoxes cell)
  {
    Cuboidf[] controls = [.. (cell.Controls ?? []).Select(c => c.Box)];
    Cuboidf[] body = cell.Selection is { } selection
      ? [.. selection.Where(b => !controls.Any(c => ReferenceEquals(c, b)))]
      : cell.Collision ?? [FullCube];
    for (int i = 0; i < controls.Length; i++)
    {
      for (int j = i + 1; j < controls.Length; j++)
        if (Intersect(controls[i], controls[j]))
          return true;
      if (body.Any(b => Intersect(controls[i], b)))
        return true;
    }
    return false;
  }

  private static bool Intersect(Cuboidf a, Cuboidf b) =>
    Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1) > Overlap
    && Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1) > Overlap
    && Math.Min(a.Z2, b.Z2) - Math.Max(a.Z1, b.Z1) > Overlap;

  private static IEnumerable<string> Placed(
    TestWorld world,
    Block block,
    IReadOnlyList<(string Label, CellBoxes Boxes)> cells,
    BlockPos at
  )
  {
    if (!cells.Any(c => c.Boxes.Controls is { Count: > 0 }))
      yield break;
    try
    {
      world.Accessor.SetBlock(block.BlockId, at);
      block.OnBlockPlaced(world.World, at, new ItemStack(block));
    }
    catch (Exception)
    {
      yield break;
    }

    var positions = new Dictionary<string, BlockPos> { ["principal"] = at };
    if (block is IFillerHost host)
    {
      BlockPos[] standing = BlockLaws.FillersOf(world, at);
      int[] angles = block is BlockFilledMegastructure mega
        ? [mega.StructureAngle]
        : Angles;
      foreach (int angle in angles)
      {
        List<FillerCell> footprint = StructureFillers.FootprintCells(
          host,
          at,
          angle
        );
        if (!footprint.Select(f => f.Pos).ToHashSet().SetEquals(standing))
          continue;
        List<FillerOffset> offsets = StructureFillers.ReadOffsets(
          host.FillerOffsets
        );
        for (int i = 0; i < offsets.Count; i++)
        {
          Vec3i o = offsets[i].Offset;
          positions[$"({o.X}, {o.Y}, {o.Z})"] = footprint[i].Pos;
        }
        break;
      }
    }

    var lacking = new List<string>();
    foreach ((string label, CellBoxes boxes) in cells)
    {
      if (!positions.TryGetValue(label, out BlockPos? pos))
        continue;
      Cuboidf[] selection =
        world.GetBlock(pos).GetSelectionBoxes(world.Accessor, pos) ?? [];
      foreach (ControlBox control in boxes.Controls ?? [])
        if (
          BlockControls.Find(block, control.Id) is not { Stage: > 0 }
          && !selection.Any(b =>
            b is CuboidfWithId { Id: { } id } && id == control.Id
          )
        )
          lacking.Add($"{control.Id} in {label}");
    }

    try
    {
      world.Accessor.SetBlock(0, at);
    }
    catch (Exception)
    {
      // A removal that throws is the megablock law's finding.
    }
    if (lacking.Count > 0)
      yield return $"{block.Code} placed selects controls without their id: {string.Join(", ", lacking)}";
  }
}
#endif
