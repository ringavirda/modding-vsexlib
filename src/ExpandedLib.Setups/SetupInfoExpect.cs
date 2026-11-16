using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Config;

namespace ExpandedLib.Scenarios;

/// <summary>The <c>info</c> of an <c>expect</c>: a culture-invariant regex that one line of the block's info
/// matches, or <c>{ "lang": "&lt;key&gt;" }</c>, the key exactly as the code hands it to <c>Lang.Get</c>.
/// A key is resolved through the language that wrote the text, so the harness, whose language echoes
/// keys, and a server, which writes its own language, read one setup the same way. Both layers read the
/// info here.</summary>
internal static class SetupInfoExpect {
  private static readonly Regex Placeholder = new(@"\{[^{}]*\}");

  /// <summary>The lines of an info text: split on newlines, trimmed, empty ones dropped.</summary>
  public static string[] Lines(string text) =>
    text.Split(
      '\n',
      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
    );

  /// <summary>Checks an <c>info</c> value.</summary>
  /// <exception cref="SetupFileException">The value is neither a string nor an object, the string is no
  /// regex, or the object has a key besides <c>lang</c> or no string <c>lang</c>.</exception>
  public static void Validate(JToken? info, SetupJson.Where at) {
    if (info is JObject o) {
      SetupJson.RefuseUnknown(o, at, "lang");
      SetupJson.String(o["lang"], at.In("lang"));
      return;
    }
    string pattern = SetupJson.String(info, at);
    try {
      _ = new Regex(pattern, RegexOptions.CultureInvariant);
    } catch (ArgumentException e) {
      throw at.Fail($"is not a regex: {e.Message}");
    }
  }

  /// <summary>How a failure names the info: the regex in quotes, or the key with the line patterns its
  /// template gave.</summary>
  /// <param name="unformatted">The current language's <c>Lang.GetUnformatted</c>; null takes the
  /// game's.</param>
  public static string Describe(
    JToken info,
    Func<string, string>? unformatted = null
  ) =>
    info is JObject o
      ? $"lang \"{(string)o["lang"]!}\" (lines {string.Join(" | ", Patterns(info, unformatted))})"
      : $"\"{(string)info!}\"";

  /// <summary>The regexes a run of consecutive lines must match, one per line: the regex itself, or each
  /// line of the key's unformatted template, anchored, with every <c>{...}</c> argument matching any
  /// text.</summary>
  /// <param name="info">A value <see cref="Validate"/> accepts.</param>
  /// <param name="unformatted">The current language's <c>Lang.GetUnformatted</c>; null takes the
  /// game's. A language without the key returns the key, as the game's does.</param>
  public static Regex[] Patterns(
    JToken info,
    Func<string, string>? unformatted = null
  ) {
    if (info is not JObject o)
      return [new Regex((string)info!, RegexOptions.CultureInvariant)];
    string template = (unformatted ?? Lang.GetUnformatted)((string)o["lang"]!);
    return
    [
      .. Lines(template)
        .Select(line => new Regex(
          "^"
            + string.Join(".*", Placeholder.Split(line).Select(Regex.Escape))
            + "$",
          RegexOptions.CultureInvariant
        )),
    ];
  }

  /// <summary>The first run of consecutive <paramref name="lines"/> the info's patterns match in order,
  /// joined by <c> | </c>, or null when none does.</summary>
  /// <param name="lines">The info text's <see cref="Lines"/>.</param>
  /// <param name="info">A value <see cref="Validate"/> accepts.</param>
  /// <param name="unformatted">The current language's <c>Lang.GetUnformatted</c>; null takes the
  /// game's.</param>
  public static string? Match(
    IReadOnlyList<string> lines,
    JToken info,
    Func<string, string>? unformatted = null
  ) {
    Regex[] patterns = Patterns(info, unformatted);
    if (patterns.Length == 0)
      return null;
    for (int first = 0; first + patterns.Length <= lines.Count; first++)
      if (patterns.Select((p, n) => p.IsMatch(lines[first + n])).All(m => m))
        return string.Join(" | ", lines.Skip(first).Take(patterns.Length));
    return null;
  }
}
