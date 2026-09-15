using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Structures;

/// <summary>The never-throwing reads the three multiblock attribute schemas share.</summary>
internal static class LayoutAttribute {
  /// <summary>One coordinate of a role or connector cell, or null when absent, non-integer, or out of <c>int</c> range.</summary>
  public static int? Coord(JToken? token) =>
    token is not JValue { Type: JTokenType.Integer } value
      ? null
      : value.Value switch {
        int i => i,
        long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
        _ => null,
      };

  /// <summary>Every (cell, key) pair under <c>attrs[key]</c>, in file order; empty when the shape is missing or malformed.</summary>
  public static IEnumerable<(
    (int X, int Y, int Z) Cell,
    string Key
  )> CellsByKey(JsonObject? attrs, string key) {
    JsonObject? node = attrs?[key];
    if (node?.Exists != true || node.Token is not JObject obj)
      yield break;

    foreach (var kv in obj) {
      if (kv.Value is not JArray cells)
        continue;
      foreach (JToken cell in cells)
        if (
          cell is JObject o
          && Coord(o["x"]) is int x
          && Coord(o["y"]) is int y
          && Coord(o["z"]) is int z
        )
          yield return ((x, y, z), kv.Key);
    }
  }
}
