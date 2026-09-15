using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Checks that a pressure vessel's construction stages accept rivets only, never nails or bolts: a
/// riveted joint is tight where a nailed one is merely strong, and a boiler sealed with nails would leak
/// steam. Shared because two mods ship a boiler and both are gated the same way.
/// </summary>
public static class PressureVesselGate {
  /// <summary>
  /// Every stage ingredient a construction-staged block declares, flattened across its stages. Reads the
  /// built JSON rather than the builder, so it sees what the game will.
  /// </summary>
  public static IEnumerable<JObject> StageIngredients(ExBlockDef def) {
    // Construction stages ride on the ExRightClickConstructable behaviour rather than under `attributes`,
    // so the walk is over entityBehaviors and not a fixed path.
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

  /// <summary>
  /// Every stage ingredient of <paramref name="def"/> whose code mentions nails - empty on a properly
  /// gated vessel. Matched on the substring rather than on an exact code, so a wildcard, a domain change
  /// or a metal capture cannot slip one past.
  /// </summary>
  /// <remarks>
  /// A caller must assert the premise too: a block whose stages stopped resolving at all yields nothing
  /// here and would pass a negative check while proving nothing. <see cref="StageIngredients"/> is what to
  /// check for that.
  /// </remarks>
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
