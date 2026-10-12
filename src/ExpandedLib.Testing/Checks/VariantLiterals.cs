using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards the string literals a mod's source compares with a variant value: <c>Variant["side"] ==
/// "north"</c> is never true when the definitions declare <c>side</c> as the letters, and nothing
/// else notices.
/// </summary>
public static class VariantLiterals {
  private const string Key = @"Variant\s*\??\s*\[\s*""(?<key>\w+)""\s*\]\s*\)?";
  private const string Literals = @"""[^""]*""(?:\s+or\s+""[^""]*"")*";
  private static readonly Regex Literal = new(
    @"""([^""]*)""",
    RegexOptions.Compiled
  );
  private static readonly Regex Equality = new(
    Key + @"\s*[!=]=\s*(?<lits>""[^""]*"")",
    RegexOptions.Compiled
  );
  private static readonly Regex Reversed = new(
    @"(?<lits>""[^""]*"")\s*[!=]=\s*[\w.?]*" + Key,
    RegexOptions.Compiled
  );
  private static readonly Regex Pattern = new(
    Key + @"\s+is\s+(?:not\s+)?(?<lits>" + Literals + ")",
    RegexOptions.Compiled
  );
  private static readonly Regex Switch = new(
    Key + @"\s*switch\s*\{",
    RegexOptions.Compiled
  );
  private static readonly Regex Arm = new(
    "(?<lits>" + Literals + @")\s*=>",
    RegexOptions.Compiled
  );

  /// <summary>What one run read and found.</summary>
  /// <param name="Comparisons">The variant-against-literal comparisons read: an <c>==</c> or
  /// <c>!=</c> either way round, an <c>is</c> pattern, and each arm of a <c>switch</c>.</param>
  /// <param name="Findings">One line per file, variant key and literal, as
  /// <c>{file}: Variant["{key}"] against "{literal}": {reason}</c>.</param>
  public sealed record Result(int Comparisons, IReadOnlyList<string> Findings);

  /// <summary>Runs the rule over the comparisons in <paramref name="srcDir"/> against the variant
  /// groups of <paramref name="defs"/>. A comparison is read when the indexer's key is a string
  /// literal and the compared value is a string literal; a lookup through a variable or
  /// <c>TryGetValue</c> is not read. A group sourced from a worldproperty accepts any
  /// state.</summary>
  /// <param name="defs">The definitions whose <c>variantgroups</c> declare the states.</param>
  /// <param name="srcDir">The source folder, read recursively for <c>*.cs</c> outside <c>bin</c>
  /// and <c>obj</c>; a line that is only a <c>//</c> comment is skipped.</param>
  /// <returns>The comparisons read and one finding per key no definition declares or state its
  /// group lacks; no findings when clean.</returns>
  /// <exception cref="DirectoryNotFoundException"><paramref name="srcDir"/> does not
  /// exist.</exception>
  public static Result Check(IEnumerable<IExDef> defs, string srcDir) {
    var declared = new Dictionary<string, HashSet<string>?>(
      StringComparer.Ordinal
    );
    foreach (IExDef def in defs)
      if (
        def.ToJson() is JObject { } json
        && json["variantgroups"] is JArray groups
      )
        foreach (JToken group in groups) {
          string? code = (string?)group["code"];
          if (code == null)
            continue;
          if (group["states"] is not JArray states) {
            declared[code] = null;
            continue;
          }
          if (declared.TryGetValue(code, out HashSet<string>? set))
            set?.UnionWith(states.Select(s => (string)s!));
          else
            declared[code] = [.. states.Select(s => (string)s!)];
        }

    int comparisons = 0;
    var findings = new HashSet<string>(StringComparer.Ordinal);
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
        || rel.StartsWith("bin/", StringComparison.Ordinal)
        || rel.StartsWith("obj/", StringComparison.Ordinal)
        || rel.EndsWith(".g.cs", StringComparison.Ordinal)
      )
        continue;
      string text = string.Join(
        "\n",
        File.ReadLines(path)
          .Select(l =>
            l.TrimStart().StartsWith("//", StringComparison.Ordinal) ? "" : l
          )
      );

      void Compare(string key, string lits) {
        foreach (Match lit in Literal.Matches(lits)) {
          comparisons++;
          string value = lit.Groups[1].Value;
          string head = $"{rel}: Variant[\"{key}\"] against \"{value}\": ";
          if (!declared.TryGetValue(key, out HashSet<string>? states))
            findings.Add(head + "no definition declares that variant group");
          else if (states != null && !states.Contains(value))
            findings.Add(
              head
                + $"not a state of {key}, which declares {string.Join(", ", states.Order(StringComparer.Ordinal))}"
            );
        }
      }

      foreach (Regex rule in new[] { Equality, Reversed, Pattern })
        foreach (Match m in rule.Matches(text))
          Compare(m.Groups["key"].Value, m.Groups["lits"].Value);
      foreach (Match m in Switch.Matches(text)) {
        int depth = 1,
          i = m.Index + m.Length;
        for (; i < text.Length && depth > 0; i++)
          depth +=
            text[i] == '{' ? 1
            : text[i] == '}' ? -1
            : 0;
        foreach (Match arm in Arm.Matches(text[(m.Index + m.Length)..i]))
          Compare(m.Groups["key"].Value, arm.Groups["lits"].Value);
      }
    }
    return new Result(comparisons, [.. findings.Order(StringComparer.Ordinal)]);
  }
}
