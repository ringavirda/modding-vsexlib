using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that every block code a mod registers resolves to a name in the <c>en</c> locale, matched
/// against <see cref="ICheckSource.BlockCodes"/> - concrete registered codes, not a def's base code,
/// since a variant-grouped block's own base code is never placeable. An unresolved <c>en</c> key
/// renders as the raw key in game regardless of the player's own locale, since <c>en</c> is what
/// every other translation falls back to.
/// <para>
/// Parity of a non-<c>en</c> locale against <c>en</c> is opt-in, via <c>allLocales</c> on
/// the three-parameter overload: a missing Ukrainian key falls back to English rather than showing
/// raw, so it is a translation gap to track at repository build time
/// (<c>ExpandedLib.Testing.LangCoverage.MissingNames</c> runs it), not a defect that fails a server
/// load over.
/// </para>
/// </summary>
public static class LangCoverageCheck {
  private const string EnglishLocale = "en";

  /// <summary>Every unresolved <c>block-</c> name key for <paramref name="domain"/> in the <c>en</c>
  /// locale, as the check's <see cref="CheckResult"/>.</summary>
  public static CheckResult Run(ICheckSource source, string domain) =>
    Run(source, domain, allLocales: false);

  /// <summary>Every unresolved <c>block-</c> name key for <paramref name="domain"/>, as the check's
  /// <see cref="CheckResult"/>. <paramref name="allLocales"/> selects which shipped locales are
  /// walked: <see langword="false"/> checks only <c>en</c> (the in-game defect, since <c>en</c> is
  /// what every other locale falls back to); <see langword="true"/> checks every locale
  /// <see cref="ICheckSource.Lang"/> returns, the repository-build parity rule
  /// <c>ExpandedLib.Testing.LangCoverage.MissingNames</c> runs.</summary>
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
