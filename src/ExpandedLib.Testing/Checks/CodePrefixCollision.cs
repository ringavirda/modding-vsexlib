using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Checks;

namespace ExpandedLib.Testing;

/// <summary>
/// Checks that no block's base code is a proper prefix of another's at a <c>-</c> boundary.
/// </summary>
public static class CodePrefixCollision {
  /// <summary>Every pair of distinct base codes where one is a prefix of the other at a <c>-</c>
  /// boundary.</summary>
  /// <returns>Empty when none.</returns>
  public static IReadOnlyList<string> Collisions(string domain, Assembly asm) =>
    CodePrefixCollisionCheck
      .Run(new AssemblyCheckSource((domain, asm)), domain)
      .Errors;
}
