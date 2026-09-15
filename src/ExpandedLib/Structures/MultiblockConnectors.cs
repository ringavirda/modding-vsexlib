using System.Collections.Generic;
using ExpandedLib.Helpers;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Structures;

/// <summary>
/// Reads the <c>multiblockConnectors</c> attribute: for each authored (north-frame) offset, the outward
/// faces that cell's occupant must expose a connector on. A sibling of <c>multiblockStructure</c>, kept
/// out of vanilla's own schema. A layout with no connector gets <see cref="None"/>.
/// </summary>
public sealed class MultiblockConnectors {
  /// <summary>A layout that marks no connector. Every lookup answers empty.</summary>
  public static readonly MultiblockConnectors None = new(
    new Dictionary<(int X, int Y, int Z), List<string>>()
  );

  private static readonly string[] NoFaces = [];

  private readonly Dictionary<(int X, int Y, int Z), List<string>> _facesAt;

  private MultiblockConnectors(
    Dictionary<(int X, int Y, int Z), List<string>> facesAt
  ) => _facesAt = facesAt;

  /// <summary>True when the layout marks no cell with any connector demand.</summary>
  public bool IsEmpty => _facesAt.Count == 0;

  /// <summary>
  /// The authored outward face letters <paramref name="authoredOffset"/> demands, empty when that cell
  /// demands none. Authored, not rotated: the caller holds the structure's angle.
  /// </summary>
  public IReadOnlyList<string> OutwardFacesAt(
    (int X, int Y, int Z) authoredOffset
  ) => _facesAt.TryGetValue(authoredOffset, out var faces) ? faces : NoFaces;

  /// <summary>
  /// Reads the <c>multiblockConnectors</c> attribute, inverted from face-to-cells to cell-to-faces.
  /// Returns <see cref="None"/> when absent. Never throws.
  /// </summary>
  public static MultiblockConnectors FromAttributes(JsonObject? attributes) {
    var map = new Dictionary<(int X, int Y, int Z), List<string>>();
    foreach (
      var (cell, key) in LayoutAttribute.CellsByKey(
        attributes,
        "multiblockConnectors"
      )
    ) {
      // Skipped: a key that is not a horizontal side letter cannot be resolved by rotation.
      if (ExOrientation.FacingFromSide(key) == null)
        continue;
      if (!map.TryGetValue(cell, out List<string>? faces))
        map[cell] = faces = [];
      if (!faces.Contains(key))
        faces.Add(key);
    }
    return map.Count == 0 ? None : new MultiblockConnectors(map);
  }
}
