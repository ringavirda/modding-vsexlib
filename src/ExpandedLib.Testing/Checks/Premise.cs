using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// What a guard assumes about the corpus it reads, asserted before its rule runs: a guard over an
/// empty or partial corpus passes while checking nothing.
/// </summary>
public static class Premise {
  /// <summary>Reads <paramref name="corpus"/> once and asserts it holds something.</summary>
  /// <param name="corpus">What the guard reads: files, types, definitions.</param>
  /// <param name="what">The corpus in words, for the failure message.</param>
  /// <returns>The corpus, read into a list.</returns>
  /// <exception cref="InvalidOperationException"><paramref name="corpus"/> is empty.</exception>
  public static IReadOnlyList<T> NotEmpty<T>(IEnumerable<T> corpus, string what) {
    List<T> read = [.. corpus];
    if (read.Count == 0)
      throw new InvalidOperationException(
        $"no {what} found - the guard would check nothing"
      );
    return read;
  }

  /// <summary>Asserts that <paramref name="read"/> holds every entry of
  /// <paramref name="census"/>, compared ordinally.</summary>
  /// <param name="read">The keys the guard reads.</param>
  /// <param name="census">The keys it must reach; must not be empty.</param>
  /// <param name="what">The census in words, for the failure message.</param>
  /// <exception cref="InvalidOperationException"><paramref name="census"/> is empty, or an entry is
  /// missing from <paramref name="read"/>; the message names each missing entry.</exception>
  public static void Covers(
    IEnumerable<string> read,
    IEnumerable<string> census,
    string what
  ) {
    IReadOnlyList<string> expected = NotEmpty(census, what);
    var seen = read.ToHashSet(StringComparer.Ordinal);
    string[] missing = [.. expected.Where(e => !seen.Contains(e))];
    if (missing.Length > 0)
      throw new InvalidOperationException(
        $"{missing.Length} of {expected.Count} {what} not read: "
          + string.Join("\n  ", missing)
      );
  }

  /// <summary>Asserts that <paramref name="goldenPaths"/> reaches every golden blocktype
  /// <paramref name="domain"/> ships, as <see cref="SelectorCoverage.GoldenBlocktypes"/> lists
  /// them.</summary>
  /// <param name="goldenPaths">The definitions the guard reads, each as
  /// <see cref="DefinitionGoldens.RelativePath"/> (<c>{domain}/blocktypes/...</c>).</param>
  /// <param name="domain">The domain whose goldens form the census.</param>
  /// <exception cref="InvalidOperationException"><paramref name="domain"/> ships no golden
  /// blocktype, or one is not read; the message names each missing golden.</exception>
  public static void Covers(IEnumerable<string> goldenPaths, string domain) =>
    Covers(
      goldenPaths,
      SelectorCoverage
        .GoldenBlocktypes(domain)
        .Select(p =>
          p[(p.IndexOf("/goldens/", StringComparison.Ordinal) + 9)..]
        ),
      $"golden blocktypes of {domain}"
    );
}
