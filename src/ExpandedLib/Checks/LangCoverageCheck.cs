using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that every registered block code resolves to a name in the <c>en</c> locale, matched
/// against <see cref="ICheckSource.BlockCodes"/>. An unresolved key renders as the raw key in game.
/// A lang key carrying <c>*</c> matches as the game's lang matches it: one trailing <c>*</c> as a
/// prefix, any other as a wildcard over the whole key.
/// </summary>
public static class LangCoverageCheck {
  private const string EnglishLocale = "en";

  /// <summary>Every unresolved <c>block-</c> name key for <paramref name="domain"/> in the <c>en</c>
  /// locale, as the check's <see cref="CheckResult"/>.</summary>
  public static CheckResult Run(ICheckSource source, string domain) =>
    Run(source, domain, allLocales: false);

  /// <summary>Every unresolved <c>block-</c> name key for <paramref name="domain"/>, as the check's
  /// <see cref="CheckResult"/>. <paramref name="allLocales"/> selects <c>en</c> only, or every
  /// locale <see cref="ICheckSource.Lang"/> returns.</summary>
  public static CheckResult Run(
    ICheckSource source,
    string domain,
    bool allLocales
  ) {
    List<string> codes =
    [
      .. source
        .BlockCodes.Where(c => c.Domain == domain)
        .Select(c => c.Path)
        .Distinct()
        .OrderBy(c => c, StringComparer.Ordinal),
    ];

    var errors = new List<string>();
    foreach ((string locale, JObject lang) in source.Lang(domain)) {
      if (!allLocales && locale != EnglishLocale)
        continue;

      var exact = new HashSet<string>(StringComparer.Ordinal);
      var wildcardPrefixes = new List<string>();
      var patterns = new List<Regex>();
      foreach (JProperty prop in lang.Properties()) {
        int stars = prop.Name.Count(c => c == '*');
        if (stars == 0)
          exact.Add(prop.Name);
        else if (stars == 1 && prop.Name.EndsWith('*'))
          wildcardPrefixes.Add(prop.Name[..^1]);
        else
          patterns.Add(Pattern(prop.Name));
      }

      foreach (string code in codes) {
        string key = "block-" + code;
        if (
          !exact.Contains(key)
          && !wildcardPrefixes.Any(p =>
            key.StartsWith(p, StringComparison.Ordinal)
          )
          && !patterns.Any(p => p.IsMatch(key))
        )
          errors.Add($"{locale}: {key}");
      }
    }
    return new CheckResult("LangCoverage", domain, errors);
  }

  // A key whose `*` is not its one last character matches as the game's lang reads it, each `*`
  // standing for any run of characters over the whole key.
  private static Regex Pattern(string key) =>
    new(
      "^" + string.Join("(.*)", key.Split('*').Select(Regex.Escape)) + "$",
      RegexOptions.CultureInvariant
    );
}
