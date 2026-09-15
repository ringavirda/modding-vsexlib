using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that every registered block code resolves to a name in the <c>en</c> locale, matched
/// against <see cref="ICheckSource.BlockCodes"/>. An unresolved key renders as the raw key in game.
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
      foreach (JProperty prop in lang.Properties()) {
        if (prop.Name.EndsWith('*'))
          wildcardPrefixes.Add(prop.Name[..^1]);
        else
          exact.Add(prop.Name);
      }

      foreach (string code in codes) {
        string key = "block-" + code;
        if (
          !exact.Contains(key)
          && !wildcardPrefixes.Any(p =>
            key.StartsWith(p, StringComparison.Ordinal)
          )
        )
          errors.Add($"{locale}: {key}");
      }
    }
    return new CheckResult("LangCoverage", domain, errors);
  }
}
