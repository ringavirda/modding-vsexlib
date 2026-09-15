using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Checks;

/// <summary>Checks that no block's base code is a proper prefix of another's at a <c>-</c> boundary,
/// which would let a multiblock wildcard built from the shorter code match the longer one too.</summary>
public static class CodePrefixCollisionCheck {
  /// <summary>Every collision found for <paramref name="domain"/>, as the check's <see cref="CheckResult"/>.</summary>
  public static CheckResult Run(ICheckSource source, string domain) {
    List<string> codes =
    [
      .. source
        .BlockDefinitions(domain)
        .Select(d => d.Code)
        .Distinct()
        .OrderBy(c => c, StringComparer.Ordinal),
    ];

    var errors = new List<string>();
    foreach (string shorter in codes)
      foreach (string longer in codes) {
        if (
          shorter == longer
          || !longer.StartsWith(shorter + "-", StringComparison.Ordinal)
        )
          continue;

        errors.Add(
          $"'{domain}:{shorter}' is a prefix of '{domain}:{longer}' - a wildcard "
            + $"'{domain}:{shorter}-*' built from the shorter code also matches the longer one"
        );
      }

    return new CheckResult("CodePrefixCollision", domain, errors);
  }
}
