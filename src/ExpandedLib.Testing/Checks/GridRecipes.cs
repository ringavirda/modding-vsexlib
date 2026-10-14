using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;

namespace ExpandedLib.Testing;

/// <summary>
/// <see cref="GridRecipeShapeCheck"/>, <see cref="GridRecipeCollisionCheck"/> and
/// <see cref="GridOutputVariantCheck"/> over code-first definitions read from assemblies.
/// </summary>
public static class GridRecipes {
  /// <summary>What one run read and found.</summary>
  /// <param name="Recipes">The grid recipes the domain ships.</param>
  /// <param name="Findings">One line per violation, prefixed with the name of the check that
  /// found it, e.g. <c>GridRecipeShape: </c>.</param>
  public sealed record Result(int Recipes, IReadOnlyList<string> Findings);

  /// <summary>Runs the grid recipe checks over <paramref name="domain"/>'s recipes, holding them to
  /// the grids the domain declared with <c>ExlibChecks.CraftingGrid</c>.</summary>
  /// <param name="domain">The domain whose grid recipes are checked.</param>
  /// <param name="family">Every domain the recipes are held against, each with the assembly
  /// declaring its definitions; must hold <paramref name="domain"/>.</param>
  /// <returns>The grid recipes read and one finding per violation; no findings when
  /// clean.</returns>
  /// <exception cref="ArgumentException"><paramref name="family"/> does not hold
  /// <paramref name="domain"/>, or holds one domain twice.</exception>
  public static Result Check(
    string domain,
    params (string Domain, Assembly Assembly)[] family
  ) => Check(domain, null, family);

  /// <summary>Runs the grid recipe checks over <paramref name="domain"/>'s recipes, holding them to
  /// <paramref name="grids"/> for this run alone.</summary>
  /// <param name="domain">The domain whose grid recipes are checked.</param>
  /// <param name="grids">The crafting grids the run holds recipes to, each as its size in slots and
  /// its station, in place of the grids declared with <c>ExlibChecks.CraftingGrid</c>; the hand
  /// grid is always held and a size of 3 or less adds nothing. Null holds the declared grids.</param>
  /// <param name="family">Every domain the recipes are held against, each with the assembly
  /// declaring its definitions; must hold <paramref name="domain"/>.</param>
  /// <returns>The grid recipes read and one finding per violation; no findings when
  /// clean.</returns>
  /// <exception cref="ArgumentException"><paramref name="family"/> does not hold
  /// <paramref name="domain"/>, or holds one domain twice.</exception>
  public static Result Check(
    string domain,
    (int Size, string Station)[]? grids,
    params (string Domain, Assembly Assembly)[] family
  ) {
    if (!family.Any(f => f.Domain == domain))
      throw new ArgumentException(
        $"the family does not hold {domain}",
        nameof(family)
      );
    var source = new AssemblyCheckSource(family);
    IReadOnlyList<ExlibChecks.Grid>? held = grids is null
      ? null
      : ExlibChecks.Ordered(
        grids.Select(g => new ExlibChecks.Grid(g.Size, g.Station))
      );
    CheckResult[] results =
    [
      GridRecipeShapeCheck.Run(
        source,
        domain,
        held ?? ExlibChecks.Grids(domain)
      ),
      GridRecipeCollisionCheck.Run(
        source,
        domain,
        held ?? ExlibChecks.AllGrids()
      ),
      GridOutputVariantCheck.Run(source, domain),
    ];
    return new Result(
      GridRecipeShapeCheck.Recipes(source, domain).Count(),
      [.. results.SelectMany(r => r.Errors.Select(e => $"{r.Check}: {e}"))]
    );
  }
}
