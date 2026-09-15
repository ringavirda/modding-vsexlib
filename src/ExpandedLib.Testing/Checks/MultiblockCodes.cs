using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>Checks that every block code a <c>multiblockStructure</c> layout asks for is a block
/// some mod defines. Only mod-domain codes are checked.</summary>
public static class MultiblockCodes {
  /// <summary>Every mod-domain layout code across <paramref name="sources"/> that no definition in
  /// those same sources provides, as <c>"{block}: wants {code}"</c> lines.</summary>
  public static IReadOnlyList<string> Unresolvable(
    params (string Domain, Assembly Assembly)[] sources
  ) => Unresolvable(out _, sources);

  /// <summary>As <see cref="Unresolvable(ValueTuple{string, Assembly}[])"/>, also reporting how many
  /// layout codes were examined.</summary>
  public static IReadOnlyList<string> Unresolvable(
    out int codesChecked,
    params (string Domain, Assembly Assembly)[] sources
  ) {
    codesChecked = 0;
    // Every code any of these mods defines, per domain.
    var defined = new Dictionary<string, HashSet<string>>(
      StringComparer.Ordinal
    );
    var defs = new List<(string Domain, IExDef Def)>();
    foreach ((string domain, Assembly asm) in sources) {
      var codes = defined.TryGetValue(domain, out var set)
        ? set
        : defined[domain] = new HashSet<string>(StringComparer.Ordinal);
      foreach (IExDef def in DefinitionGoldens.Collect(domain, asm)) {
        // Recipe defs serialise as an array, not an object; indexed access on those throws.
        if (def.ToJson() is not JObject json)
          continue;
        defs.Add((domain, def));
        if (json["code"]?.Value<string>() is { } code)
          codes.Add(code);
      }
    }

    var missing = new List<string>();
    foreach ((string domain, IExDef def) in defs) {
      JToken? numbers = ((JObject)def.ToJson())["attributes"]
        ?["multiblockStructure"]
        ?["blockNumbers"];
      if (numbers is not JObject map)
        continue;

      foreach (var entry in map) {
        string wanted = entry.Key;
        if (
          !IsModDomainCode(wanted, out string wantDomain, out string wantPath)
        )
          continue;
        codesChecked++;
        if (
          !defined.TryGetValue(wantDomain, out var codes)
          || !AnyProvides(codes, wantPath)
        )
          missing.Add(
            $"{domain}:{((JObject)def.ToJson())["code"]} wants '{wanted}'"
          );
      }
    }
    return missing;
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
    // A domain wildcard or wildcarded path names no accountable mod.
    if (domain == "*" || path.StartsWith('@'))
      return false;
    return domain != "game";
  }

  /// <summary>Whether some defined code satisfies the layout's (possibly wildcarded) one.</summary>
  internal static bool AnyProvides(
    HashSet<string> definedCodes,
    string wantedPath
  ) {
    string want = wantedPath.TrimEnd('*');
    foreach (string code in definedCodes) {
      if (code == want)
        return true;
      // A variant of this def: the same prefix followed by '-'.
      if (
        want.StartsWith(code, StringComparison.Ordinal)
        && want.Length > code.Length
        && want[code.Length] == '-'
      )
        return true;
      // A wildcarded prefix of this def.
      if (code.StartsWith(want, StringComparison.Ordinal))
        return true;
    }
    return false;
  }
}
