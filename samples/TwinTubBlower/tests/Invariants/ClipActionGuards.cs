#if GAME_GE_1_22
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Industry;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace TwinTubBlower.Tests;

/// <summary>Every twintubblower clip is driven by an action or listed static, every action plays its clip
/// without a jump in its set time on a client twin, and no two shapes share an animator key. The rule
/// is <see cref="ClipLaws"/>'s; the findings the sample accepts are in <c>clip-actions.json</c>.</summary>
[GuardOf(typeof(ClipLaws), nameof(ClipLaws.Run))]
public class ClipActionGuards(ITestOutputHelper output)
{
  // Fails when a twintubblower clip has no action and no static row, an action ends away from its pose or
  // outside its time, or a known row matches no finding.
  [Fact]
  public void Every_twintubblower_clip_has_a_driver_and_plays_as_declared()
  {
    var defs = DefinitionGoldens.Collect(
      "twintubblower",
      typeof(TwinTubBlower.Blocks.BlockTwinTubMPBlower).Assembly
    );
    Premise.Covers(
      defs.Select(DefinitionGoldens.RelativePath),
      "twintubblower"
    );
    BlockLaws.Result result = ClipLaws.Run(
      "twintubblower",
      defs,
      [
        typeof(BlockStructureFiller).Assembly,
        typeof(IndustryModule).Assembly,
        typeof(TwinTubBlower.Blocks.BlockTwinTubMPBlower).Assembly,
      ],
      System.IO.Path.Combine(
        RepoPaths.Mod("twintubblower"),
        "tests",
        "Invariants",
        "clip-actions.json"
      )
    );
    foreach (BlockLaws.Law law in result.Laws)
    {
      output.WriteLine(law.ToString());
      foreach (string finding in law.Findings)
        output.WriteLine("  " + finding);
    }
    Assert.All(
      result.Laws,
      law =>
        Assert.True(
          law.Findings.Count == 0,
          string.Join("\n", law.Findings.Prepend(law.ToString()))
        )
    );
    Assert.True(
      result.Laws.Single(l => l.Name == "animator key").Blocks >= 1,
      "the animator key law saw no twintubblower blocktype with an animator"
    );
    Assert.True(
      result.Laws.Single(l => l.Name == "clip driver").Blocks >= 1,
      "the clip driver law saw no twintubblower blocktype with clips"
    );
  }
}
#endif
