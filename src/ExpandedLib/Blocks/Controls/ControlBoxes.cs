using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks;

/// <summary>Composes a cell's collision and selection boxes from its <see cref="CellBoxes"/> and the
/// states of its controls, and finds a filler's row on its principal's definition.</summary>
public static class ControlBoxes {
  private static readonly Cuboidf[] FullCube =
  [
    new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f),
  ];

  /// <summary>
  /// The collision and selection boxes of one cell for the given control states. Selection is the door
  /// leaves, control boxes, then body boxes; a Hidden control drops its own, and an emptied selection
  /// falls back to the collision body. On 1.22 leaves and controls carry their id.
  /// </summary>
  /// <param name="turned">The cell's boxes already turned into the placed orientation. When the cell has a
  /// <see cref="CellBoxes.Selection"/>, each <see cref="ControlBox.Box"/> must be the same instance as the
  /// entry it names: Compose drops that entry by reference and substitutes the id-carrying copy.</param>
  /// <param name="state">The state of a control id; ids nothing owns are <see cref="ControlState.Shown"/>.</param>
  public static (Cuboidf[] Collision, Cuboidf[] Selection) Compose(
    CellBoxes turned,
    System.Func<string, ControlState> state
  ) {
    Cuboidf[] body = turned.Collision ?? FullCube;
    var collision = new List<Cuboidf>();
    var selection = new List<Cuboidf>();
    bool hidden = false;

    foreach (DoorLeaf door in turned.Doors ?? []) {
      ControlState s = state(door.Control);
      if (s == ControlState.Hidden) {
        hidden = true;
        continue;
      }
      bool open = s == ControlState.Open;
      Cuboidf[] leaves = open ? door.Open : door.Shut;
      foreach (Cuboidf leaf in leaves)
        selection.Add(WithId(leaf, door.Control));
      if (!open || door.OpenCollides)
        collision.AddRange(leaves);
    }
    collision.AddRange(body);

    foreach (ControlBox control in turned.Controls ?? []) {
      if (state(control.Id) == ControlState.Hidden)
        hidden = true;
      else
        selection.Add(WithId(control.Box, control.Id));
    }

    if (turned.Selection == null)
      selection.AddRange(body);
    else
      foreach (Cuboidf box in turned.Selection)
        if (!IsControlBox(turned.Controls, box))
          selection.Add(box);

    if (selection.Count == 0 && hidden)
      selection.AddRange(body);
    return ([.. collision], [.. selection]);
  }

  /// <summary>
  /// The state of <paramref name="controlId"/> on a principal: the answers of
  /// <paramref name="principal"/> and of each of its behaviours that implement
  /// <see cref="IControlStates"/>, combined as Hidden if any says Hidden, else Open if any says Open.
  /// </summary>
  /// <returns><see cref="ControlState.Shown"/> when <paramref name="principal"/> is null or nothing owns the id.</returns>
  public static ControlState StateOf(BlockEntity? principal, string controlId) {
    if (principal == null)
      return ControlState.Shown;
    ControlState result = ControlState.Shown;
    if (principal is IControlStates self)
      result = Combine(result, self.StateOf(controlId));
    foreach (BlockEntityBehavior behavior in principal.Behaviors)
      if (behavior is IControlStates answers)
        result = Combine(result, answers.StateOf(controlId));
    return result;
  }

  private static ControlState Combine(ControlState so, ControlState? answer) =>
    answer switch {
      ControlState.Hidden => ControlState.Hidden,
      ControlState.Open when so != ControlState.Hidden => ControlState.Open,
      _ => so,
    };

  private static bool IsControlBox(
    IReadOnlyList<ControlBox>? controls,
    Cuboidf box
  ) {
    foreach (ControlBox control in controls ?? [])
      if (ReferenceEquals(control.Box, box))
        return true;
    return false;
  }

  private static Cuboidf WithId(Cuboidf box, string id) {
#if GAME_GE_1_22
    if (box is CuboidfWithId { Id: { } have } && have == id)
      return box;
    return new CuboidfWithId(box) { Id = id };
#else
    return box;
#endif
  }

  #region Filler lookup

  private sealed class RowTable(Dictionary<(int, int, int), CellBoxes> north) {
    public readonly Dictionary<(int, int, int), CellBoxes> North = north;
    public readonly ConcurrentDictionary<
      (int, int, int, int),
      PlacedCell
    > Turned = new();
  }

  internal sealed class PlacedCell(CellBoxes boxes) {
    public readonly CellBoxes Boxes = boxes;
    private readonly (Cuboidf[] Collision, Cuboidf[] Selection)? _static =
      boxes.Controls is { Count: > 0 } || boxes.Doors is { Count: > 0 }
        ? null
        : Compose(boxes, _ => ControlState.Shown);

    public (Cuboidf[] Collision, Cuboidf[] Selection) At(
      IBlockAccessor accessor,
      BlockPos principalPos
    ) {
      if (_static is { } cached)
        return cached;
      BlockEntity? principalBe = accessor.GetBlockEntity(principalPos);
      return Compose(Boxes, id => StateOf(principalBe, id));
    }
  }

  private static readonly ConditionalWeakTable<Block, RowTable> Tables = new();

  private static RowTable TableOf(Block principal, IFillerHost host) =>
    Tables.GetValue(
      principal,
      _ => {
        var north = new Dictionary<(int, int, int), CellBoxes>();
        foreach (
          FillerOffset off in StructureFillers.ReadOffsets(host.FillerOffsets)
        )
          if (off.Boxes != null)
            north[(off.Offset.X, off.Offset.Y, off.Offset.Z)] = off.Boxes;
        return new RowTable(north);
      }
    );

  internal static Cuboidf[]? EntityCollision(
    IBlockAccessor accessor,
    BlockPos principalPos,
    Vec3i northOffset,
    int angle
  ) =>
    accessor.GetBlockEntity(principalPos) is ICellCollision answers
      ? answers.CollisionAt(northOffset, angle)
      : null;

  /// <summary>
  /// The boxes a filler at <paramref name="pos"/> answers with, composed from its principal's
  /// <c>fillerOffsets</c> row at the north offset <c>RotateOffset(pos - principal, (360 - angle) % 360)</c>,
  /// <c>angle</c> the host's <see cref="IFillerHost.StructureAngle"/>. An <see cref="ICellCollision"/> entity
  /// on the principal replaces the composed collision when it answers non-null.
  /// </summary>
  /// <returns>False when the filler has no principal, the principal is gone or is not an
  /// <see cref="IFillerHost"/>, or its definition has no row with box keys at that offset.</returns>
  internal static bool TryFiller(
    IBlockAccessor accessor,
    BlockPos pos,
    BlockEntityStructureFiller? filler,
    out (Cuboidf[] Collision, Cuboidf[] Selection) boxes
  ) {
    boxes = default;
    if (filler?.Principal is not { } principalPos)
      return false;
    Block? principal = accessor.GetBlock(principalPos);
    if (
      principal == null
      || principal.Id == 0
      || principal is not IFillerHost host
    )
      return false;

    int angle = ((host.StructureAngle % 360) + 360) % 360;
    Vec3i north = ExOrientation.RotateOffset(
      pos.X - principalPos.X,
      pos.Y - principalPos.Y,
      pos.Z - principalPos.Z,
      (360 - angle) % 360
    );
    RowTable table = TableOf(principal, host);
    if (
      !table.North.TryGetValue((north.X, north.Y, north.Z), out CellBoxes? row)
    )
      return false;

    PlacedCell placed = table.Turned.GetOrAdd(
      (north.X, north.Y, north.Z, angle),
      static (_, a) => new PlacedCell(a.row.Turned(a.angle)),
      (row, angle)
    );
    (Cuboidf[] collision, Cuboidf[] selection) = placed.At(
      accessor,
      principalPos
    );
    boxes = (
      EntityCollision(accessor, principalPos, north, angle) ?? collision,
      selection
    );
    return true;
  }

  #endregion

  #region Principal cell

  private sealed class PrincipalTable(CellBoxes? row) {
    public readonly CellBoxes? Row = row;
    public readonly ConcurrentDictionary<int, PlacedCell> Turned = new();
  }

  private static readonly ConditionalWeakTable<
    Block,
    PrincipalTable
  > Principals = new();

  private static PrincipalTable PrincipalOf(Block block) =>
    Principals.GetValue(block, b => new PrincipalTable(PrincipalRow(b)));

  /// <summary>The block's own cell from <c>attributes.principalCell</c>, or null without one.</summary>
  internal static CellBoxes? PrincipalRow(Block block) {
    JsonObject? node = block.Attributes?["principalCell"];
    return node != null && node.Exists ? CellBoxes.Read(node) : null;
  }

  private static PlacedCell? PlacedFor(Block block, out int angle) {
    angle =
      (
        (
          block is IFillerHost host
            ? host.StructureAngle
            : ExOrientation.AngleFromSide(
              block.Variant?["side"] ?? block.Variant?["orientation"]
            )
        ) % 360
        + 360
      ) % 360;
    PrincipalTable table = PrincipalOf(block);
    if (table.Row == null)
      return null;
    return table.Turned.GetOrAdd(
      angle,
      static (_, a) => new PlacedCell(a.Row.Turned(a.angle)),
      (table.Row, angle)
    );
  }

  /// <summary>
  /// The collision boxes of <paramref name="block"/>'s own cell at <paramref name="pos"/>, from its
  /// <c>principalCell</c> turned by <see cref="IFillerHost.StructureAngle"/> (else its <c>side</c> or
  /// <c>orientation</c>) and the control states there. An <see cref="ICellCollision"/> entity at
  /// <paramref name="pos"/> that answers non-null replaces those boxes. For a principal that is no mega-block or node.
  /// </summary>
  /// <returns>Null when the block has no <c>principalCell</c>; the caller then answers as its base does.</returns>
  public static Cuboidf[]? Collision(
    Block block,
    IBlockAccessor accessor,
    BlockPos pos
  ) {
    PlacedCell? cell = PlacedFor(block, out int angle);
    if (cell == null)
      return null;
    return EntityCollision(accessor, pos, new Vec3i(0, 0, 0), angle)
      ?? cell.At(accessor, pos).Collision;
  }

#if GAME_GE_1_22
  /// <summary>The selection boxes of <paramref name="block"/>'s own cell, as <see cref="Collision"/>
  /// composes the collision. Available on 1.22 only.</summary>
  /// <returns>Null when the block has no <c>principalCell</c>.</returns>
  public static Cuboidf[]? Selection(
    Block block,
    IBlockAccessor accessor,
    BlockPos pos
  ) => PlacedFor(block, out _)?.At(accessor, pos).Selection;
#endif

  #endregion
}
