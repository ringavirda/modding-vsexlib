using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks;

/// <summary>A control placed in one cell: its id and its selection box. On 1.22 the box in the
/// cell's <see cref="CellBoxes.Selection"/> that it names carries the id as vanilla's
/// <c>CuboidfWithId</c>; on 1.21 and 1.20 the id rides only here.</summary>
/// <param name="Id">The control id, non-empty; it matches a <see cref="BlockControl.Id"/>.</param>
/// <param name="Box">Cell units 0..1, north frame. The same instance as the selection entry it
/// equals, or the appended one.</param>
public sealed record ControlBox(string Id, Cuboidf Box);

/// <summary>The leaves of one door in one cell. All boxes are cell units 0..1, north frame.</summary>
/// <param name="Control">The id of the door's control, non-empty.</param>
/// <param name="Shut">The leaf boxes while the door is shut.</param>
/// <param name="Open">The leaf boxes while the door is open.</param>
/// <param name="Opening">The opening the door clears; it documents the plan and is not used at runtime.</param>
/// <param name="OpenCollides">True when the open leaves still collide; false when they only select.</param>
public sealed record DoorLeaf(
  string Control,
  Cuboidf[] Shut,
  Cuboidf[] Open,
  Cuboidf[] Opening,
  bool OpenCollides
);

/// <summary>
/// The boxes of one cell as data: collision, selection, the controls on it and its doors. Read from a
/// <c>fillerOffsets</c> row or a <c>principalCell</c> attribute, written back by
/// <see cref="WriteTo"/>, turned into the placed orientation by <see cref="Turned(int)"/>. Ids ride the
/// selection boxes only on 1.22 (<c>CuboidfWithId</c>); on 1.21 and 1.20 a hit carries no id.
/// </summary>
/// <param name="Collision">Cell units 0..1 (a principal cell may pass them), north frame. Null is the
/// full cube; an empty array is no collision.</param>
/// <param name="Selection">Null selects from the collision boxes and door leaves; an empty array leaves
/// the cell unselectable.</param>
/// <param name="Controls">Null is none.</param>
/// <param name="Doors">Null is none.</param>
public sealed record CellBoxes(
  Cuboidf[]? Collision = null,
  Cuboidf[]? Selection = null,
  IReadOnlyList<ControlBox>? Controls = null,
  IReadOnlyList<DoorLeaf>? Doors = null
) {
  private const float Tolerance = 1e-4f;

  /// <summary>
  /// Reads the box keys of a row: <c>collisionBoxes</c> (or the single <c>collisionBox</c>),
  /// <c>selectionBoxes</c>, <c>controls</c> [{id, box}] and <c>doors</c>
  /// [{control, shut, open, opening, openCollides}]. A control box equal within 1e-4 to a selection entry
  /// names that entry; one equal to none is appended after the entries, and is dropped from the
  /// selection when the row has no <c>selectionBoxes</c>.
  /// </summary>
  /// <returns>Null when the row has none of the box keys.</returns>
  public static CellBoxes? Read(JsonObject row) {
    if (
      !row["collisionBoxes"].Exists
      && !row["collisionBox"].Exists
      && !row["selectionBoxes"].Exists
      && !row["controls"].Exists
      && !row["doors"].Exists
    )
      return null;

    Cuboidf[]? collision = StructureFillers.ReadBoxes(row);
    List<Cuboidf>? selection = row["selectionBoxes"].Exists
      ? [.. ReadArray(row["selectionBoxes"])]
      : null;

    var controls = new List<ControlBox>();
    var named = new HashSet<int>();
    foreach (JsonObject node in row["controls"].AsArray() ?? []) {
      string id = node["id"].AsString() ?? "";
      Cuboidf box = node["box"].AsObject<Cuboidf>() ?? new Cuboidf();
      if (selection != null) {
        int at = -1;
        for (int i = 0; i < selection.Count && at < 0; i++)
          if (!named.Contains(i) && Near(selection[i], box))
            at = i;
        if (at < 0) {
          selection.Add(box);
          at = selection.Count - 1;
        }
        named.Add(at);
        selection[at] = WithId(selection[at], id);
        box = selection[at];
      }
      controls.Add(new ControlBox(id, box));
    }

    var doors = new List<DoorLeaf>();
    foreach (JsonObject node in row["doors"].AsArray() ?? [])
      doors.Add(
        new DoorLeaf(
          node["control"].AsString() ?? "",
          ReadArray(node["shut"]),
          ReadArray(node["open"]),
          ReadArray(node["opening"]),
          node["openCollides"].AsBool(false)
        )
      );

    return new CellBoxes(collision, selection?.ToArray(), controls, doors);
  }

  /// <summary>Writes the box keys into <paramref name="row"/> in the keys <see cref="Read"/> takes: a
  /// single collision box as <c>collisionBox</c>, several or none as <c>collisionBoxes</c>; no key for a
  /// null collision, selection, or an empty controls or doors list.</summary>
  public void WriteTo(JObject row) {
    if (Collision is { Length: 1 })
      row["collisionBox"] = ExBlockDef.Corners(Collision[0]);
    else if (Collision != null)
      row["collisionBoxes"] = Corners(Collision);
    if (Selection != null)
      row["selectionBoxes"] = Corners(Selection);
    if (Controls is { Count: > 0 })
      row["controls"] = new JArray(
        Controls.Select(c => new JObject {
          ["id"] = c.Id,
          ["box"] = ExBlockDef.Corners(c.Box),
        })
      );
    if (Doors is { Count: > 0 })
      row["doors"] = new JArray(
        Doors.Select(d => new JObject {
          ["control"] = d.Control,
          ["opening"] = Corners(d.Opening),
          ["open"] = Corners(d.Open),
          ["shut"] = Corners(d.Shut),
          ["openCollides"] = d.OpenCollides,
        })
      );
  }

  /// <summary>A copy turned by <paramref name="angle"/> degrees around the cell centre, the way
  /// <see cref="ExOrientation.RotateBoxes"/> turns a cell's collision. Ids are kept.</summary>
  public CellBoxes Turned(int angle) =>
    Turned(box => ExOrientation.RotateBoxes([box], angle)[0]);

  /// <summary>A copy with every box passed through <paramref name="turn"/>, for a node's 3D rotation.
  /// <paramref name="turn"/> may drop the id (<c>Cuboidf.RotatedCopy</c> does); it is re-attached by
  /// position.</summary>
  public CellBoxes Turned(Func<Cuboidf, Cuboidf> turn) {
    Cuboidf[]? selection = Selection?.Select(b => TurnKeepingId(b, turn)).ToArray();
    return new CellBoxes(
      Collision?.Select(turn).ToArray(),
      selection,
      Controls
        ?.Select(c => {
          int at = Selection == null ? -1 : IndexOfSame(Selection, c.Box);
          return c with {
            Box = at >= 0 ? selection![at] : TurnKeepingId(c.Box, turn),
          };
        })
        .ToArray(),
      Doors
        ?.Select(d => d with {
          Shut = [.. d.Shut.Select(turn)],
          Open = [.. d.Open.Select(turn)],
          Opening = [.. d.Opening.Select(turn)],
        })
        .ToArray()
    );
  }

  private static int IndexOfSame(Cuboidf[] boxes, Cuboidf box) {
    for (int i = 0; i < boxes.Length; i++)
      if (ReferenceEquals(boxes[i], box))
        return i;
    return -1;
  }

  private static Cuboidf TurnKeepingId(Cuboidf box, Func<Cuboidf, Cuboidf> turn) {
    Cuboidf turned = turn(box);
#if GAME_GE_1_22
    if (box is CuboidfWithId { Id: { } id })
      return new CuboidfWithId(turned) { Id = id };
#endif
    return turned;
  }

  private static Cuboidf WithId(Cuboidf box, string id) {
#if GAME_GE_1_22
    return new CuboidfWithId(box) { Id = id };
#else
    return box;
#endif
  }

  private static Cuboidf[] ReadArray(JsonObject node) {
    var boxes = new List<Cuboidf>();
    foreach (JsonObject item in node.AsArray() ?? [])
      if (item.AsObject<Cuboidf>() is { } box)
        boxes.Add(box);
    return [.. boxes];
  }

  private static JArray Corners(IEnumerable<Cuboidf> boxes) =>
    new(boxes.Select(ExBlockDef.Corners));

  private static bool Near(Cuboidf a, Cuboidf b) =>
    Math.Abs(a.X1 - b.X1) <= Tolerance
    && Math.Abs(a.Y1 - b.Y1) <= Tolerance
    && Math.Abs(a.Z1 - b.Z1) <= Tolerance
    && Math.Abs(a.X2 - b.X2) <= Tolerance
    && Math.Abs(a.Y2 - b.Y2) <= Tolerance
    && Math.Abs(a.Z2 - b.Z2) <= Tolerance;
}
