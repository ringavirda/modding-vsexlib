using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that every code-first block definition a domain declares produced a registered block,
/// matching its code and variant groups against <see cref="ICheckSource.BlockCodes"/>.
/// </summary>
public static class DefinitionCatalogueCheck {
  /// <summary>Every def in <paramref name="domain"/> that produced no registered block, as the check's <see cref="CheckResult"/>.</summary>
  public static CheckResult Run(ICheckSource source, string domain) {
    AssetLocation[] registered = [.. source.BlockCodes];

    var errors = new List<string>();
    foreach (ExBlockDef def in source.BlockDefinitions(domain)) {
      bool resolves = Patterns(def)
        .Select(p => new AssetLocation(p))
        .Any(pattern =>
          registered.Any(code => WildcardUtil.Match(pattern, code))
        );

      if (!resolves)
        errors.Add(
          $"{domain}:{def.Code} registers no block matching '{def.Domain}:{def.Code}*' - the "
            + "def is registered but the loader produced no block for it"
        );
    }
    return new CheckResult("DefinitionCatalogue", domain, errors);
  }

  // Code patterns def.Code expands into via its variant groups; a property group stands in as a
  // wildcard.
  private static IEnumerable<string> Patterns(ExBlockDef def) {
    var groups = new List<string[]>();
    if (def.ToJson()["variantgroups"] is JArray vg)
      foreach (JToken g in vg)
        groups.Add(
          g["states"] is JArray arr ? [.. arr.Select(s => (string)s!)] : ["*"]
        );

    IEnumerable<string> codes = [$"{def.Domain}:{def.Code}"];
    foreach (string[] states in groups)
      codes = codes.SelectMany(c => states.Select(s => c + "-" + s));
    return codes;
  }
}
