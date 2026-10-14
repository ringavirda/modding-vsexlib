using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
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
  /// The collision and selection boxes of one cell for the given control states.
  /// Collision is the shut leaves (and open ones whose door says <c>OpenCollides</c>), then the body;
  /// a null <see cref="CellBoxes.Collision"/> is the full cube. Selection is the door leaves of the
  /// current state, then the control boxes, then the body boxes: the selection entries that are not
  /// control boxes, or the collision body when the row has no selection. On 1.22 leaves and control
  /// boxes are <c>CuboidfWithId</c> carrying their control id. A Hidden control drops its boxes and
  /// leaves; when that leaves the selection empty the cell selects as its collision body.
  /// </summary>
  /// <param name="turned">The cell's boxes already turned into the placed orientation.</param>
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
    public readonly ConcurrentDictionary<(int, int, int, int), Placed> Turned =
      new();
  }

  private sealed class Placed(CellBoxes boxes) {
    public readonly CellBoxes Boxes = boxes;
    public readonly (Cuboidf[] Collision, Cuboidf[] Selection)? Static =
      boxes.Controls is { Count: > 0 } || boxes.Doors is { Count: > 0 }
        ? null
        : Compose(boxes, _ => ControlState.Shown);
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

  /// <summary>
  /// The boxes a filler at <paramref name="pos"/> answers with, composed from its row on the
  /// principal block's <c>fillerOffsets</c> at the north offset
  /// <c>RotateOffset(pos - principal, (360 - angle) % 360)</c>, <c>angle</c> being the host's
  /// <see cref="IFillerHost.StructureAngle"/>.
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

    Placed placed = table.Turned.GetOrAdd(
      (north.X, north.Y, north.Z, angle),
      _ => new Placed(row.Turned(angle))
    );
    if (placed.Static is { } cached) {
      boxes = cached;
      return true;
    }
    BlockEntity? principalBe = accessor.GetBlockEntity(principalPos);
    boxes = Compose(placed.Boxes, id => StateOf(principalBe, id));
    return true;
  }

  #endregion
}
