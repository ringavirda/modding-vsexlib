using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>A block entity comes back from <see cref="TestWorld.Reload"/> with the tree it saved
/// and the block info it showed.</summary>
public static class ReloadLaw {
  internal const string Name = "reload";

  /// <summary>Sets every variant of each block of <paramref name="domain"/> that names an entity
  /// class at a fresh cell of <paramref name="world"/> as <see cref="InfoLaw.Run"/> does, advances
  /// 5 s of block entity time for the listeners the case registered, reloads it and compares the
  /// tree and info the reloaded entity writes with the ticked one's.</summary>
  /// <remarks>A finding is each difference <c>ExTree.Differences</c> names (a key missing, of
  /// another type, or back at the value an entity spawned at a spare cell with no placement or tick
  /// writes), info text that differs, an entity of another class or none after the reload, and a
  /// tree write, spawn or reload that throws or logs. A placement, tick or info read that throws or
  /// logs is <see cref="InfoLaw"/>'s finding; the variant is judged no further.</remarks>
  /// <param name="world">A world holding every variant of the blocks judged
  /// (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are placed.</param>
  /// <returns>The law's blocktypes, variants reloaded and findings, each keyed by the variant's
  /// code.</returns>
  public static BlockLaws.Law Run(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    IPlayer player = world.Player("reader").Player;
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      Block[] entities = [.. type.Where(b => b.EntityClass != null)];
      if (entities.Length == 0)
        continue;
      blocks++;
      foreach (Block block in entities) {
        cases++;
        var step = new BlockLaws.EntityCase(
          world,
          block,
          sites.Next(),
          findings
        );
        if (
          !step.Place(judged: false)
          || step.Entity is not { } placed
          || !step.Tick(judged: false)
          || step.Tree(placed, "saved", judged: true) is not { } saved
          || step.FreshTree(sites.Next(), judged: true) is not { } fresh
          || step.Info(player, "ticked", judged: false) is not { } before
          || !step.Reload(judged: true)
        )
          continue;
        if (
          step.Entity is not { } reloaded
          || reloaded.GetType() != placed.GetType()
        ) {
          findings.Add(
            $"{block.Code} reloaded as {step.Entity?.GetType().Name ?? "no block entity"}, "
              + $"not {placed.GetType().Name}"
          );
          continue;
        }
        if (step.Tree(reloaded, "reloaded", judged: true) is { } after)
          foreach (string line in ExTree.Differences(saved, after, fresh))
            findings.Add($"{block.Code} {line}");
        if (
          step.Info(player, "reloaded", judged: false) is { } shown
          && shown != before
        )
          findings.Add(
            $"{block.Code} info changed over the reload: \"{Flat(before)}\" became "
              + $"\"{Flat(shown)}\""
          );
      }
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  private static string Flat(string info) =>
    info.TrimEnd().Replace("\r", "").Replace("\n", " | ");
}
