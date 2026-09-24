using System;
using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Checks;

namespace ExpandedLib.Testing;

/// <summary>Checks that every block code a <c>multiblockStructure</c> layout asks for is a block
/// some mod defines. Only mod-domain codes are checked.</summary>
public static class MultiblockCodes {
  /// <summary>Every mod-domain layout code across <paramref name="sources"/> that no block defined
  /// in those same sources provides, as <c>"{domain}:{block} wants '{code}'"</c> lines. An item
  /// code does not provide a cell.</summary>
  /// <exception cref="ArgumentException">Two of <paramref name="sources"/> share a
  /// domain.</exception>
  public static IReadOnlyList<string> Unresolvable(
    params (string Domain, Assembly Assembly)[] sources
  ) => Unresolvable(out _, sources);

  /// <summary>As <see cref="Unresolvable(ValueTuple{string, Assembly}[])"/>, also reporting how many
  /// layout codes were examined.</summary>
  /// <exception cref="ArgumentException">Two of <paramref name="sources"/> share a
  /// domain.</exception>
  public static IReadOnlyList<string> Unresolvable(
    out int codesChecked,
    params (string Domain, Assembly Assembly)[] sources
  ) {
    var source = new AssemblyCheckSource(sources) {
      PropertyGroupsAsWildcard = true,
    };
    codesChecked = 0;
    var missing = new List<string>();
    foreach ((string domain, _) in sources) {
      missing.AddRange(
        MultiblockCodesCheck.Run(source, domain, out int examined).Errors
      );
      codesChecked += examined;
    }
    return missing;
  }
}
