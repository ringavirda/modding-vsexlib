using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>
/// One behaviour hosted by a footprint filler cell: a behaviour code plus an optional connector face and
/// a <see cref="Properties"/> config blob. Serialized as <c>{ code[, face][, properties] }</c> in a cell's
/// <c>behaviors</c> array.
/// </summary>
public readonly record struct FillerBehaviorSpec(
  string Code,
  string? Face = null,
  object? Properties = null
) {
  /// <summary>The same spec named by type, resolving <typeparamref name="T"/>'s registered key at compile time.</summary>
  public static FillerBehaviorSpec Of<T>(
    string? face = null,
    object? properties = null
  )
    where T : Vintagestory.API.Common.BlockEntityBehavior =>
    new(
      // The type's own assembly supplies the domain via [assembly: ExDomain].
      ExpandedLib.Registries.EntityRegistry.KeyFor(string.Empty, typeof(T)),
      face,
      properties
    );
}

/// <summary>
/// One north-orientation footprint cell for a mega-block, authored in C#: offset from the principal, attach
/// flag, and any hosted <see cref="FillerBehaviorSpec"/> behaviours. May also carry a passive
/// <see cref="PortFace"/>/<see cref="PortNetworkType"/> port. <see cref="CollisionBoxes"/> null is the full
/// cube and empty is no collision; <see cref="SelectionBoxes"/> null selects from the collision boxes and
/// door leaves, empty leaves the cell unselectable; <see cref="Controls"/> and <see cref="Doors"/> are
/// written as the plan's <c>controls</c> and <c>doors</c> rows. All boxes are cell units 0..1, north frame.
/// </summary>
public readonly record struct FillerCellSpec(
  int X,
  int Y,
  int Z,
  bool AllowAttach = false,
  IReadOnlyList<FillerBehaviorSpec>? Behaviors = null,
  IReadOnlyList<Cuboidf>? CollisionBoxes = null,
  string? PortFace = null,
  string? PortNetworkType = null,
  IReadOnlyList<Cuboidf>? SelectionBoxes = null,
  IReadOnlyList<ControlBox>? Controls = null,
  IReadOnlyList<DoorLeaf>? Doors = null
);

/// <summary>
/// The half-cell volumes a partially-filled footprint cell can take, named by the face the solid half sits
/// against: <c>Down</c> is a floor slab, <c>North</c> a slab against the north face.
/// </summary>
internal static class FillerSlab {
  /// <summary>The half of the cell against <paramref name="face"/>, as a single north-orientation cuboid.
  /// <see cref="StructureFillers.FootprintCells"/> rotates it into the placed orientation.</summary>
  public static Cuboidf Half(BlockFacing face) =>
    face.Index switch {
      BlockFacing.indexNORTH => new Cuboidf(0f, 0f, 0f, 1f, 1f, 0.5f),
      BlockFacing.indexSOUTH => new Cuboidf(0f, 0f, 0.5f, 1f, 1f, 1f),
      BlockFacing.indexEAST => new Cuboidf(0.5f, 0f, 0f, 1f, 1f, 1f),
      BlockFacing.indexWEST => new Cuboidf(0f, 0f, 0f, 0.5f, 1f, 1f),
      BlockFacing.indexUP => new Cuboidf(0f, 0.5f, 0f, 1f, 1f, 1f),
      _ => new Cuboidf(0f, 0f, 0f, 1f, 0.5f, 1f),
    };
}

/// <summary>
/// Computes mega-block footprints (the <c>fillerOffsets</c> tables) from a compact description. Generated
/// cells are validated by <see cref="Validate"/> at build time.
/// </summary>
public static class StructureFootprint {
  /// <summary>
  /// A rectangular floor footprint: <paramref name="depth"/> rows along +Z and <c>2*halfWidth + 1</c>
  /// columns along X, emitted centre-out per row. The principal origin is skipped; every flanking column
  /// opts into attachment.
  /// </summary>
  public static IReadOnlyList<FillerCellSpec> Rectangle(
    int halfWidth,
    int depth
  ) {
    if (halfWidth < 0)
      throw new ArgumentOutOfRangeException(nameof(halfWidth));
    if (depth < 1)
      throw new ArgumentOutOfRangeException(nameof(depth));

    var cells = new List<FillerCellSpec>();
    for (int z = 0; z < depth; z++)
      foreach (int x in ColumnsCentreOut(halfWidth)) {
        if (x == 0 && z == 0)
          continue; // the principal sits at the origin
        cells.Add(new FillerCellSpec(x, 0, z, AllowAttach: x != 0));
      }

    Validate(cells);
    return cells;
  }

  /// <summary>Builds a footprint from ASCII grids drawn as <c>Layer</c> (per Y), <c>Slice</c> (per X)
  /// or <c>Face</c> (per Z), each glyph registered on the builder as <c>Solid</c>, <c>Attach</c>,
  /// <c>Host</c>, <c>Slab</c> or <c>Port</c>. The principal origin is skipped if drawn.</summary>
  public static IReadOnlyList<FillerCellSpec> Layout(
    Action<FillerLayoutBuilder> configure
  ) {
    var builder = new FillerLayoutBuilder();
    configure(builder);
    return builder.Build();
  }

  /// <summary>
  /// The cells with the box members of <paramref name="table"/> set by offset: collision, selection,
  /// controls and doors are replaced by the table's, so a null collision is the full cube. Cells the table
  /// does not name are unchanged.
  /// </summary>
  /// <param name="cells">The result of <see cref="Layout"/>.</param>
  /// <param name="table">Boxes keyed by the north-frame offset (X, Y, Z) of a cell.</param>
  /// <exception cref="ArgumentException">An offset of <paramref name="table"/> that no cell has.</exception>
  public static IReadOnlyList<FillerCellSpec> WithBoxes(
    this IReadOnlyList<FillerCellSpec> cells,
    IReadOnlyDictionary<(int X, int Y, int Z), CellBoxes> table
  ) {
    var known = cells.Select(c => (c.X, c.Y, c.Z)).ToHashSet();
    foreach ((int X, int Y, int Z) at in table.Keys)
      if (!known.Contains(at))
        throw new ArgumentException(
          $"Boxes name the offset ({at.X},{at.Y},{at.Z}), which no footprint cell has."
        );

    var result = new List<FillerCellSpec>(cells.Count);
    foreach (FillerCellSpec cell in cells)
      result.Add(
        table.TryGetValue((cell.X, cell.Y, cell.Z), out CellBoxes? boxes)
          ? cell with {
            CollisionBoxes = boxes.Collision,
            SelectionBoxes = boxes.Selection,
            Controls = boxes.Controls,
            Doors = boxes.Doors,
          }
          : cell
      );
    return result;
  }

  /// <summary>Column offsets from the centre out: <c>0, +1, -1, +2, -2, ...</c> up to <paramref name="halfWidth"/>.</summary>
  private static IEnumerable<int> ColumnsCentreOut(int halfWidth) {
    yield return 0;
    for (int k = 1; k <= halfWidth; k++) {
      yield return k;
      yield return -k;
    }
  }

  /// <summary>
  /// Validates a footprint: no cell may sit at the principal origin, and no two cells may share a
  /// position, and no control or door may have an empty id. Throws <see cref="ArgumentException"/> on a
  /// violation.
  /// </summary>
  public static void Validate(IReadOnlyList<FillerCellSpec> cells) {
    var seen = new HashSet<(int, int, int)>();
    foreach (FillerCellSpec cell in cells) {
      if (cell.X == 0 && cell.Y == 0 && cell.Z == 0)
        throw new ArgumentException(
          "Filler footprint contains the principal origin (0,0,0); the principal already occupies it."
        );
      if (cell.Controls?.Any(c => string.IsNullOrEmpty(c.Id)) == true)
        throw new ArgumentException(
          $"Filler cell ({cell.X},{cell.Y},{cell.Z}) has a control with an empty id."
        );
      if (cell.Doors?.Any(d => string.IsNullOrEmpty(d.Control)) == true)
        throw new ArgumentException(
          $"Filler cell ({cell.X},{cell.Y},{cell.Z}) has a door with an empty control id."
        );
      if (!seen.Add((cell.X, cell.Y, cell.Z)))
        throw new ArgumentException(
          $"Filler footprint has a duplicate cell at ({cell.X},{cell.Y},{cell.Z})."
        );
    }
  }
}
