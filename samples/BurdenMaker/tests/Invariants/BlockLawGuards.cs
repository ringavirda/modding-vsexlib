using System;
using System.Collections.Generic;
using System.Linq;
using BurdenMaker.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Industry;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace BurdenMaker.Tests;

/// <summary>The burdenmaker blocks under <see cref="BlockLaws"/>: a
/// <see cref="BlockFilledMegastructure"/> with <c>ExOrientable</c>, a <c>side</c> group and
/// construction stages, so the placement, break, megablock, info and reload laws judge exlib's own
/// megastructure class. Each law's counts and findings are printed; none is allowed.</summary>
[GuardOf(typeof(BlockLaws), nameof(BlockLaws.Run))]
public class BlockLawGuards(ITestOutputHelper output) {
  private static readonly Lazy<BlockLaws.Result> Laws = new(() => {
    var defs = DefinitionGoldens.Collect(
      "burdenmaker",
      typeof(BlockBurdenmaker).Assembly
    );
    Premise.Covers(defs.Select(DefinitionGoldens.RelativePath), "burdenmaker");
    return BlockLaws.Run(
      "burdenmaker",
      defs.OfType<ExBlockDef>(),
      [
        typeof(BlockStructureFiller).Assembly,
        typeof(IndustryModule).Assembly,
        typeof(BlockBurdenmaker).Assembly,
      ]
    );
  });

  /// <summary>Law, then the blocktypes it judged at its last green run; fewer means the law stopped
  /// seeing blocks it saw.</summary>
  private static readonly Dictionary<string, int> Floors = new() {
    ["placement"] = 1,
    ["break"] = 1,
    ["megablock"] = 1,
    ["info"] = 1,
    ["reload"] = 1,
  };

  private void Judge(string law) {
    BlockLaws.Law result = Laws.Value[law];
    output.WriteLine(result.ToString());
    foreach (string finding in result.Findings)
      output.WriteLine("  " + finding);
    Assert.True(
      result.Blocks >= Floors[law],
      $"the {law} law judged {result.Blocks} burdenmaker blocktype(s), below its floor of "
        + $"{Floors[law]}: a drop means the law stopped seeing blocks"
    );
    FindingLists.Assert(
      result.Findings,
      new Dictionary<string, string>(),
      new Dictionary<string, string>(),
      BlockLaws.CodeOf
    );
  }

  // Fails when ExOrientable reports a placement without placing the block.
  [Fact]
  public void Every_placement_lands_a_declared_side() => Judge("placement");

#if GAME_GE_1_22
  // Fails when the burdenmaker drops its own code beside its stage refund.
  [Fact]
  public void The_burdenmaker_breaks_whole_and_refunds_its_stages() =>
    Judge("break");
#endif

  // Fails when BlockFilledMegastructure.OnBlockRemoved skips RemoveFillers.
  [Fact]
  public void The_burdenmaker_holds_its_fillers_exactly_while_it_stands() =>
    Judge("megablock");

  // Fails when the burdenmaker's GetBlockInfo throws or logs.
  [Fact]
  public void The_burdenmaker_shows_its_info_fresh_ticked_and_reloaded() =>
    Judge("info");

  // Fails when the burdenmaker's FromTreeAttributes throws on the tree it saved.
  [Fact]
  public void The_burdenmaker_reloads_with_its_tree_and_info() =>
    Judge("reload");
}
