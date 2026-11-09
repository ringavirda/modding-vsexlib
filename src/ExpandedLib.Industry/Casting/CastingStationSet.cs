using System;
using System.Collections.Generic;
using ExpandedLib.Catalogues;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Casting;

/// <summary>The station kinds one catalogue file or one C# contribution declares.</summary>
/// <param name="Schema">Schema version of the declaration.</param>
/// <param name="Stations">The declared kinds, in order.</param>
public sealed record CastingStationSet(
  int Schema,
  IReadOnlyList<CastingStationKind> Stations
) {
  /// <summary>The schema this parser writes and reads up to.</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>Parses one <c>config/castingstations</c> file. Returns false with a human-readable
  /// <paramref name="error"/> naming the malformed field; <paramref name="set"/> is then null.</summary>
  internal static bool TryParse(
    JsonObject? node,
    out CastingStationSet? set,
    out string? error
  ) {
    set = null;
    if (node is not { Exists: true }) {
      error = "missing casting station declaration";
      return false;
    }
    if (!SpecSchema.TryRead(node, CurrentSchema, out int schema, out error))
      return false;

    var kinds = new List<CastingStationKind>();
    foreach (JsonObject stationNode in node["stations"].AsArray() ?? []) {
      string code = stationNode["code"].AsString("");
      if (string.IsNullOrWhiteSpace(code)) {
        error = "a station names no 'code'";
        return false;
      }
      if (code != code.ToLowerInvariant() || code.Contains(':')) {
        error = $"station code '{code}' must be a bare lowercase string";
        return false;
      }

      var moulds = new List<MouldKind>();
      JsonObject[]? mouldNodes = stationNode["moulds"].AsArray();
      if (mouldNodes is null)
        moulds.Add(MouldKind.Open);
      else
        foreach (JsonObject mouldNode in mouldNodes) {
          string name = mouldNode.AsString("");
          if (!MoldSpec.TryParseMould(name, out MouldKind mould)) {
            error =
              $"station '{code}': invalid mould '{name}' (expected 'open' or 'closed')";
            return false;
          }
          moulds.Add(mould);
        }
      if (moulds.Count == 0) {
        error = $"station '{code}' takes no moulds";
        return false;
      }

      string sandBase = stationNode["sand"]["base"].AsString("");
      string sandHalf = stationNode["sand"]["half"].AsString("");
      if (
        string.IsNullOrWhiteSpace(sandBase)
        || string.IsNullOrWhiteSpace(sandHalf)
      ) {
        error = $"station '{code}' needs a 'sand' pair with 'base' and 'half'";
        return false;
      }

      string sandItem = stationNode["sand"]["item"].AsString("");
      if (string.IsNullOrWhiteSpace(sandItem)) {
        error = $"station '{code}' needs a 'sand.item'";
        return false;
      }

      kinds.Add(
        new CastingStationKind(code, moulds, sandBase, sandHalf, sandItem)
      );
    }

    error = null;
    set = new CastingStationSet(schema, kinds);
    return true;
  }
}
