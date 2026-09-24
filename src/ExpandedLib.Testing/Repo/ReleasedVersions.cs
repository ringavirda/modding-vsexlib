using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ExpandedLib.Testing;

/// <summary>
/// The highest version published per modid, as the mods' test <c>ModuleInit</c>s register it in
/// <see cref="ReleasedHistory"/>. Cannot be derived from <c>dist/Releases/</c>, gitignored build
/// output that holds only the newest local build.
/// </summary>
public static class ReleasedVersions {
  private static readonly Regex Semantic = new(
    @"^(\d+)\.(\d+)\.(\d+)(?:-(.+))?$",
    RegexOptions.Compiled
  );

  /// <summary>modid -> the newest version registered under it, across every branch: the full
  /// registrations' versions and every release row.</summary>
  public static IReadOnlyDictionary<string, string> HighestPublished =>
    ReleasedHistory.AllVersions;

  /// <summary>Orders two versions: numerically by <c>major.minor.patch</c>, then a version with a
  /// <c>-suffix</c> (a pre-release) before the same version without one, then by the suffixes'
  /// dot-separated identifiers in turn: two all-digit identifiers as numbers, any other pair
  /// ordinally, and a suffix that runs out first before the longer one.</summary>
  /// <param name="a">A version such as <c>0.8.2</c> or <c>0.8.0-preview.3</c>.</param>
  /// <param name="b">The version to compare with.</param>
  /// <returns>Negative when <paramref name="a"/> is older, zero when equal, positive when newer.
  /// </returns>
  /// <exception cref="ArgumentException">Either is not <c>major.minor.patch</c> with an optional
  /// <c>-suffix</c>.</exception>
  public static int Compare(string a, string b) {
    Match x = Parse(a, nameof(a)),
      y = Parse(b, nameof(b));
    for (int i = 1; i <= 3; i++) {
      int c = int.Parse(x.Groups[i].Value)
        .CompareTo(int.Parse(y.Groups[i].Value));
      if (c != 0)
        return c;
    }
    bool xPre = x.Groups[4].Success,
      yPre = y.Groups[4].Success;
    if (xPre != yPre)
      return xPre ? -1 : 1;
    string[] xs = x.Groups[4].Value.Split('.'),
      ys = y.Groups[4].Value.Split('.');
    for (int i = 0; i < Math.Min(xs.Length, ys.Length); i++) {
      int c =
        IsNumeric(xs[i]) && IsNumeric(ys[i])
          ? BigInteger.Parse(xs[i]).CompareTo(BigInteger.Parse(ys[i]))
          : string.CompareOrdinal(xs[i], ys[i]);
      if (c != 0)
        return c;
    }
    return xs.Length.CompareTo(ys.Length);
  }

  private static bool IsNumeric(string identifier) =>
    identifier.Length > 0 && identifier.All(char.IsAsciiDigit);

  private static Match Parse(string version, string name) {
    Match m = Semantic.Match(version);
    return m.Success
      ? m
      : throw new ArgumentException(
        $"'{version}' is not major.minor.patch[-suffix]",
        name
      );
  }
}
