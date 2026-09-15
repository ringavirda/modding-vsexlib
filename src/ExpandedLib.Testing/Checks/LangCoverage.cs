using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Checks that every block code a mod registers resolves to a name in every locale it ships.
/// Coverage is checked against concrete codes, not base codes.
/// </summary>
public static class LangCoverage {
  /// <summary>Every <c>(locale, code)</c> with no resolving <c>block-</c> name key.</summary>
  /// <returns>Empty when full coverage.</returns>
  public static IReadOnlyList<string> MissingNames(
    string domain,
    Assembly asm,
    string langDir
  ) {
    var source = new AssemblyCheckSource([(domain, asm)], [(domain, langDir)]);
    return LangCoverageCheck.Run(source, domain, allLocales: true).Errors;
  }

  /// <summary>Every <c>blockdesc-</c> key that matches no live block code, as
  /// <c>"{locale}: {key}"</c>.</summary>
  public static IReadOnlyList<string> OrphanedDescriptions(
    string domain,
    Assembly asm,
    string langDir
  ) {
    var codes = DefinitionCodes
      .ForDomain(domain, asm)
      .Select(r => r.Code[(domain.Length + 1)..])
      .ToList();

    var orphans = new List<string>();
    foreach (
      string langFile in Directory
        .EnumerateFiles(langDir, "*.json")
        .OrderBy(f => f)
    ) {
      var lang = JObject.Parse(File.ReadAllText(langFile));
      string locale = Path.GetFileNameWithoutExtension(langFile);

      foreach (var prop in lang.Properties()) {
        if (!prop.Name.StartsWith("blockdesc-", StringComparison.Ordinal))
          continue;

        string target = prop.Name["blockdesc-".Length..];
        bool wildcard = target.EndsWith('*');
        string stem = wildcard ? target[..^1] : target;

        bool hit = wildcard
          ? codes.Any(c => c.StartsWith(stem, StringComparison.Ordinal))
          : codes.Any(c => c == stem);

        if (!hit)
          orphans.Add($"{locale}: {prop.Name}");
      }
    }
    return orphans;
  }

  /// <summary>The distinct base codes behind <see cref="MissingNames"/>.</summary>
  public static IReadOnlyList<string> MissingBaseCodes(
    IEnumerable<string> failures
  ) =>
    [
      .. failures
        .Select(f => f[(f.IndexOf("block-", StringComparison.Ordinal) + 6)..])
        .Select(c => c.Split('-')[0])
        .Distinct()
        .OrderBy(c => c, StringComparer.Ordinal),
    ];
}
