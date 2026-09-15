using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>Checks that a pressure vessel's construction stages accept rivets only, never nails or
/// bolts.</summary>
public static class PressureVesselGate {
  /// <summary>Every stage ingredient a construction-staged block declares, flattened across its
  /// stages.</summary>
  public static IEnumerable<JObject> StageIngredients(ExBlockDef def) {
    // Construction stages ride on the ExRightClickConstructable behaviour, not a fixed `attributes` path.
    if (def.ToJson()["entityBehaviors"] is not JArray behaviours)
      yield break;

    foreach (JToken behaviour in behaviours) {
      if (behaviour["properties"]?["stages"] is not JArray stages)
        continue;
      foreach (JToken stage in stages)
        if (stage["requireStacks"] is JArray required)
          foreach (JToken ingredient in required)
            if (ingredient is JObject o)
              yield return o;
    }
  }

  /// <summary>Every stage ingredient of <paramref name="def"/> whose code mentions nails.</summary>
  public static IReadOnlyList<string> NailedIngredients(ExBlockDef def) =>
    [
      .. StageIngredients(def)
        .Where(i => i["code"]?.ToString().Contains("nailsandstrips") == true)
        .Select(i => i["code"]!.ToString()),
    ];

  /// <summary>How many of <paramref name="rivetCode"/> the whole build costs, summed over its stages.</summary>
  public static int RivetsRequired(ExBlockDef def, string rivetCode) =>
    StageIngredients(def)
      .Where(i => i["code"]?.ToString() == rivetCode)
      .Sum(i => i["quantity"]?.Value<int>() ?? 0);
}
