using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Scenarios;

/// <summary>An <c>at</c> of a setup file: a plot-local cell, or a cell named by what an id
/// placed (setups.md, Positions).</summary>
internal sealed class SetupPosition {
  private enum Frame {
    None,
    Cell,
    Layout,
    Port,
  }

  private readonly Vec3i? _plain;
  private readonly Frame _frame;
  private readonly Vec3i? _offset;
  private readonly BlockFacing? _beyond;

  /// <summary>The id an <c>of</c> form names; null for a plain cell.</summary>
  public string? Of { get; }

  private SetupPosition(
    Vec3i? plain,
    string? of,
    Frame frame,
    Vec3i? offset,
    BlockFacing? beyond
  ) {
    _plain = plain;
    Of = of;
    _frame = frame;
    _offset = offset;
    _beyond = beyond;
  }

  /// <summary>The position <c>{ "of": id }</c>.</summary>
  public static SetupPosition OfId(string id) =>
    new(null, id, Frame.None, null, null);

  public static SetupPosition Parse(JToken? token, SetupJson.Where where) {
    if (token is JArray)
      return new(Triple(token, where), null, Frame.None, null, null);
    JObject node = SetupJson.Object(token, where);
    SetupJson.RefuseUnknown(node, where, "of", "cell", "layout", "port", "beyond");
    string of = SetupJson.String(node["of"], where.In("of"));
    string[] frames = [.. new[] { "cell", "layout", "port" }.Where(k => node[k] != null)];
    if (frames.Length > 1)
      throw where.Fail("takes one of \"cell\", \"layout\" and \"port\".");
    Frame frame = frames.Length == 0
      ? Frame.None
      : frames[0] switch {
        "cell" => Frame.Cell,
        "layout" => Frame.Layout,
        _ => Frame.Port,
      };
    Vec3i? offset = frames.Length == 0
      ? null
      : Triple(node[frames[0]], where.In(frames[0]));
    BlockFacing? beyond = null;
    if (node["beyond"] != null) {
      string face = SetupJson.String(node["beyond"], where.In("beyond"));
      beyond = face is "n" or "e" or "s" or "w" or "u" or "d"
        ? ExOrientation.FacingFromSide(face)
        : throw where.In("beyond").Fail($"\"{face}\" is not n, e, s, w, u or d.");
    }
    return new(null, of, frame, offset, beyond);
  }

  /// <summary>The cell this position names in <paramref name="world"/>.</summary>
  /// <exception cref="SetupFileException">The id has placed nothing, or a structure form names
  /// a block that is no exlib structure or a footprint cell with no port.</exception>
  public BlockPos Resolve(ISetupWorld world) {
    if (_plain is { } p)
      return new BlockPos(p.X, p.Y, p.Z);
    SetupPlacement placed = world.Placed(Of!)
      ?? throw new SetupFileException($"\"{Of}\" has placed nothing.");
    SetupFrame? frame = placed.Frame;
    if (_frame != Frame.None && frame == null)
      throw new SetupFileException(
        $"\"{Of}\" is no exlib structure, so it has no cell, layout or port."
      );
    BlockPos cell = _frame switch {
      Frame.Cell => FootprintCell(frame!),
      Frame.Layout => frame!.Layout?.Invoke(_offset!)
        ?? throw new SetupFileException($"\"{Of}\" has no layout."),
      Frame.Port => PortCell(frame!),
      _ => placed.Cell.Copy(),
    };
    if (_beyond == null)
      return cell;
    BlockFacing face = frame == null
      ? _beyond
      : ExOrientation.RotateFacing(_beyond, frame.Angle);
    return cell.AddCopy(face);
  }

  private BlockPos FootprintCell(SetupFrame frame) {
    Vec3i r = ExOrientation.RotateOffset(_offset!, frame.Angle);
    return frame.Principal.AddCopy(r.X, r.Y, r.Z);
  }

  private BlockPos PortCell(SetupFrame frame) {
    BlockPos at = FootprintCell(frame);
    var cell = frame.Footprint.FirstOrDefault(c => c.Pos.Equals(at));
    BlockFacing face = cell.PortFace == null
      ? throw new SetupFileException(
        $"\"{Of}\" declares no port at [{_offset!.X}, {_offset.Y}, {_offset.Z}]."
      )
      : ExOrientation.FacingFromSide(cell.PortFace)!;
    return at.AddCopy(face);
  }

  private static Vec3i Triple(JToken? token, SetupJson.Where where) {
    JArray a = SetupJson.Array(token, where);
    if (a.Count != 3)
      throw where.Fail("expected [x, y, z].");
    return new Vec3i(
      SetupJson.Integer(a[0], where.At(0)),
      SetupJson.Integer(a[1], where.At(1)),
      SetupJson.Integer(a[2], where.At(2))
    );
  }

  /// <summary>Every id this position names: none or one.</summary>
  public IEnumerable<string> Ids => Of == null ? [] : [Of];
}
