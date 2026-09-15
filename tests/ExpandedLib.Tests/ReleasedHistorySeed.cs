using System.Collections.Generic;
using ExpandedLib.Testing;

namespace ExpandedLib.Tests;

/// <summary>
/// exlib's own contribution to the release-history registry: the codes and version it has shipped.
/// Read through <see cref="ReleasedCodes"/> and <see cref="ReleasedVersions"/>.
/// </summary>
public static class ReleasedHistorySeed {
  /// <summary>exlib 0.7.0: no blocktype JSON, but the filler is written into released worlds by every
  /// mega-block footprint and carries the same migration contract as a placed block.</summary>
  private static readonly IReadOnlyList<ReleasedCodes.Shipped> Shipped =
  [
    new(
      "exlib",
      "structurefiller",
      "exlib:structurefiller",
      ["exlib:structurefiller"]
    ),
  ];

  public static void Register() =>
    ReleasedHistory.Register(
      "exlib",
      Shipped,
      [],
      new Dictionary<string, string> { ["exlib"] = "0.7.2" },
      []
    );
}
