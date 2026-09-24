using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// The two lists a guard holds beside its rule: <c>Allowed</c>, permanent exceptions, each with its
/// reason, and <c>KnownFindings</c>, defects awaiting a fix, each described in words. A finding on
/// neither list fails the guard, and so does a known entry the rule does not report.
/// </summary>
public static class FindingLists {
  /// <summary>Asserts <paramref name="findings"/> against a guard's two lists.</summary>
  /// <param name="findings">The rule's findings, one line each.</param>
  /// <param name="allowed">Key to the reason the finding it keys is a permanent exception. An
  /// allowed key that keys no finding is not an error.</param>
  /// <param name="known">Key to the defect it keys, in words. Every known key must key at least
  /// one finding.</param>
  /// <param name="keyOf">The key of one finding, compared ordinally with both lists' keys; null
  /// keys a finding by its whole text.</param>
  /// <exception cref="ArgumentException">A key is on both lists, or an entry's reason or
  /// description is blank.</exception>
  /// <exception cref="InvalidOperationException">A finding is keyed by neither list, or a known key
  /// keys no finding. The message names each.</exception>
  public static void Assert(
    IEnumerable<string> findings,
    IReadOnlyDictionary<string, string> allowed,
    IReadOnlyDictionary<string, string> known,
    Func<string, string>? keyOf = null
  ) {
    keyOf ??= f => f;
    string[] both =
    [
      .. allowed.Keys.Intersect(known.Keys, StringComparer.Ordinal),
    ];
    if (both.Length > 0)
      throw new ArgumentException(
        "on both the allowed and the known list: " + string.Join(", ", both)
      );
    string[] blank =
    [
      .. allowed
        .Concat(known)
        .Where(e => string.IsNullOrWhiteSpace(e.Value))
        .Select(e => e.Key),
    ];
    if (blank.Length > 0)
      throw new ArgumentException(
        "listed without a reason: " + string.Join(", ", blank)
      );

    List<string> all = [.. findings];
    var keys = all.Select(keyOf).ToHashSet(StringComparer.Ordinal);
    List<string> unlisted =
    [
      .. all.Where(f =>
      {
        string key = keyOf(f);
        return !allowed.ContainsKey(key) && !known.ContainsKey(key);
      }),
    ];
    List<string> stale = [.. known.Keys.Where(k => !keys.Contains(k))];
    if (unlisted.Count == 0 && stale.Count == 0)
      return;

    var parts = new List<string>();
    if (unlisted.Count > 0)
      parts.Add(
        Listed($"{unlisted.Count} finding(s) on neither list", unlisted)
      );
    if (stale.Count > 0)
      parts.Add(
        Listed(
          $"{stale.Count} known finding(s) that no longer fail; remove them",
          stale
        )
      );
    throw new InvalidOperationException(string.Join("\n", parts));
  }

  // The first item shares the heading's line, which is all a one-line runner summary shows.
  private static string Listed(string heading, List<string> items) =>
    $"{heading}: {string.Join("\n  ", items)}";
}
