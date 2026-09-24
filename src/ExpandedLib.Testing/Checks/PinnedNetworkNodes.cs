using System;
using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Checks;

namespace ExpandedLib.Testing;

/// <summary>Checks that no shipped layout pins the orientation of a network node; the sanctioned way
/// to state what a layout wants is the <c>Connector</c> mark.</summary>
public static class PinnedNetworkNodes {
  /// <summary>Every pinned network node across <paramref name="sources"/>, as one line each, also
  /// reporting how many pinned codes were examined.</summary>
  /// <exception cref="ArgumentException">Two of <paramref name="sources"/> share a
  /// domain.</exception>
  public static IReadOnlyList<string> Violations(
    out int codesChecked,
    params (string Domain, Assembly Assembly)[] sources
  ) {
    var source = new AssemblyCheckSource(sources);
    codesChecked = 0;
    var problems = new List<string>();
    foreach ((string domain, _) in sources) {
      problems.AddRange(
        PinnedNetworkNodesCheck.Run(source, domain, out int examined).Errors
      );
      codesChecked += examined;
    }
    return problems;
  }
}
