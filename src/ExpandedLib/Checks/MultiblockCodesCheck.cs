using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that every block code a <c>multiblockStructure</c> layout asks for is a block some mod
/// registers, matched against <see cref="ICheckSource.BlockCodes"/>. Only mod-domain codes are
/// checked; a <c>game:</c> code or an alternation group is vanilla's to answer for.
/// </summary>
public static class MultiblockCodesCheck {
  /// <summary>Every unresolvable layout code in <paramref name="domain"/>'s defs, as the check's <see cref="CheckResult"/>.</summary>
  public static CheckResult Run(ICheckSource source, string domain) {
    var codesByDomain = new Dictionary<string, HashSet<string>>(
      StringComparer.Ordinal
    );
    foreach (AssetLocation code in source.BlockCodes) {
      var set = codesByDomain.TryGetValue(code.Domain, out var s)
        ? s
        : codesByDomain[code.Domain] = new HashSet<string>(
          StringComparer.Ordinal
        );
      set.Add(code.Path);
    }

    var errors = new List<string>();
    foreach (ExBlockDef def in source.BlockDefinitions(domain)) {
      if (
        def.ToJson()["attributes"]?["multiblockStructure"]?["blockNumbers"]
        is not JObject numbers
      )
        continue;

      foreach (var entry in numbers) {
        if (
          !IsModDomainCode(
            entry.Key,
            out string wantDomain,
            out string wantPath
          )
        )
          continue;

        if (
          !codesByDomain.TryGetValue(wantDomain, out var codes)
          || !AnyProvides(codes, wantPath)
        )
          errors.Add($"{domain}:{def.Code} wants '{entry.Key}'");
      }
    }
    return new CheckResult("MultiblockCodes", domain, errors);
  }

  // An alternation group or a domainless/vanilla code is out of scope.
  internal static bool IsModDomainCode(
    string code,
    out string domain,
    out string path
  ) {
    domain = path = "";
    if (code.StartsWith('@'))
      return false;
    int colon = code.IndexOf(':');
    if (colon <= 0)
      return false;
    domain = code[..colon];
    path = code[(colon + 1)..];
    if (domain == "*" || path.StartsWith('@'))
      return false;
    return domain != "game";
  }

  // Whether a registered code satisfies the layout's code, matched segment by '-'-delimited segment.
  // A `*` segment matches any one segment; a trailing `*` matches by prefix within its segment.
  internal static bool AnyProvides(
    HashSet<string> registeredCodes,
    string wantedPath
  ) {
    string[] want = wantedPath.Split('-');
    foreach (string code in registeredCodes) {
      if (SegmentsMatch(want, code.Split('-')))
        return true;
    }
    return false;
  }

  private static bool SegmentsMatch(string[] want, string[] code) {
    int common = Math.Min(want.Length, code.Length);
    for (int i = 0; i < common; i++) {
      string w = want[i];
      if (w == "*")
        continue;
      if (w.EndsWith('*')) {
        if (!code[i].StartsWith(w[..^1], StringComparison.Ordinal))
          return false;
      } else if (w != code[i]) {
        return false;
      }
    }
    return true;
  }
}
