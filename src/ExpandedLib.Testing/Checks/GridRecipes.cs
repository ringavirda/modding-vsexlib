using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;

namespace ExpandedLib.Testing;

/// <summary>
/// <see cref="GridRecipeShapeCheck"/> and <see cref="GridRecipeCollisionCheck"/> over code-first
/// definitions read from assemblies.
/// </summary>
public static class GridRecipes {
  /// <summary>What one run read and found.</summary>
  /// <param name="Recipes">The grid recipes the domain ships.</param>
  /// <param name="Findings">One line per violation, prefixed with the name of the check that
  /// found it, e.g. <c>GridRecipeShape: </c>.</param>
  public sealed record Result(int Recipes, IReadOnlyList<string> Findings);

  /// <summary>Runs the grid recipe checks over <paramref name="domain"/>'s recipes.</summary>
  /// <param name="domain">The domain whose grid recipes are checked.</param>
  /// <param name="family">Every domain the recipes are held against, each with the assembly
  /// declaring its definitions; must hold <paramref name="domain"/>.</param>
  /// <returns>The grid recipes read and one finding per violation; no findings when
  /// clean.</returns>
  /// <exception cref="ArgumentException"><paramref name="family"/> does not hold
  /// <paramref name="domain"/>.</exception>
  public static Result Check(
    string domain,
    params (string Domain, Assembly Assembly)[] family
  ) {
    if (!family.Any(f => f.Domain == domain))
      throw new ArgumentException(
        $"the family does not hold {domain}",
        nameof(family)
      );
    var source = new AssemblyCheckSource(family);
    CheckResult[] results =
    [
      GridRecipeShapeCheck.Run(source, domain),
      GridRecipeCollisionCheck.Run(source, domain),
    ];
    return new Result(
      GridRecipeShapeCheck.Recipes(source, domain).Count(),
      [.. results.SelectMany(r => r.Errors.Select(e => $"{r.Check}: {e}"))]
    );
  }
}
