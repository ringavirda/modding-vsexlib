using System.Collections.Generic;
using ExpandedLib.Catalogues;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Casting;

/// <summary>The tool-mould types one catalogue file or one C# contribution declares oversize.</summary>
/// <param name="Schema">Schema version of the declaration.</param>
/// <param name="Oversize">The <c>tooltype</c> variants too large for the mould pedestal, bare lowercase.</param>
public sealed record MoldFitSet(int Schema, IReadOnlyList<string> Oversize) {
  /// <summary>The schema this parser writes and reads up to.</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>Parses one <c>config/moldfits</c> file. Returns false with a human-readable
  /// <paramref name="error"/> naming the malformed field; <paramref name="set"/> is then null.</summary>
  internal static bool TryParse(
    JsonObject? node,
    out MoldFitSet? set,
    out string? error
  ) {
    set = null;
    if (node is not { Exists: true }) {
      error = "missing mould fit declaration";
      return false;
    }
    if (!SpecSchema.TryRead(node, CurrentSchema, out int schema, out error))
      return false;

    var oversize = new List<string>();
    foreach (JsonObject entry in node["oversize"].AsArray() ?? []) {
      string tooltype = entry.AsString("");
      if (
        string.IsNullOrWhiteSpace(tooltype)
        || tooltype != tooltype.ToLowerInvariant()
        || tooltype.Contains(':')
      ) {
        error =
          $"oversize tooltype '{tooltype}' must be a bare lowercase string";
        return false;
      }
      oversize.Add(tooltype);
    }

    error = null;
    set = new MoldFitSet(schema, oversize);
    return true;
  }
}
