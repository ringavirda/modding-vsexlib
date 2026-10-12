using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards the lang values a mod sends to chat. Chat output is VTML: a raw <c>&lt;</c> swallows the
/// rest of the message, a bare <c>&gt;</c> (as in <c>-&gt;</c>) blanks a command result or
/// notification line, and <c>&lt;hk&gt;</c> is a handbook tag chat does not resolve.
/// </summary>
public static class ChatText {
  private static readonly Regex Sink = new(
    @"\b(?:SendLocalisedMessage|SendMessage|ShowChatMessage|TextCommandResult\s*\.\s*(?:Success|Error))\s*\(",
    RegexOptions.Compiled
  );
  private static readonly Regex Constant = new(
    "\\b(?:const|static\\s+readonly)\\s+string\\s+(\\w+)\\s*=\\s*\"([a-z0-9]+:[a-z0-9-]+)\"",
    RegexOptions.Compiled | RegexOptions.IgnoreCase
  );
  private static readonly Regex Identifier = new(
    @"\b[A-Za-z_]\w*\b",
    RegexOptions.Compiled
  );

  /// <summary>Runs the rule over the lang keys named in the arguments of a chat call
  /// (<c>SendLocalisedMessage</c>, <c>SendMessage</c>, <c>ShowChatMessage</c>,
  /// <c>TextCommandResult.Success</c> and <c>.Error</c>), as literals or as <c>const</c> /
  /// <c>static readonly</c> string fields assigned a literal key. A key built by concatenation or
  /// passed through a variable is not read.</summary>
  /// <param name="domain">The mod's domain; a literal key of another domain is skipped.</param>
  /// <param name="srcDir">The mod's source folder, read recursively for <c>*.cs</c> outside
  /// <c>bin</c> and <c>obj</c>.</param>
  /// <param name="langDir">The folder of the mod's <c>{locale}.json</c> files.</param>
  /// <returns>The distinct keys read, and one finding per locale, key and defect, as
  /// <c>{locale}: {key} ({file}): {defect}</c> with the defect <c>raw &lt;</c>, <c>raw &gt;</c> or
  /// <c>&lt;hk&gt;</c>; no findings when clean. A key a locale lacks is left to
  /// <see cref="LangCallSites"/>.</returns>
  public static (
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> Findings
  ) Check(string domain, string srcDir, string langDir) {
    var sources = new List<(string Rel, string Text)>();
    foreach (
      string path in Directory.EnumerateFiles(
        srcDir,
        "*.cs",
        SearchOption.AllDirectories
      )
    ) {
      string rel = Path.GetRelativePath(srcDir, path).Replace('\\', '/');
      if (
        rel.Contains("/bin/", StringComparison.Ordinal)
        || rel.Contains("/obj/", StringComparison.Ordinal)
        || rel.EndsWith(".g.cs", StringComparison.Ordinal)
      )
        continue;
      sources.Add((rel, File.ReadAllText(path)));
    }

    var constants = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach ((_, string text) in sources)
      foreach (Match m in Constant.Matches(text))
        constants[m.Groups[1].Value] = m.Groups[2].Value;

    var keys = new HashSet<(string File, string Key)>();
    foreach ((string rel, string text) in sources)
      foreach (Match call in Sink.Matches(text)) {
        string region = LangCallSites.ArgumentRegion(
          text,
          call.Index + call.Length - 1
        );
        foreach (Match m in LangCallSites.Literal.Matches(region))
          if (
            m.Groups[1]
              .Value.StartsWith(domain + ':', StringComparison.OrdinalIgnoreCase)
          )
            keys.Add((rel, m.Groups[1].Value));
        foreach (Match m in Identifier.Matches(region))
          if (
            constants.TryGetValue(m.Value, out string? key)
            && key.StartsWith(domain + ':', StringComparison.OrdinalIgnoreCase)
          )
            keys.Add((rel, key));
      }

    var findings = new List<string>();
    foreach (string langFile in Directory.EnumerateFiles(langDir, "*.json")) {
      string locale = Path.GetFileNameWithoutExtension(langFile);
      var values = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (
        JProperty entry in JObject
          .Parse(File.ReadAllText(langFile))
          .Properties()
      )
        values[
          entry.Name.Contains(':', StringComparison.Ordinal)
            ? entry.Name
            : domain + ':' + entry.Name
        ] = (string?)entry.Value ?? "";

      foreach ((string file, string key) in keys)
        if (values.TryGetValue(key, out string? value)) {
          if (value.Contains("<hk>", StringComparison.Ordinal))
            findings.Add($"{locale}: {key} ({file}): <hk>");
          if (value.Contains('<', StringComparison.Ordinal))
            findings.Add($"{locale}: {key} ({file}): raw <");
          if (value.Contains('>', StringComparison.Ordinal))
            findings.Add($"{locale}: {key} ({file}): raw >");
        }
    }
    return (
      [
        .. keys.Select(k => k.Key)
          .Distinct(StringComparer.Ordinal)
          .Order(StringComparer.Ordinal),
      ],
      [
        .. findings
          .Distinct(StringComparer.Ordinal)
          .OrderBy(f => f, StringComparer.Ordinal),
      ]
    );
  }
}
