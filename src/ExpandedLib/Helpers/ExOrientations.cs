using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ExpandedLib.Helpers;

/// <summary>One declared set of orientation tokens, plus the rotation rule that set implies.</summary>
public sealed class ExOrientationScheme {
  private readonly HashSet<string> _tokens;

  // One token per face set, first declared, for the fallback half of the rule.
  private readonly Dictionary<string, string> _bySet;

  internal ExOrientationScheme(string name, params string[] tokens) {
    Name = name;
    Tokens = tokens;
    _tokens = [.. tokens];
    _bySet = [];
    foreach (string t in tokens)
      _bySet.TryAdd(SetKey(t), t);
  }

  /// <summary>The scheme's name.</summary>
  public string Name { get; }

  /// <summary>Every token this scheme declares, in declaration order.</summary>
  public IReadOnlyList<string> Tokens { get; }

  /// <summary>Whether <paramref name="token"/> is one this scheme declares.</summary>
  public bool Contains(string? token) =>
    token != null && _tokens.Contains(token);

  /// <summary>Rotates <paramref name="token"/> by <paramref name="angle"/> degrees about Y; a token this scheme does not declare comes back unchanged.</summary>
  public string Rotate(string? token, int angle) {
    if (token == null || !_tokens.Contains(token))
      return token ?? "";

    string ordered = RotateLetters(token, angle);
    if (_tokens.Contains(ordered))
      return ordered;
    return _bySet.TryGetValue(SetKey(ordered), out string? bySet)
      ? bySet
      : token;
  }

  /// <summary>Whether <paramref name="token"/> moves under a Y rotation.</summary>
  public bool RotatesUnderY(string? token) =>
    Contains(token) && Rotate(token, 90) != token;

  /// <summary>Rotates every direction letter, preserving the input's order; vertical letters are untouched.</summary>
  private static string RotateLetters(string token, int angle) {
    var sb = new StringBuilder(token.Length);
    foreach (char c in token)
      sb.Append(
        c is 'u' or 'd'
          ? c
          : ExOrientation.SideFromAngle(
            ExOrientation.AngleFromSide(c.ToString()) + angle,
            asLetter: true
          )[0]
      );
    return sb.ToString();
  }

  /// <summary>The face set of a token, order-independent.</summary>
  private static string SetKey(string token) =>
    string.Concat(token.Distinct().OrderBy(c => c));
}

/// <summary>The named orientation schemes every mod declares against.</summary>
public static class ExOrientations {
  /// <summary>The four horizontal faces.</summary>
  public static readonly ExOrientationScheme Face = new(
    "Face",
    "n",
    "e",
    "s",
    "w"
  );

  /// <summary>All six faces.</summary>
  public static readonly ExOrientationScheme FaceAll = new(
    "FaceAll",
    "n",
    "e",
    "s",
    "w",
    "u",
    "d"
  );

  /// <summary>Undirected axes including vertical.</summary>
  public static readonly ExOrientationScheme Axis = new(
    "Axis",
    "ns",
    "we",
    "ud"
  );

  /// <summary>Undirected horizontal axes.</summary>
  public static readonly ExOrientationScheme AxisFlat = new(
    "AxisFlat",
    "ns",
    "we"
  );

  /// <summary>Horizontal axes where order encodes input to output.</summary>
  public static readonly ExOrientationScheme DirectedAxisFlat = new(
    "DirectedAxisFlat",
    "ns",
    "we",
    "ew",
    "sn"
  );

  /// <summary>Axes where order encodes input to output, including vertical.</summary>
  public static readonly ExOrientationScheme DirectedAxis = new(
    "DirectedAxis",
    "ns",
    "we",
    "ud",
    "sn",
    "ew",
    "du"
  );

  /// <summary>Two adjacent faces, spelled as authored.</summary>
  public static readonly ExOrientationScheme PipeBend = new(
    "PipeBend",
    "nw",
    "se",
    "en",
    "ws",
    "un",
    "us",
    "uw",
    "ue",
    "dn",
    "ds",
    "dw",
    "de"
  );

  /// <summary>Four horizontal bends.</summary>
  public static readonly ExOrientationScheme CanalBend = new(
    "CanalBend",
    "nw",
    "se",
    "en",
    "ws"
  );

  /// <summary>Three faces.</summary>
  public static readonly ExOrientationScheme PipeTee = new(
    "PipeTee",
    "uns",
    "uwe",
    "dns",
    "dwe",
    "nes",
    "esw",
    "swn",
    "wne",
    "dnu",
    "deu",
    "dsu",
    "dwu"
  );

  /// <summary>Four horizontal tees.</summary>
  public static readonly ExOrientationScheme CanalTee = new(
    "CanalTee",
    "nes",
    "esw",
    "swn",
    "wne"
  );

  /// <summary>Four faces, three planes.</summary>
  public static readonly ExOrientationScheme PipeCross = new(
    "PipeCross",
    "nswe",
    "nsud",
    "weud"
  );

  /// <summary>A single horizontal cross.</summary>
  public static readonly ExOrientationScheme CanalCross = new(
    "CanalCross",
    "nswe"
  );

  /// <summary>Every declared scheme.</summary>
  public static IReadOnlyList<ExOrientationScheme> All =>
    [
      Face,
      FaceAll,
      Axis,
      AxisFlat,
      DirectedAxisFlat,
      DirectedAxis,
      PipeBend,
      CanalBend,
      PipeTee,
      CanalTee,
      PipeCross,
      CanalCross,
    ];

  /// <summary>Returns the scheme whose declared tokens are exactly <paramref name="states"/>, or null.</summary>
  public static ExOrientationScheme? Resolve(IEnumerable<string>? states) {
    if (states == null)
      return null;
    var set = new HashSet<string>(states);
    return All.FirstOrDefault(s =>
      s.Tokens.Count == set.Count && s.Tokens.All(set.Contains)
    );
  }
}
