using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks every grid recipe in a domain against the pattern the game reads: each ingredient key
/// is placed by <c>ingredientPattern</c>, each pattern letter has an ingredient, the pattern
/// fills its declared width and height, and the grid is at most 3x3.
/// </summary>
/// <remarks>A key the pattern never places is never consumed and never shown, so two recipes
/// differing only in it take the same input. A letter with no key or a pattern of another length
/// than width times height fails the recipe at load (<c>GridRecipe.Resolve</c>), and a grid wider
/// or taller than the crafting grid never matches (<c>GridRecipe.Matches</c>). A grid recipe is one under <c>recipes/grid/</c>; its properties
/// are read case-insensitively, as the game's loader reads them.</remarks>
public static class GridRecipeShapeCheck {
  /// <summary>The crafting grid's width and height, in slots.</summary>
  internal const int GridSize = 3;

  /// <summary>Every key, letter, pattern length or grid size in <paramref name="domain"/>'s grid
  /// recipes the game cannot place.</summary>
  /// <param name="source">The recipes to read.</param>
  /// <param name="domain">The domain whose grid recipes are checked.</param>
  /// <returns>The check's <see cref="CheckResult"/>, named <c>GridRecipeShape</c>, one error per
  /// unplaced key, keyless letter, pattern not filling its grid and oversized grid; no errors when
  /// none.</returns>
  public static CheckResult Run(ICheckSource source, string domain) {
    var errors = new List<string>();
    foreach (GridEntry recipe in Recipes(source, domain)) {
      foreach (
        string key in recipe.Ingredients.Keys.Where(k =>
          !recipe.Cells.Contains(k)
        )
      )
        errors.Add(
          $"{recipe.Where}: key {key} is not in the pattern {recipe.RawPattern}"
        );
      foreach (string letter in recipe.Cells.Where(IsLetter).Distinct())
        if (!recipe.Ingredients.ContainsKey(letter))
          errors.Add($"{recipe.Where}: letter {letter} has no key");
      if (recipe.Width * recipe.Height != recipe.Cells.Count)
        errors.Add(
          $"{recipe.Where}: pattern {recipe.RawPattern} has {recipe.Cells.Count} slots, "
            + $"not {recipe.Width}x{recipe.Height}"
        );
      if (recipe.Width > GridSize || recipe.Height > GridSize)
        errors.Add(
          $"{recipe.Where}: grid {recipe.Width}x{recipe.Height} is larger than "
            + $"{GridSize}x{GridSize}"
        );
    }
    return new CheckResult("GridRecipeShape", domain, errors);
  }

  /// <summary>One grid recipe as the game resolves it.</summary>
  /// <param name="Where">The recipe's file, its position among the file's recipes from 0, and its
  /// name, or its output code when it has none: <c>mod:recipes/grid/a.json#1 (Crate)</c>.</param>
  /// <param name="At">The recipe's file and position alone:
  /// <c>mod:recipes/grid/a.json#1</c>.</param>
  /// <param name="Json">The recipe object.</param>
  /// <param name="RawPattern">The pattern as written, or an empty string.</param>
  /// <param name="Cells">The pattern with row separators removed, one entry per slot, row by
  /// row.</param>
  /// <param name="Width">The declared width in slots; 3 when not declared.</param>
  /// <param name="Height">The declared height in slots; 3 when not declared.</param>
  /// <param name="Ingredients">Every key of <c>ingredients</c>, to its ingredient.</param>
  internal sealed record GridEntry(
    string Where,
    string At,
    JObject Json,
    string RawPattern,
    IReadOnlyList<string> Cells,
    int Width,
    int Height,
    IReadOnlyDictionary<string, JObject> Ingredients
  );

  /// <summary>Every grid recipe <paramref name="domain"/> ships, in source order.</summary>
  internal static IEnumerable<GridEntry> Recipes(
    ICheckSource source,
    string domain
  ) {
    var positions = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach ((AssetLocation file, JObject json) in source.Recipes(domain)) {
      string at = file.ToShortString();
      // A recipe keeps its place in its file's array when the source leaves others out.
      int position = json.Parent is JArray siblings
        ? siblings.Take(siblings.IndexOf(json)).Count(t => t is JObject)
        : positions[at] = positions.GetValueOrDefault(at, -1) + 1;
      if (!file.Path.StartsWith("recipes/grid/", StringComparison.Ordinal))
        continue;
      string pattern = (string?)Prop(json, "ingredientPattern") ?? "";
      var keys = new Dictionary<string, JObject>(StringComparer.Ordinal);
      if (Prop(json, "ingredients") is JObject ingredients)
        foreach (JProperty key in ingredients.Properties())
          keys[key.Name] = key.Value as JObject ?? [];
      string label =
        (string?)Prop(json, "name")
        ?? (string?)(Prop(json, "output") as JObject)?["code"]
        ?? "(unnamed)";
      yield return new GridEntry(
        $"{at}#{position} ({label})",
        $"{at}#{position}",
        json,
        pattern,
        [
          .. pattern
            .Where(c => c is not (',' or '\t' or '\r' or '\n'))
            .Select(c => c.ToString()),
        ],
        (int?)Prop(json, "width") ?? GridSize,
        (int?)Prop(json, "height") ?? GridSize,
        keys
      );
    }
  }

  /// <summary>Whether a pattern slot names an ingredient: anything but <c>_</c> and a
  /// space.</summary>
  internal static bool IsLetter(string cell) => cell is not ("_" or " ");

  /// <summary><paramref name="name"/>'s value on <paramref name="json"/>, matched
  /// case-insensitively; null when absent.</summary>
  internal static JToken? Prop(JObject json, string name) =>
    json.GetValue(name, StringComparison.OrdinalIgnoreCase);
}
