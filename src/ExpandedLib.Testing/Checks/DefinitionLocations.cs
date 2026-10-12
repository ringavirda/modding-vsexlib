using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards that no two definitions share a <see cref="IExDef.Location"/>: the registry keys on it,
/// so a second definition on a location replaces the first without an error, and a variant group
/// does not tell two families apart.
/// </summary>
public static class DefinitionLocations {
  /// <summary>What one run read and found.</summary>
  /// <param name="Definitions">The definitions read.</param>
  /// <param name="Findings">One line per shared location, as
  /// <c>{golden path}: {n} definitions share this location</c>.</param>
  public sealed record Result(int Definitions, IReadOnlyList<string> Findings);

  /// <summary>Groups <paramref name="defs"/> by location, ignoring case, and reports each location
  /// more than one of them holds.</summary>
  /// <param name="defs">The definitions to read.</param>
  /// <returns>The definitions read and one finding per shared location, in path order; no findings
  /// when every location is held once.</returns>
  public static Result Check(IEnumerable<IExDef> defs) {
    List<IExDef> all = defs.ToList();
    var findings = all.GroupBy(
        DefinitionGoldens.RelativePath,
        StringComparer.OrdinalIgnoreCase
      )
      .Where(g => g.Count() > 1)
      .OrderBy(g => g.Key, StringComparer.Ordinal)
      .Select(g => $"{g.Key}: {g.Count()} definitions share this location")
      .ToList();
    return new Result(all.Count, findings);
  }
}
