using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using static ExpandedLib.Checks.GridRecipeShapeCheck;

namespace ExpandedLib.Checks;

/// <summary>
/// Checks that no two grid recipes of the family match the same input: each grid recipe of the
/// domain against every other grid recipe of every domain the source covers.
/// </summary>
/// <remarks>
/// Follows the game's matcher: a shaped pattern, trimmed, at any offset its grid allows
/// (<c>GridRecipe.Matches</c>, <c>RecipeBase.MatchesAtPosition</c>), with no mirrored match; a
/// shapeless recipe's input merged into one stack per distinct item, which its exact ingredients,
/// merged the same way, and its other ingredients, one stack each, take one to one
/// (<c>RecipeBase.MatchesShapeLess</c>); a named wildcard in one state across its slots
/// (<c>RecipeBase.GenerateRecipesForAllIngredientCombinations</c>);
/// <c>allowedVariants</c> narrowing a wildcard (<c>WildcardUtil.Match</c>). Tags, attributes and
/// <c>skipVariants</c> are not read, and a regex or tag-only ingredient overlaps its whole item
/// class. An ingredient code without a domain is in its recipe file's domain, as the game's
/// <c>RecipeLoader</c> reads it (<c>AssetLocation.Create</c>). A recipe the game refuses or never
/// matches is skipped.
/// </remarks>
public static class GridRecipeCollisionCheck {
  // Above this many combinations of named states, the names are left unbound, which can only add
  // collisions.
  private const int MaxBindings = 256;

  // Past this many slot placements, a shapeless pair is reported as colliding.
  private const int MaxPlacements = 100_000;

  private static readonly Regex AdvancedHole = new(@"\{[^}]*\}");

  /// <summary>Every pair of grid recipes matching the same input, one of them in
  /// <paramref name="domain"/>.</summary>
  /// <param name="source">The recipes to read; every domain it covers is held against
  /// <paramref name="domain"/>.</param>
  /// <param name="domain">The domain whose grid recipes are checked.</param>
  /// <returns>The check's <see cref="CheckResult"/>, named <c>GridRecipeCollision</c>, one error
  /// per colliding pair; no errors when none. A pair across two domains is reported in the run
  /// of each.</returns>
  public static CheckResult Run(ICheckSource source, string domain) {
    List<Craft> own = [.. Crafts(source, domain)];
    List<Craft> others =
    [
      .. source
        .Domains.Where(d => d != domain)
        .Distinct()
        .SelectMany(d => Crafts(source, d)),
    ];
    var errors = new List<string>();
    for (int i = 0; i < own.Count; i++)
      foreach (Craft other in own.Skip(i + 1).Concat(others))
        if (Collide(own[i], other))
          errors.Add($"{own[i].Where} and {other.Where} match the same input");
    return new CheckResult("GridRecipeCollision", domain, errors);
  }

  /// <summary>What an ingredient matches: its item class, its domain (null for any) and the code
  /// paths it matches, each a pattern whose <c>*</c> matches any text (null for any). Exact when
  /// the game matches it as one code, which a shapeless recipe merges with its equals.</summary>
  private sealed record Slot(
    string Type,
    string? Domain,
    IReadOnlyList<string>? Paths,
    bool Exact = false
  );

  /// <summary>A recipe the game loads and can match: its slots, row by row, once per combination
  /// of its named states, and the box its ingredients fill.</summary>
  private sealed record Craft(
    string Where,
    bool Shapeless,
    int Width,
    int Height,
    IReadOnlyList<Slot?[]> Variants,
    int Left,
    int Top,
    int BoxWidth,
    int BoxHeight
  );

  private static IEnumerable<Craft> Crafts(ICheckSource source, string domain) {
    foreach (GridEntry recipe in Recipes(source, domain)) {
      if (
        Prop(recipe.Json, "enabled")?.Type == JTokenType.Boolean
        && !(bool)Prop(recipe.Json, "enabled")!
      )
        continue;
      if (
        recipe.Width > GridSize
        || recipe.Height > GridSize
        || recipe.Width * recipe.Height != recipe.Cells.Count
      )
        continue;
      JObject?[] placed =
      [
        .. recipe.Cells.Select(c =>
          IsLetter(c) ? recipe.Ingredients.GetValueOrDefault(c) : null
        ),
      ];
      int[] filled =
      [
        .. Enumerable.Range(0, placed.Length).Where(i => placed[i] != null),
      ];
      if (filled.Length == 0 || filled.Length != recipe.Cells.Count(IsLetter))
        continue;

      int left = filled.Min(i => i % recipe.Width);
      int top = filled.Min(i => i / recipe.Width);
      yield return new Craft(
        recipe.Where,
        Prop(recipe.Json, "shapeless")?.Type == JTokenType.Boolean
          && (bool)Prop(recipe.Json, "shapeless")!,
        recipe.Width,
        recipe.Height,
        [
          .. Bindings(placed)
            .Select(b =>
              placed
                .Select(i => i == null ? null : SlotOf(i, b, domain))
                .ToArray()
            ),
        ],
        left,
        top,
        filled.Max(i => i % recipe.Width) - left + 1,
        filled.Max(i => i / recipe.Width) - top + 1
      );
    }
  }

  // Every combination of states the recipe's named wildcards take; one empty combination when it
  // names none, or names more than MaxBindings combinations. A name several slots share takes the
  // states of the last in pattern order (RecipeBase.GetNameToCodeMappingForBasicWildcard), and
  // stays unbound when that slot has no allowedVariants.
  private static IEnumerable<Dictionary<string, string>> Bindings(
    JObject?[] placed
  ) {
    var named = new Dictionary<string, string[]?>(StringComparer.Ordinal);
    foreach (JObject ingredient in placed.OfType<JObject>())
      if (IsNamedWildcard(ingredient, out string name))
        named[name] = Allowed(ingredient);
    Dictionary<string, string[]> names = named
      .Where(n => n.Value != null)
      .ToDictionary(n => n.Key, n => n.Value!, StringComparer.Ordinal);

    IEnumerable<Dictionary<string, string>> combinations =
    [
      new Dictionary<string, string>(StringComparer.Ordinal),
    ];
    if (names.Values.Aggregate(1L, (n, s) => n * s.Length) > MaxBindings)
      return combinations;
    foreach ((string name, string[] states) in names)
      combinations = combinations.SelectMany(c =>
        states.Select(s => new Dictionary<string, string>(c) { [name] = s })
      );
    return combinations;
  }

  private static bool IsNamedWildcard(JObject ingredient, out string name) {
    name = (string?)Prop(ingredient, "name") ?? "";
    return name.Length > 0
      && ((string?)Prop(ingredient, "code") ?? "").Contains('*');
  }

  private static string[]? Allowed(JObject ingredient) =>
    Prop(ingredient, "allowedVariants") is JArray states
      ? [.. states.Select(s => (string)s!)]
      : null;

  private static Slot SlotOf(
    JObject ingredient,
    Dictionary<string, string> binding,
    string fileDomain
  ) {
    string type = (
      (string?)Prop(ingredient, "type") ?? "block"
    ).ToLowerInvariant();
    string? code = (string?)Prop(ingredient, "code");
    if (code == null || code.StartsWith('@'))
      return new Slot(type, null, null);

    var location = AssetLocation.Create(code, fileDomain);
    string path = AdvancedHole.Replace(location.Path, "*");
    string? domain = location.Domain == "*" ? null : location.Domain;
    // A name left unbound takes any state, whatever this slot's allowedVariants.
    if (IsNamedWildcard(ingredient, out string name))
      return binding.TryGetValue(name, out string? state)
        ? new Slot(type, domain, [path.Replace("*", state)], true)
        : new Slot(type, domain, [path]);
    if (Allowed(ingredient) is { } allowed && path.Contains('*')) {
      int star = path.IndexOf('*');
      return new Slot(
        type,
        domain,
        [.. allowed.Select(s => path[..star] + s + path[(star + 1)..])]
      );
    }
    return new Slot(
      type,
      domain,
      [path],
      domain != null && !path.Contains('*')
    );
  }

  private static bool Collide(Craft a, Craft b) {
    if (!a.Shapeless && !b.Shapeless) {
      if (a.BoxWidth != b.BoxWidth || a.BoxHeight != b.BoxHeight)
        return false;
      if (!Meets(a.Left, a.Width, b.Left, b.Width))
        return false;
      if (!Meets(a.Top, a.Height, b.Top, b.Height))
        return false;
      return a.Variants.Any(x =>
        b.Variants.Any(y => SameBox(Box(a, x), Box(b, y)))
      );
    }
    return a.Variants.Any(x =>
      b.Variants.Any(y =>
        a.Shapeless
          ? Takes(Stacks(x), [.. y.OfType<Slot>()], b.Shapeless)
          : Takes(Stacks(y), [.. x.OfType<Slot>()], false)
      )
    );
  }

  // Whether two boxes starting at offsets from and other can sit at one column of the grid; a
  // recipe of width w lays its box anywhere from its offset to its offset plus 3 - w.
  private static bool Meets(int from, int width, int other, int otherWidth) =>
    Math.Max(from, other)
    <= Math.Min(from + GridSize - width, other + GridSize - otherWidth);

  private static Slot?[] Box(Craft c, Slot?[] slots) =>
    [
      .. Enumerable
        .Range(0, c.BoxHeight)
        .SelectMany(r =>
          Enumerable
            .Range(0, c.BoxWidth)
            .Select(col => slots[(c.Top + r) * c.Width + c.Left + col])
        ),
    ];

  private static bool SameBox(Slot?[] a, Slot?[] b) =>
    a.Zip(b)
      .All(p =>
        p.First == null
          ? p.Second == null
          : p.Second != null && Overlap(p.First, p.Second)
      );

  // The stacks a shapeless recipe takes: one per exact code, however many slots name it, and one
  // per other ingredient.
  private static List<Slot> Stacks(Slot?[] slots) {
    var stacks = new List<Slot>();
    foreach (Slot slot in slots.OfType<Slot>())
      if (!slot.Exact || !stacks.Any(s => s.Exact && SameItem(s, slot)))
        stacks.Add(slot);
    return stacks;
  }

  // Whether items in slots, merged by item, can be exactly the stacks: every slot's item one its
  // stack takes, every stack filled, two stacks never one item. With distinct, each slot is a
  // stack of its own recipe and fills a stack alone.
  private static bool Takes(
    IReadOnlyList<Slot> stacks,
    IReadOnlyList<Slot> slots,
    bool distinct
  ) {
    if (slots.Count < stacks.Count || (distinct && slots.Count != stacks.Count))
      return false;
    var held = new Slot?[stacks.Count];
    int placements = 0;
    bool Fill(int at) {
      if (slots.Count - at < held.Count(h => h == null))
        return false;
      if (at == slots.Count)
        return Apart(held!);
      for (int k = 0; k < stacks.Count; k++) {
        if (distinct && held[k] != null)
          continue;
        if (++placements > MaxPlacements)
          return true;
        Slot? was = held[k];
        if (Meet(was ?? stacks[k], slots[at]) is not { } met)
          continue;
        held[k] = met;
        if (Fill(at + 1))
          return true;
        held[k] = was;
      }
      return false;
    }
    return Fill(0);
  }

  // Whether no two stacks are forced to one item; a stack still matching a pattern can take an
  // item the others do not.
  private static bool Apart(Slot[] held) {
    for (int i = 0; i < held.Length; i++)
      for (int j = i + 1; j < held.Length; j++)
        if (Single(held[i]) && Single(held[j]) && SameItem(held[i], held[j]))
          return false;
    return true;
  }

  private static bool Single(Slot slot) =>
    slot.Domain != null && slot.Paths is [string path] && !path.Contains('*');

  private static bool SameItem(Slot a, Slot b) =>
    a.Type == b.Type
    && a.Domain == b.Domain
    && a.Paths is [string p]
    && b.Paths is [string q]
    && p == q;

  // What both slots match, or a wider set when two patterns meet; null when they share nothing.
  private static Slot? Meet(Slot a, Slot b) {
    if (!Overlap(a, b))
      return null;
    IReadOnlyList<string>? paths =
      a.Paths == null ? b.Paths
      : b.Paths == null ? a.Paths
      :
      [
        .. a
          .Paths.SelectMany(p =>
            b.Paths.Where(q => PatternsMeet(p, q))
              .Select(q => p.Contains('*') ? q : p)
          )
          .Distinct(),
      ];
    return new Slot(a.Type, a.Domain ?? b.Domain, paths);
  }

  private static bool Overlap(Slot a, Slot b) =>
    a.Type == b.Type
    && (a.Domain == null || b.Domain == null || a.Domain == b.Domain)
    && (
      a.Paths == null
      || b.Paths == null
      || a.Paths.Any(p => b.Paths.Any(q => PatternsMeet(p, q)))
    );

  /// <summary>Whether some text matches both <paramref name="a"/> and <paramref name="b"/>, each
  /// a pattern whose <c>*</c> matches any text, itself included.</summary>
  internal static bool PatternsMeet(string a, string b) {
    var memo = new bool?[a.Length + 1, b.Length + 1];
    bool Meet(int i, int j) {
      if (memo[i, j] is bool known)
        return known;
      bool meets;
      if (i == a.Length && j == b.Length)
        meets = true;
      else if (i < a.Length && a[i] == '*')
        meets = Meet(i + 1, j) || (j < b.Length && Meet(i, j + 1));
      else if (j < b.Length && b[j] == '*')
        meets = Meet(i, j + 1) || (i < a.Length && Meet(i + 1, j));
      else
        meets =
          i < a.Length && j < b.Length && a[i] == b[j] && Meet(i + 1, j + 1);
      memo[i, j] = meets;
      return meets;
    }
    return Meet(0, 0);
  }
}
