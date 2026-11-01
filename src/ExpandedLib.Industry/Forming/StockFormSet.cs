using System.Collections.Generic;
using ExpandedLib.Catalogues;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Forming;

/// <summary>A mill deck admission: the code a player offers, and the stock item it enters as.</summary>
/// <param name="Offered">Full item code admitted at the deck.</param>
/// <param name="EntersAs">Full item code of the stock the offered item is converted into.</param>
public sealed record FeedstockEntry(string Offered, string EntersAs);

/// <summary>The stock forms and feedstock one catalogue file or one C# contribution declares.</summary>
/// <param name="Schema">Schema version of the declaration.</param>
/// <param name="Forms">The forms declared.</param>
/// <param name="Feedstock">The deck admissions declared.</param>
public sealed record StockFormSet(
  int Schema,
  IReadOnlyList<StockForm> Forms,
  IReadOnlyList<FeedstockEntry> Feedstock
) {
  /// <summary>The schema this parser writes and reads up to.</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>Parses one <c>config/stockforms</c> file. Returns false with a human-readable
  /// <paramref name="error"/> naming the malformed field; <paramref name="set"/> is then null.</summary>
  internal static bool TryParse(
    JsonObject? node,
    out StockFormSet? set,
    out string? error
  ) {
    set = null;
    if (node is not { Exists: true }) {
      error = "missing stock form declaration";
      return false;
    }
    if (!SpecSchema.TryRead(node, CurrentSchema, out int schema, out error))
      return false;

    var forms = new List<StockForm>();
    foreach (JsonObject entry in node["forms"].AsArray() ?? []) {
      string name = entry["name"].AsString("");
      if (!BareLowercase(name)) {
        error = $"form name '{name}' must be a bare lowercase string";
        return false;
      }
      var formerNames = new List<string>();
      foreach (JsonObject former in entry["formerNames"].AsArray() ?? []) {
        string formerName = former.AsString("");
        if (!BareLowercase(formerName)) {
          error =
            $"form '{name}': former name '{formerName}' must be a bare lowercase string";
          return false;
        }
        formerNames.Add(formerName);
      }
      float[] dims = new float[5];
      string[] keys =
      [
        "baseWidth",
        "baseThickness",
        "maxWidth",
        "baseLength",
        "spreadExponent",
      ];
      for (int i = 0; i < keys.Length; i++) {
        dims[i] = entry[keys[i]].AsFloat(0f);
        if (dims[i] <= 0f) {
          error = $"form '{name}': {keys[i]} must be above 0";
          return false;
        }
      }
      string? shape = entry["shape"].AsString();
      string? baseElement = entry["baseElement"].AsString();
      forms.Add(
        new StockForm(
          name,
          dims[0],
          dims[1],
          dims[2],
          dims[3],
          dims[4],
          string.IsNullOrEmpty(shape) ? null : shape,
          string.IsNullOrEmpty(baseElement) ? null : baseElement,
          [.. formerNames]
        )
      );
    }

    var feedstock = new List<FeedstockEntry>();
    foreach (JsonObject entry in node["feedstock"].AsArray() ?? []) {
      string offered = entry["offered"].AsString("");
      string entersAs = entry["entersAs"].AsString("");
      if (!offered.Contains(':') || !entersAs.Contains(':')) {
        error =
          $"feedstock '{offered}' to '{entersAs}': both must be full item codes";
        return false;
      }
      feedstock.Add(new FeedstockEntry(offered, entersAs));
    }

    error = null;
    set = new StockFormSet(schema, forms, feedstock);
    return true;
  }

  private static bool BareLowercase(string code) =>
    !string.IsNullOrWhiteSpace(code)
    && code == code.ToLowerInvariant()
    && !code.Contains(':');
}
