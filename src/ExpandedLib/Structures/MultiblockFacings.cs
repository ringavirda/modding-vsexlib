using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Structures;

/// <summary>
/// Rotates the facing segment of a multiblock layout's oriented part codes (from
/// <c>attributes.multiblockFacings</c>) to match the structure's placed angle. Wildcards, vertical
/// parts and codes with no facing pass through unchanged.
/// </summary>
public sealed class MultiblockFacings {
  /// <summary>A layout with no oriented parts. <see cref="Rotate"/> is the identity.</summary>
  public static readonly MultiblockFacings None = new(
    new Dictionary<string, int[]>()
  );

  private readonly Dictionary<string, int[]> _segmentOf;

  private MultiblockFacings(Dictionary<string, int[]> segmentOf) =>
    _segmentOf = segmentOf;

  /// <summary>True when no part of the layout is orientation-checked.</summary>
  public bool IsEmpty => _segmentOf.Count == 0;

  /// <summary>Reads the <c>multiblockFacings</c> attribute, or <see cref="None"/> when absent.</summary>
  public static MultiblockFacings FromAttributes(JsonObject? attributes) {
    JsonObject? node = attributes?["multiblockFacings"];
    if (node?.Exists != true)
      return None;

    // JsonObject has no key enumeration; reads the underlying token directly.
    if (node.Token is not JObject obj)
      return None;

    var map = new Dictionary<string, int[]>();
    foreach (var kv in obj) {
      // An array of segment indices; a bare integer is accepted too.
      if (kv.Value is JArray array) {
        int[] segments = array
          .Where(t => t.Type == JTokenType.Integer)
          .Select(t => (int)t)
          .ToArray();
        if (segments.Length > 0)
          map[kv.Key] = segments;
      } else if (kv.Value?.Type == JTokenType.Integer)
        map[kv.Key] = [(int)kv.Value];
    }
    return map.Count == 0 ? None : new MultiblockFacings(map);
  }

  /// <summary>
  /// The code a cell requires once the structure is turned to <paramref name="angle"/>: the authored code
  /// with its facing segment rotated. A code this layout did not mark as oriented is returned unchanged.
  /// </summary>
  public AssetLocation Rotate(AssetLocation code, int angle) {
    if (_segmentOf.Count == 0)
      return code;

    // ToString(), not ToShortString(): the table is keyed by the full domained form.
    if (!_segmentOf.TryGetValue(code.ToString(), out int[]? segments))
      return code;

    string? rotated = RotateSegments(code.Path, segments, angle);
    return rotated == null ? code : new AssetLocation(code.Domain, rotated);
  }

  /// <summary>
  /// Swaps every listed dash-segment of <paramref name="path"/> for its <paramref name="angle"/>-rotated
  /// orientation. Returns null when any index is out of range or names a segment that is not an orientation.
  /// </summary>
  internal static string? RotateSegments(string path, int[] segments, int angle) {
    var segmented = new ExOrientation.SegmentedCode(path);
    foreach (int segment in segments)
      if (
        !segmented.InRange(segment)
        || !ExOrientation.IsOrientationToken(segmented[segment])
      )
        return null;

    foreach (int segment in segments)
      segmented.Parts[segment] = ExOrientation.RotateOrientationToken(
        segmented.Parts[segment],
        angle
      );
    return segmented.Join();
  }
}
