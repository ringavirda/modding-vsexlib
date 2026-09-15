using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Checks that every lang key a mod's own source hands to <c>Lang.Get</c>, <c>ActionLangCode</c>
/// or <c>SendIngameError</c> exists in every locale it ships. Only literal keys are checked; a
/// key built by concatenation is skipped.
/// </summary>
public static class LangCallSites {
  #region Call-site scanning

  // The three forms that name a key; literals are read from the region that follows (a balanced
  // argument list, or an assignment's right-hand side).
  private static readonly Regex LangCall = new(
    @"\bLang\.Get(?:IfExists|Matching)?\s*\(",
    RegexOptions.Compiled
  );
  private static readonly Regex ActionLangCode = new(
    @"\bActionLangCode\s*[=:]\s*",
    RegexOptions.Compiled
  );
  private static readonly Regex ErrorCall = new(
    @"\bSendIngameError\s*\(",
    RegexOptions.Compiled
  );

  // Case-insensitive on purpose: a key is lower-case by convention, and a capital is a typo.
  private static readonly Regex Literal = new(
    "\"([a-z0-9]+:[a-z0-9-]+)\"",
    RegexOptions.Compiled | RegexOptions.IgnoreCase
  );
  private static readonly Regex BareLiteral = new(
    "\"([a-z0-9-]+)\"",
    RegexOptions.Compiled | RegexOptions.IgnoreCase
  );

  /// <summary>The source text between an opening parenthesis and the one that closes it. String
  /// contents are not parsed.</summary>
  private static string ArgumentRegion(string source, int openParen) {
    int depth = 0;
    for (int i = openParen; i < source.Length; i++) {
      if (source[i] == '(')
        depth++;
      else if (source[i] == ')' && --depth == 0)
        return source[(openParen + 1)..i];
    }
    return source[(openParen + 1)..];
  }

  /// <summary>The right-hand side of an assignment starting at <paramref name="start"/>:
  /// everything up to the first <c>,</c> or <c>;</c> outside brackets, or the bracket that closes
  /// the enclosing list.</summary>
  private static string AssignedRegion(string source, int start) {
    int depth = 0;
    for (int i = start; i < source.Length; i++) {
      char c = source[i];
      if (c is '(' or '[' or '{')
        depth++;
      else if (c is ')' or ']' or '}') {
        if (depth == 0)
          return source[start..i];
        depth--;
      } else if (depth == 0 && c is ',' or ';')
        return source[start..i];
    }
    return source[start..];
  }

  /// <summary>A literal glued to a neighbour with <c>+</c> is one piece of a key, not a key.</summary>
  private static bool IsConcatenated(string region, Match literal) {
    string before = region[..literal.Index].TrimEnd();
    string after = region[(literal.Index + literal.Length)..].TrimStart();
    return before.EndsWith('+') || after.StartsWith('+');
  }

  /// <summary>The keys in <paramref name="region"/>.</summary>
  /// <param name="domain">Filters domain-qualified literals to this domain; <c>null</c> takes
  /// every domain.</param>
  private static IEnumerable<string> KeysIn(
    string region,
    Regex literals,
    string? domain
  ) {
    foreach (Match m in literals.Matches(region)) {
      if (IsConcatenated(region, m))
        continue;
      string value = m.Groups[1].Value;
      if (
        literals == Literal
        && domain != null
        && !value.StartsWith(domain + ':', StringComparison.OrdinalIgnoreCase)
      )
        continue;
      yield return value;
    }
  }

  /// <summary>Every <c>(file, key)</c> a mod's source names, normalised the way the lang cache
  /// holds it.</summary>
  public static IReadOnlyList<(string File, string Key)> Keys(
    string domain,
    string srcDir
  ) {
    string root = srcDir;
    var found = new List<(string, string)>();
    foreach (
      string path in Directory.EnumerateFiles(
        root,
        "*.cs",
        SearchOption.AllDirectories
      )
    ) {
      string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
      if (
        rel.Contains("/bin/", StringComparison.Ordinal)
        || rel.Contains("/obj/", StringComparison.Ordinal)
        || rel.EndsWith(".g.cs", StringComparison.Ordinal)
      )
        continue;

      string source = File.ReadAllText(path);
      foreach (Match call in LangCall.Matches(source)) {
        string region = ArgumentRegion(source, call.Index + call.Length - 1);
        foreach (string key in KeysIn(region, Literal, domain))
          found.Add((rel, key));
      }
      foreach (Match call in ActionLangCode.Matches(source)) {
        string region = AssignedRegion(source, call.Index + call.Length);
        foreach (string key in KeysIn(region, Literal, null))
          found.Add((rel, key));
      }
      foreach (Match call in ErrorCall.Matches(source)) {
        string region = ArgumentRegion(source, call.Index + call.Length - 1);
        // The two-argument overload carries its own text and never reads a lang file.
        if (TopLevelCommas(region) > 0)
          continue;
        foreach (string code in KeysIn(region, BareLiteral, domain))
          found.Add((rel, "game:ingameerror-" + code));
      }
    }
    return found;
  }

  private static int TopLevelCommas(string region) {
    int depth = 0,
      commas = 0;
    foreach (char c in region) {
      if (c is '(' or '[')
        depth++;
      else if (c is ')' or ']')
        depth--;
      else if (c == ',' && depth == 0)
        commas++;
    }
    return commas;
  }

  #endregion

  #region Coverage

  /// <summary>Every <c>(locale, file, key)</c> a call site names that the locale does not
  /// carry.</summary>
  /// <returns>Empty when every hand-written key resolves.</returns>
  public static IReadOnlyList<string> Unresolvable(
    string domain,
    string srcDir,
    string langDir
  ) {
    var keys = Keys(domain, srcDir);

    var failures = new List<string>();
    foreach (
      string langFile in Directory
        .EnumerateFiles(langDir, "*.json")
        .OrderBy(f => f)
    ) {
      string locale = Path.GetFileNameWithoutExtension(langFile);
      var shipped = new HashSet<string>(StringComparer.Ordinal);
      foreach (
        JProperty entry in JObject
          .Parse(File.ReadAllText(langFile))
          .Properties()
      )
        shipped.Add(
          entry.Name.Contains(':', StringComparison.Ordinal)
            ? entry.Name
            : domain + ':' + entry.Name
        );

      foreach ((string file, string key) in keys)
        if (!shipped.Contains(key))
          failures.Add($"{locale}: {key} ({file})");
    }
    return
    [
      .. failures
        .Distinct(StringComparer.Ordinal)
        .OrderBy(f => f, StringComparer.Ordinal),
    ];
  }

  #endregion
}
