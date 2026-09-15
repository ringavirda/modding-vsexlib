using System;
using System.Collections.Generic;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>Horizontal rotation math shared by the mod family's oriented blocks: north 0, west 90,
/// south 180, east 270.</summary>
public static class ExOrientation {
  /// <summary>A block code's path split on <c>-</c>, domain stripped.</summary>
  public readonly struct SegmentedCode {
    /// <summary>The path's dash-segments, domain stripped.</summary>
    public string[] Parts { get; }

    public SegmentedCode(string code) {
      int colon = code.IndexOf(':');
      string path = colon >= 0 ? code[(colon + 1)..] : code;
      Parts = path.Split('-');
    }

    /// <summary>Segment count.</summary>
    public int Count => Parts.Length;

    /// <summary>The segment at <paramref name="index"/>.</summary>
    public string this[int index] => Parts[index];

    /// <summary>True when <paramref name="index"/> names an actual segment.</summary>
    public bool InRange(int index) => index >= 0 && index < Parts.Length;

    /// <summary>Rejoins <see cref="Parts"/> (or a caller-modified copy of it) into a dash-separated path.</summary>
    public static string Join(IReadOnlyList<string> parts) =>
      string.Join('-', parts);

    /// <summary>Rejoins <see cref="Parts"/> as authored, with no segment replaced.</summary>
    public string Join() => Join(Parts);
  }

  /// <summary>Returns the rotation angle a horizontal side variant names, full word or single letter.</summary>
  public static int AngleFromSide(string? side) =>
    side switch {
      "east" or "e" => 270,
      "south" or "s" => 180,
      "west" or "w" => 90,
      _ => 0,
    };

  /// <summary>Rotates a structure-local offset by <paramref name="angle"/> (Y is untouched).</summary>
  public static Vec3i RotateOffset(Vec3i off, int angle) =>
    RotateOffset(off.X, off.Y, off.Z, angle);

  /// <summary>Rotates a structure-local offset by <paramref name="angle"/> (Y is untouched).</summary>
  public static Vec3i RotateOffset(int x, int y, int z, int angle) {
    angle = ((angle % 360) + 360) % 360;
    var (dx, dz) = angle switch {
      90 => (z, -x),
      180 => (-x, -z),
      270 => (-z, x),
      _ => (x, z),
    };
    return new Vec3i(dx, y, dz);
  }

  /// <summary>Rotates a continuous XZ offset by <paramref name="angle"/>, the same turn as <see cref="RotateOffset(int, int, int, int)"/> for a hit point.</summary>
  public static (double X, double Z) RotateXZ(double x, double z, int angle) {
    angle = ((angle % 360) + 360) % 360;
    return angle switch {
      90 => (z, -x),
      180 => (-x, -z),
      270 => (-z, x),
      _ => (x, z),
    };
  }

  /// <summary>Takes a world-frame XZ offset back into the structure's authored frame, the inverse of <see cref="RotateXZ"/>.</summary>
  public static (double X, double Z) UnrotateXZ(
    double x,
    double z,
    int angle
  ) => RotateXZ(x, z, -angle);

  /// <summary>Converts a structure-local offset into a world position.</summary>
  public static BlockPos GlobalPos(
    BlockPos origin,
    int localX,
    int localY,
    int localZ,
    int angle
  ) {
    Vec3i r = RotateOffset(localX, localY, localZ, angle);
    return origin.AddCopy(r.X, r.Y, r.Z);
  }

  /// <summary>Reads a structure-local <c>{ x, y, z }</c> offset from a JSON node, falling back to <paramref name="fallback"/> when absent.</summary>
  public static Vec3i ReadOffset(JsonObject? node, Vec3i fallback) {
    if (node == null || !node.Exists)
      return fallback;
    return new Vec3i(
      node["x"].AsInt(fallback.X),
      node["y"].AsInt(fallback.Y),
      node["z"].AsInt(fallback.Z)
    );
  }

  /// <summary>The double counterpart of <see cref="ReadOffset"/>, for continuous points such as particle anchors.</summary>
  public static Vec3d ReadOffsetD(JsonObject? node, Vec3d fallback) {
    if (node == null || !node.Exists)
      return fallback;
    return new Vec3d(
      node["x"].AsDouble(fallback.X),
      node["y"].AsDouble(fallback.Y),
      node["z"].AsDouble(fallback.Z)
    );
  }

  /// <summary>Resolves a structure-local offset node to a world cell for the placed rotation.</summary>
  public static BlockPos WorldPosFromAttr(
    BlockPos origin,
    JsonObject? node,
    Vec3i fallback,
    int angle
  ) {
    Vec3i off = ReadOffset(node, fallback);
    Vec3i r = RotateOffset(off, angle);
    return origin.AddCopy(r.X, r.Y, r.Z);
  }

  /// <summary>Returns copies of <paramref name="boxes"/> rotated around the block centre by <paramref name="angle"/> degrees; the input array itself for angle 0.</summary>
  public static Cuboidf[] RotateBoxes(Cuboidf[] boxes, int angle) {
    angle = ((angle % 360) + 360) % 360;
    if (angle == 0 || boxes.Length == 0)
      return boxes;
    var origin = new Vec3d(0.5, 0.5, 0.5);
    var rotated = new Cuboidf[boxes.Length];
    for (int i = 0; i < boxes.Length; i++)
      rotated[i] = boxes[i].RotatedCopy(0, angle, 0, origin);
    return rotated;
  }

  /// <summary>Rotates a horizontal block face by <paramref name="angle"/>; vertical faces come back unchanged.</summary>
  public static BlockFacing RotateFacing(BlockFacing baseFace, int angle) {
    if (baseFace.IsVertical)
      return baseFace;
    Vec3i n = baseFace.Normali;
    Vec3i r = RotateOffset(new Vec3i(n.X, 0, n.Z), angle);
    return BlockFacing.FromNormal(r) ?? baseFace;
  }

  /// <summary>Inverse of <see cref="AngleFromSide"/>: the side word for a rotation angle.</summary>
  public static string SideFromAngle(int angle, bool asLetter = false) {
    angle = ((angle % 360) + 360) % 360;
    return angle switch {
      90 => asLetter ? "w" : "west",
      180 => asLetter ? "s" : "south",
      270 => asLetter ? "e" : "east",
      _ => asLetter ? "n" : "north",
    };
  }

  /// <summary>String counterpart of <see cref="RotateFacing"/>: rotates a horizontal side word by <paramref name="angle"/>, preserving its form.</summary>
  public static string RotateSideWord(string side, int angle) {
    if (!IsHorizontalSideWord(side))
      return side;
    bool asLetter = side.Length == 1;
    return SideFromAngle(AngleFromSide(side) + angle, asLetter);
  }

  /// <summary>True for the four horizontal side words, full or single-letter.</summary>
  public static bool IsHorizontalSideWord(string? side) =>
    side is "north" or "south" or "east" or "west" or "n" or "s" or "e" or "w";

  /// <summary>Returns the <see cref="BlockFacing"/> a <c>side</c> or <c>orientation</c> token names, in either spelling; null when the token names no facing.</summary>
  public static BlockFacing? FacingFromSide(string? side) {
    if (string.IsNullOrEmpty(side))
      return null;
    if (side is "u")
      return BlockFacing.UP;
    if (side is "d")
      return BlockFacing.DOWN;
    if (!IsHorizontalSideWord(side))
      return BlockFacing.FromCode(side);
    return BlockFacing.FromCode(
      SideFromAngle(AngleFromSide(side), asLetter: false)
    );
  }

  /// <summary>Returns the token a <see cref="BlockFacing"/> wears in a block code, full word or single letter.</summary>
  public static string TokenOf(BlockFacing facing, bool asLetter) =>
    SideFromAngle(AngleFromSide(facing.Code), asLetter);

  #region Multi-direction orientation tokens (network nodes)

  // A token is one direction letter or whole axis pairs, each axis used at most once.

  private static readonly string[] AxisPairs =
  [
    "ns",
    "sn",
    "we",
    "ew",
    "ud",
    "du",
  ];

  /// <summary>True for a whole code segment naming an orientation: a single side or a concatenation of complete axis pairs.</summary>
  public static bool IsOrientationToken(string? token) {
    if (string.IsNullOrEmpty(token))
      return false;
    if (IsHorizontalSideWord(token) || token is "up" or "down" or "u" or "d")
      return true;

    if (token.Length % 2 != 0 || token.Length > 6)
      return false;
    var axesSeen = new List<char>(3);
    for (int i = 0; i < token.Length; i += 2) {
      string pair = token.Substring(i, 2);
      if (Array.IndexOf(AxisPairs, pair) < 0)
        return false;
      char axis = AxisOf(pair[0]);
      if (axesSeen.Contains(axis))
        return false;
      axesSeen.Add(axis);
    }
    return true;
  }

  /// <summary><see cref="RotateSideWord"/> for multi-direction tokens, emitted in canonical axis order; a non-orientation token comes back unchanged.</summary>
  public static string RotateOrientationToken(string token, int angle) {
    if (!IsOrientationToken(token))
      return token;
    if (
      token.Length > 1
      && !IsHorizontalSideWord(token)
      && token is not ("up" or "down")
    ) {
      var axes = new List<char>(3);
      foreach (char c in token) {
        char rotated = RotateLetter(c, angle);
        char axis = AxisOf(rotated);
        if (!axes.Contains(axis))
          axes.Add(axis);
      }
      var sb = new System.Text.StringBuilder(token.Length);
      foreach (char axis in "nwu")
        if (axes.Contains(axis))
          sb.Append(
            axis == 'n' ? "ns"
            : axis == 'w' ? "we"
            : "ud"
          );
      return sb.ToString();
    }
    return RotateSideWord(token, angle);
  }

  /// <summary>Whether <paramref name="token"/> moves under a Y rotation.</summary>
  public static bool RotatesUnderY(string token) =>
    IsOrientationToken(token) && RotateOrientationToken(token, 90) != token;

  /// <summary>The axis a direction letter belongs to, named by its first member: n, w or u.</summary>
  private static char AxisOf(char direction) =>
    direction switch {
      'n' or 's' => 'n',
      'w' or 'e' => 'w',
      _ => 'u',
    };

  private static char RotateLetter(char direction, int angle) =>
    direction is 'u' or 'd'
      ? direction
      : RotateSideWord(direction.ToString(), angle)[0];

  #endregion

  /// <summary>Rotates a block-relative float coordinate around a cell centre by <paramref name="angle"/>.</summary>
  public static void RotateAroundCenter(
    ref float x,
    ref float z,
    int angle,
    float center = 0.5f
  ) {
    angle = ((angle % 360) + 360) % 360;
    float dx = x - center;
    float dz = z - center;
    var (ndx, ndz) = angle switch {
      90 => (dz, -dx),
      180 => (-dx, -dz),
      270 => (-dz, dx),
      _ => (dx, dz),
    };
    x = center + ndx;
    z = center + ndz;
  }
}
