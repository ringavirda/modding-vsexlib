#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Catalogues;
using ExpandedLib.Checks;
using ExpandedLib.Machines;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>Where a recipe is crafted.</summary>
internal enum CraftWalkClass
{
  /// <summary>The 3 x 3 hand grid.</summary>
  HandGrid,

  /// <summary>A station whose orders hold the recipe: wider or taller than the hand grid.</summary>
  GridStation,

  /// <summary>Smithing, clay forming, a process or a construction: followed, not crafted.</summary>
  Followed,
}

/// <summary>One thing an edge asks for: the loaded codes that satisfy it.</summary>
/// <param name="Label">The ingredient's code as the recipe writes it.</param>
/// <param name="Codes">Every loaded code that satisfies it.</param>
internal sealed record WalkNeed(string Label, IReadOnlyList<string> Codes);

/// <summary>One recipe of the walk's graph.</summary>
/// <param name="Kind">The registry: <c>grid</c>, <c>smithing</c> or <c>clay forming</c>.</param>
/// <param name="Name">The recipe's name, or its output when it has none.</param>
/// <param name="Class">Where the recipe is crafted.</param>
/// <param name="Needs">What the recipe asks for, one entry for each distinct ingredient.</param>
/// <param name="Output">The code the recipe makes.</param>
/// <param name="OnTrust">Whether an ingredient carries attributes the walk takes on trust.</param>
/// <param name="Grid">The grid recipe behind a grid edge; null for the others.</param>
internal sealed record WalkEdge(
  string Kind,
  string Name,
  CraftWalkClass Class,
  IReadOnlyList<WalkNeed> Needs,
  string Output,
  bool OnTrust,
  GridRecipe? Grid = null
);

internal static class CraftWalkEdges
{
  private static readonly char[] Wild = ['*', '@', '{', '('];

  /// <summary>Where <paramref name="recipe"/> is crafted: the hand grid when it fits 3 x 3, else
  /// a grid station.</summary>
  internal static CraftWalkClass ClassOf(GridRecipe recipe) =>
    recipe.Width > HandGrid.Size || recipe.Height > HandGrid.Size
      ? CraftWalkClass.GridStation
      : CraftWalkClass.HandGrid;

  /// <summary>The edges of every grid, smithing and clay forming recipe the world loaded whose
  /// output is in <paramref name="domain"/>.</summary>
  internal static List<WalkEdge> Read(
    TestWorld world,
    string domain,
    IReadOnlyList<CollectibleObject> all
  )
  {
    var cache = new Dictionary<string, WalkNeed>(StringComparer.Ordinal);
    var edges = new List<WalkEdge>();
    foreach (GridRecipe recipe in world.World.GridRecipes)
      if (
        recipe.Output?.ResolvedItemStack?.Collectible.Code is { } output
        && output.Domain == domain
      )
        edges.Add(
          Edge(
            "grid",
            recipe,
            recipe.ResolvedIngredients ?? [],
            output,
            ClassOf(recipe),
            all,
            cache,
            recipe
          )
        );
    foreach (SmithingRecipe recipe in world.SmithingRecipes)
      if (OutputOf(recipe.Output) is { } output && output.Domain == domain)
        edges.Add(
          Edge(
            "smithing",
            recipe,
            recipe.Ingredients ?? [],
            output,
            CraftWalkClass.Followed,
            all,
            cache,
            null
          )
        );
    foreach (ClayFormingRecipe recipe in world.ClayFormingRecipes)
      if (OutputOf(recipe.Output) is { } output && output.Domain == domain)
        edges.Add(
          Edge(
            "clay forming",
            recipe,
            recipe.Ingredients ?? [],
            output,
            CraftWalkClass.Followed,
            all,
            cache,
            null
          )
        );
    Dictionary<string, JArray> constructions = Constructions(all, domain);
    for (int i = 0; i < edges.Count; i++)
      if (constructions.ContainsKey(edges[i].Output))
        edges[i] = edges[i] with { Output = CoreOf(edges[i].Output) };
    edges.AddRange(Jobs(domain, all));
    edges.AddRange(Routes(domain, all));
    edges.AddRange(Products(domain, all));
    edges.AddRange(Structures(constructions, all));
    return edges;
  }

  private static string CoreOf(string structure) => "core:" + structure;

  private static readonly Regex Placeholder = new(@"\{[^}]*\}");

  // The loaded codes a pattern matches; a `{state}` placeholder matches any state.
  private static WalkNeed Pattern(
    IReadOnlyList<CollectibleObject> all,
    string pattern
  ) =>
    new(
      pattern,
      [
        .. all
          .Where(c =>
            WildcardUtil.Match(
              new AssetLocation(Placeholder.Replace(pattern, "*")),
              c.Code
            )
          )
          .Select(c => c.Code.ToString()),
      ]
    );

  private static IEnumerable<ExlibChecks.Runner> Runners(string domain) =>
    ExlibChecks.Runners().Where(r => r.Domain == domain);

  private static Dictionary<string, JArray> Constructions(
    IReadOnlyList<CollectibleObject> all,
    string domain
  )
  {
    var found = new Dictionary<string, JArray>(StringComparer.Ordinal);
    foreach (Block block in all.OfType<Block>())
      if (
        block.Code.Domain == domain
        && block
          .BlockEntityBehaviors?.FirstOrDefault(b =>
            b.Name == "ExRightClickConstructable"
          )
          ?.properties?.Token?["stages"] is JArray stages
      )
        found[block.Code.ToString()] = stages;
    return found;
  }

  private static IEnumerable<WalkEdge> Jobs(
    string domain,
    IReadOnlyList<CollectibleObject> all
  )
  {
    foreach (ExlibChecks.Runner runner in Runners(domain))
      foreach (ProcessJob job in ProcessJobRegistry.Shared.Jobs(runner.Machine))
        if (new AssetLocation(job.Output).Domain == domain)
          yield return new WalkEdge(
            "process job",
            $"{runner.Machine} job on {job.Input}",
            CraftWalkClass.Followed,
            [Pattern(all, job.Input), Pattern(all, runner.BlockCode)],
            job.Output,
            false
          );
  }

  // The thickest stopping point of a route has no edge; only its own source makes it.
  private static IEnumerable<WalkEdge> Routes(
    string domain,
    IReadOnlyList<CollectibleObject> all
  )
  {
    foreach (ExlibChecks.Runner runner in Runners(domain))
      foreach (string family in ProcessRouteRegistry.Shared.Families)
      {
        ProcessRoute? route = ProcessRouteRegistry.Shared.Route(family);
        if (route == null)
          continue;
        var stops = route
          .Stages.Where(s =>
            s.IsStoppingPoint && !s.HalfStep && s.IsAcceptedBy(runner.Machine)
          )
          .Select(s => (Code: s.Code!, s.Thickness))
          .ToList();
        foreach (var stop in stops)
        {
          var thicker = stops.Where(p => p.Thickness > stop.Thickness).ToList();
          if (
            thicker.Count == 0
            || new AssetLocation(stop.Code).Domain != domain
          )
            continue;
          var prior = thicker.MinBy(p => p.Thickness);
          yield return new WalkEdge(
            "route",
            $"{runner.Machine} stop on {stop.Code}",
            CraftWalkClass.Followed,
            [Pattern(all, prior.Code), Pattern(all, runner.BlockCode)],
            stop.Code,
            false
          );
        }
      }
  }

  private static IEnumerable<WalkEdge> Products(
    string domain,
    IReadOnlyList<CollectibleObject> all
  )
  {
    foreach (
      ExlibChecks.Declaration declared in ExlibChecks
        .Produced()
        .Where(d => d.Domain == domain && d.At != null)
    )
      foreach (string output in Pattern(all, declared.Code).Codes)
        yield return new WalkEdge(
          "machine product",
          declared.Source,
          CraftWalkClass.Followed,
          [
            Pattern(all, declared.At!),
            .. declared.From.Select(f => Pattern(all, f)),
          ],
          output,
          false
        );
  }

  private static IEnumerable<WalkEdge> Structures(
    Dictionary<string, JArray> constructions,
    IReadOnlyList<CollectibleObject> all
  )
  {
    foreach ((string code, JArray stages) in constructions)
    {
      var needs = new List<WalkNeed> { new(CoreOf(code), [CoreOf(code)]) };
      foreach (JToken stage in stages)
        if (stage["requireStacks"] is JArray stacks)
          foreach (JToken stack in stacks)
            if (
              ((string?)stack["code"] ?? (string?)stack["name"]) is { } wanted
            )
              needs.Add(Pattern(all, wanted));
      yield return new WalkEdge(
        "construction",
        code,
        CraftWalkClass.Followed,
        [.. needs.DistinctBy(n => n.Label)],
        code,
        false
      );
    }
  }

  private static AssetLocation? OutputOf(JsonItemStack? stack) =>
    stack?.ResolvedItemStack?.Collectible.Code ?? stack?.Code;

  private static WalkEdge Edge(
    string kind,
    IRecipeBase recipe,
    IEnumerable<CraftingRecipeIngredient?> ingredients,
    AssetLocation output,
    CraftWalkClass at,
    IReadOnlyList<CollectibleObject> all,
    Dictionary<string, WalkNeed> cache,
    GridRecipe? grid
  )
  {
    CraftingRecipeIngredient[] asked =
    [
      .. ingredients.OfType<CraftingRecipeIngredient>(),
    ];
    return new WalkEdge(
      kind,
      recipe.Name?.ToString() ?? output.ToString(),
      at,
      [
        .. asked
          .Select(i => Need(i, all, cache))
          .DistinctBy(n => n.Label + "|" + string.Join(",", n.Codes.Take(3))),
      ],
      output.ToString(),
      asked.Any(i => i.Attributes is { Exists: true }),
      grid
    );
  }

  private static WalkNeed Need(
    CraftingRecipeIngredient ingredient,
    IReadOnlyList<CollectibleObject> all,
    Dictionary<string, WalkNeed> cache
  )
  {
    string label =
      (
        ingredient.MatchingType == EnumRecipeMatchType.Exact
          ? ingredient.ResolvedItemStack?.Collectible.Code ?? ingredient.Code
          : ingredient.Code
      )?.ToString() ?? "?";
    string key =
      $"{ingredient.Type}|{ingredient.MatchingType}|{label}|{string.Join(",", ingredient.AllowedVariants ?? [])}|{string.Join(",", ingredient.SkipVariants ?? [])}|{ingredient.Tags}";
    if (cache.TryGetValue(key, out WalkNeed? known))
      return known;
    string[] codes;
    if (ingredient.MatchingType == EnumRecipeMatchType.Exact)
      codes = ingredient.ResolvedItemStack is { } stack
        ? [stack.Collectible.Code.ToString()]
        : [];
    else
    {
      string path = ingredient.Code?.Path ?? "*";
      int wild = path.IndexOfAny(Wild);
      string prefix = wild < 0 ? path : path[..wild];
      codes =
      [
        .. all.Where(c =>
            c.ItemClass == ingredient.Type
            && c.Code.Path.StartsWith(prefix, StringComparison.Ordinal)
            && ingredient.SatisfiesAsIngredient(new ItemStack(c), false)
          )
          .Select(c => c.Code.ToString()),
      ];
    }
    return cache[key] = new WalkNeed(label, codes);
  }
}
#endif
