using Newtonsoft.Json.Linq;

namespace ExpandedLib.Definitions;

/// <summary>The rule the block and item builders share for the <c>shape</c> / <c>shapebytype</c> pair.</summary>
internal static class ShapeEntries {
  /// <summary>
  /// Copies <c>shape.selectiveElements</c> into every <c>shapebytype</c> entry that names none. The game
  /// resolves a matching by-type entry in place of the plain shape, so a list kept only on the plain
  /// entry governs no variant a by-type wildcard matches. Mutates <paramref name="root"/>; the builders
  /// pass the clone their <c>ToJson</c> returns.
  /// </summary>
  internal static void SpreadSelectiveElements(JObject root) {
    if (
      root["shape"]?["selectiveElements"] is not JArray selective
      || root["shapebytype"] is not JObject byType
    )
      return;
    foreach (JProperty entry in byType.Properties())
      if (entry.Value is JObject shape && shape["selectiveElements"] == null)
        shape["selectiveElements"] = selective.DeepClone();
  }
}
