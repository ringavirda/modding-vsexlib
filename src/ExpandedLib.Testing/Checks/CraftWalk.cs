#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>Walks a mod's whole craft chain from the codes a player takes from the world: every
/// creative-tab kind of a domain must be reached, and every recipe of the domain must be able to
/// fire, round by round.</summary>
/// <remarks>The walk starts from <see cref="GatheredSources"/> and the mod's kit, fires every
/// recipe whose ingredients are all reached, and repeats until nothing fires. Vanilla recipes are
/// not walked: a <c>game:</c> code is reached only when it is gathered or in the kit.</remarks>
public static class CraftWalk
{
  /// <summary>What one walk read and found.</summary>
  /// <param name="Summary">One line: <c>craft walk &lt;domain&gt;: K kinds, R recipes fired (E executed,
  /// G followed), U undeclared, T on trust, F failures</c>.</param>
  /// <param name="Findings">One line per failure, <c>key | what</c>, less the known rows, then one
  /// per known row that matched no failure; empty when the walk is clean.</param>
  public sealed record Result(string Summary, IReadOnlyList<string> Findings)
  {
    /// <summary>The summary line, then each finding indented.</summary>
    public override string ToString() =>
      string.Join(Environment.NewLine + "  ", [Summary, .. Findings]);
  }

  /// <summary>Walks <paramref name="domain"/>'s craft chain over the recipes <paramref name="world"/>
  /// loaded.</summary>
  /// <param name="world">A world with the domain's mod loaded by <c>LoadAssets</c> and its recipes by
  /// <see cref="TestWorld.LoadRecipes"/>.</param>
  /// <param name="domain">The domain whose kinds and recipes are walked.</param>
  /// <param name="kitFile">The kit: a JSON object from each <c>game:</c> code the player brings in (a
  /// <c>*</c> stands for any states) to <c>{ "route": "how vanilla makes it" }</c>, or to the route
  /// as a bare string. Null or a missing file is an empty kit. A row matching no loaded block or
  /// item is a finding.</param>
  /// <param name="exceptionsFile">The known failures: a JSON object from the key a finding opens
  /// with to <c>{ "why": "...", "task": "..." }</c>, or to the reason as a bare string. A row matching
  /// no finding is a finding. Null or a missing file accepts nothing.</param>
  /// <returns>The summary line and the findings.</returns>
  /// <exception cref="InvalidOperationException">A file is not a JSON object, or a row has no
  /// route or reason.</exception>
  /// <exception cref="Newtonsoft.Json.JsonException">A file is not JSON.</exception>
  public static Result Run(
    TestWorld world,
    string domain,
    string? kitFile,
    string? exceptionsFile
  )
  {
    CollectibleObject[] all =
    [
      .. world
        .World.Items.Where(i => i?.Code != null)
        .Cast<CollectibleObject>(),
      .. world.World.Blocks.Where(b => b is { Id: not 0 }),
    ];
    IReadOnlyDictionary<string, string> kit = CraftWalkFiles.Kit(kitFile);
    IReadOnlyDictionary<string, string> known = CraftWalkFiles.Known(
      exceptionsFile
    );
    var found = new List<(string Key, string Line)>();

    IReadOnlyList<GatheredSources.Source> gathered = GatheredSources.All;
    var reached = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (CollectibleObject c in all)
      if (
        gathered.Any(s => s.Type == c.ItemClass && Matches(s.Pattern, c))
        || kit.Keys.Any(k => Matches(k, c))
      )
        reached[c.Code.ToString()] = 0;
    foreach (string row in kit.Keys.Where(k => !all.Any(c => Matches(k, c))))
      found.Add((row, $"{row} | kit row matches no loaded block or item"));

    List<WalkEdge> edges = CraftWalkEdges.Read(world, domain, all);
    var fired = new bool[edges.Count];
    int rounds = 0;
    while (true)
    {
      int[] ready =
      [
        .. Enumerable
          .Range(0, edges.Count)
          .Where(i => !fired[i] && Ready(edges[i], reached)),
      ];
      if (ready.Length == 0)
        break;
      rounds++;
      foreach (int i in ready)
      {
        fired[i] = true;
        reached.TryAdd(edges[i].Output, rounds);
      }
    }

    Dictionary<string, WalkNeed> undeclared = [];
    for (int i = 0; i < edges.Count; i++)
      if (!fired[i])
        foreach (WalkNeed need in edges[i].Needs)
          if (
            Waits(need, reached)
            && new AssetLocation(need.Label).Domain == "game"
            && undeclared.TryAdd(need.Label, need)
          )
            found.Add(
              (
                need.Label,
                $"{need.Label} | asked for by {edges[i].Name}, in neither GatheredSources nor the kit file"
              )
            );

    ILookup<string, CollectibleObject> kinds = all.Where(c =>
        c.Code.Domain == domain
      )
      .ToLookup(ObtainabilityCheck.KindOf);
    string[] asked =
    [
      .. kinds
        .Where(k => k.Any(c => c.CreativeInventoryTabs is { Length: > 0 }))
        .Select(k => k.Key),
    ];
    string[] unreached =
    [
      .. asked.Where(k =>
        !kinds[k].Any(c => reached.ContainsKey(c.Code.ToString()))
      ),
    ];
    var inUnreached = new HashSet<string>(
      unreached.SelectMany(k => kinds[k]).Select(c => c.Code.ToString()),
      StringComparer.Ordinal
    );
    var kindFindings = new List<(string Key, string Line)>();
    foreach (string kind in unreached)
    {
      HashSet<string> members = [.. kinds[kind].Select(c => c.Code.ToString())];
      WalkEdge[] making = [.. edges.Where(e => members.Contains(e.Output))];
      if (making.Length == 0)
      {
        kindFindings.Add(
          (kind, $"{kind} | unreached after round {rounds}: no recipe makes it")
        );
        continue;
      }
      foreach (WalkEdge edge in making)
      {
        WalkNeed blocker = edge.Needs.First(n => Waits(n, reached));
        if (
          undeclared.ContainsKey(blocker.Label)
          || blocker.Codes.Any(inUnreached.Contains)
        )
          continue;
        kindFindings.Add(
          (
            kind,
            $"{kind} | unreached after round {rounds}: {edge.Name} waits on {blocker.Label}, which nothing reaches"
          )
        );
        break;
      }
    }
    if (kindFindings.Count == 0 && undeclared.Count == 0)
      foreach (string kind in unreached)
        kindFindings.Add(
          (
            kind,
            $"{kind} | unreached after round {rounds}: its recipes wait on each other"
          )
        );
    found.AddRange(kindFindings);

    string[] failures =
    [
      .. found.Where(f => !known.ContainsKey(f.Key)).Select(f => f.Line),
      .. known
        .Keys.Where(k => found.All(f => f.Key != k))
        .Select(k => $"{k} | known row matches no failure; remove it"),
    ];
    int count = fired.Count(f => f);
    int trust = Enumerable
      .Range(0, edges.Count)
      .Count(i => fired[i] && edges[i].OnTrust);
    int inputless = ExlibChecks
      .Produced()
      .Count(d => d.Domain == domain && d.At == null);
    return new Result(
      $"craft walk {domain}: {asked.Length} kinds, {count} recipes fired (0 executed, {count} followed), {inputless} undeclared, {trust} on trust, {failures.Length} failures",
      failures
    );
  }

  private static bool Matches(string pattern, CollectibleObject c) =>
    WildcardUtil.Match(new AssetLocation(pattern), c.Code);

  private static bool Waits(WalkNeed need, Dictionary<string, int> reached) =>
    !need.Codes.Any(reached.ContainsKey);

  private static bool Ready(WalkEdge edge, Dictionary<string, int> reached) =>
    edge.Needs.All(n => !Waits(n, reached));
}
#endif
