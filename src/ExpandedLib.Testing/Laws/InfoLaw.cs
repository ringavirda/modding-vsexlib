using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>A block entity's <see cref="BlockEntity.GetBlockInfo"/> throws nothing and logs
/// nothing, read fresh, after a tick and after a reload.</summary>
public static class InfoLaw {
  internal const string Name = "info";

  /// <summary>Sets every variant of each block of <paramref name="domain"/> that names an entity
  /// class at a fresh cell of <paramref name="world"/> through the accessor's <c>SetBlock</c>, runs
  /// its <see cref="Block.OnBlockPlaced"/>, and reads its entity's info for a test player three
  /// times: fresh, after 5 s of block entity time for the listeners the case registered, and after
  /// <see cref="TestWorld.Reload"/>.</summary>
  /// <remarks>A finding is a step that throws or writes any log entry (placement, tick, reload and
  /// each info read), and a placement that raises no entity. A logged entry is declared expected to
  /// <see cref="TestWorld.Log"/>, so the finding stands in for the log rule.</remarks>
  /// <param name="world">A world holding every variant of the blocks judged
  /// (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are placed.</param>
  /// <returns>The law's blocktypes, variants placed and findings, each keyed by the variant's
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
        if (!step.Place(judged: true))
          continue;
        if (step.Entity == null) {
          findings.Add(
            $"{block.Code} placed raised no {block.EntityClass} block entity"
          );
          continue;
        }
        step.Info(player, "fresh", judged: true);
        step.Tick(judged: true);
        step.Info(player, "ticked", judged: true);
        if (step.Reload(judged: true) && step.Entity != null)
          step.Info(player, "reloaded", judged: true);
      }
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }
}
