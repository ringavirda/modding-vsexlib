#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Machines;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>Crafts the grid recipes the walk reached: at the hand grid, and at the placed craft
/// stations for the recipes the hand grid cannot hold.</summary>
internal static class CraftWalkRuns
{
  private const int Gap = 32;

  private sealed record Station(
    string Name,
    BlockEntity Entity,
    IReadOnlyList<CraftOrder> Orders
  );

  /// <summary>Crafts every fired grid edge once and appends a failure for each craft the matcher
  /// or the station refuses, for each set member the hand grid takes and for each station recipe no
  /// placed station holds.</summary>
  /// <param name="world">The loaded world.</param>
  /// <param name="domain">The domain whose blocks are placed as stations.</param>
  /// <param name="edges">The walk's edges.</param>
  /// <param name="round">For each edge, the round it fired in; 0 for one that did not.</param>
  /// <param name="reached">The round each code was reached in.</param>
  /// <param name="all">Every loaded item and block.</param>
  /// <param name="found">The failures, as key and line.</param>
  /// <param name="crafted">Receives each craft that succeeded, with where it was made.</param>
  /// <returns>For each edge, whether it was crafted without a refusal.</returns>
  internal static bool[] Execute(
    TestWorld world,
    string domain,
    IReadOnlyList<WalkEdge> edges,
    int[] round,
    Dictionary<string, int> reached,
    IReadOnlyList<CollectibleObject> all,
    List<(string Key, string Line)> found,
    List<CraftWalk.CraftedEdge> crafted
  )
  {
    var executed = new bool[edges.Count];
    using SetupScene scene = SetupScene.Over(world, "craft-walk");
    IReadOnlyList<CraftOrder> hand = HandGrid.Orders(world.World);
    List<Station> stations = Place(scene, world, domain, all);
    var cache = new Dictionary<string, WalkNeed>(StringComparer.Ordinal);
    for (int i = 0; i < edges.Count; i++)
    {
      if (round[i] == 0 || edges[i].Grid is not { } recipe)
        continue;
      WalkEdge edge = edges[i];
      bool inHand = hand.Any(o => o.Grid == recipe);
      if (edge.Class == CraftWalkClass.GridStation && inHand)
        found.Add(
          (
            edge.Output,
            $"{edge.Output} | hand grid takes {edge.Name}, a set member wider or taller than the grid"
          )
        );
      CraftOrder? order = null;
      Station? at = null;
      if (edge.Class == CraftWalkClass.HandGrid)
        order = hand.FirstOrDefault(o => o.Grid == recipe);
      else
        foreach (Station station in stations)
          if (
            station.Orders.FirstOrDefault(o => Holds(o, recipe, edge)) is
            { } held
          )
          {
            order = held;
            at = station;
            break;
          }
      if (order == null)
      {
        if (edge.Class == CraftWalkClass.GridStation)
          found.Add(
            (
              edge.Output,
              $"{edge.Output} | no placed station holds {edge.Name}"
            )
          );
        continue;
      }
      try
      {
        scene.CraftOnce(
          edge.Name,
          at?.Entity,
          order,
          Bill(order, all, cache, reached, round[i])
        );
        executed[i] = true;
        crafted.Add(
          new CraftWalk.CraftedEdge(
            edge.Output,
            edge.Name,
            at?.Name ?? CraftWalk.HandGridName
          )
        );
      }
      catch (InvalidOperationException e)
      {
        found.Add(
          (
            edge.Output,
            at == null
              ? $"{edge.Output} | does not match: {e.Message}"
              : $"{edge.Output} | station refuses: {e.Message}"
          )
        );
      }
    }
    return executed;
  }

  private static bool Holds(
    CraftOrder order,
    GridRecipe recipe,
    WalkEdge edge
  ) =>
    order.Grid == recipe
    || (
      order.Grid == null
      && order.Recipe == edge.Name
      && order.Product.Collectible.Code.ToString() == edge.Output
    );

  // Places one of each block of the domain that has a block entity, a cell of its own apiece, and
  // keeps those whose entity is a craft station.
  private static List<Station> Place(
    SetupScene scene,
    TestWorld world,
    string domain,
    IReadOnlyList<CollectibleObject> all
  )
  {
    var stations = new List<Station>();
    int n = 0;
    foreach (
      Block block in all.OfType<Block>()
        .Where(b => b.Code.Domain == domain && b.EntityClass != null)
    )
    {
      var at = new BlockPos((n++ * Gap) + Gap, 4, Gap);
      try
      {
        scene.PlaceAt(block.Code.ToString(), block, at);
      }
      catch (InvalidOperationException)
      {
        continue;
      }
      if (world.GetBlockEntity(at) is ICraftStation craft)
        stations.Add(
          new Station(
            block.Code.ToString(),
            (BlockEntity)craft,
            craft.Orders(world.World)
          )
        );
    }
    return stations;
  }

  // The first variant of each wildcard ingredient that was reached before the edge fired, laid by
  // input name.
  private static JObject Bill(
    CraftOrder order,
    IReadOnlyList<CollectibleObject> all,
    Dictionary<string, WalkNeed> cache,
    Dictionary<string, int> reached,
    int round
  )
  {
    var with = new JObject();
    foreach (CraftInput input in order.Inputs)
    {
      if (input.Ingredient.MatchingType == EnumRecipeMatchType.Exact)
        continue;
      string? pick = CraftWalkEdges
        .Need(input.Ingredient, all, cache)
        .Codes.FirstOrDefault(c =>
          reached.TryGetValue(c, out int r) && r < round
        );
      if (pick != null)
        with[input.Name] = pick;
    }
    return with;
  }
}
#endif
