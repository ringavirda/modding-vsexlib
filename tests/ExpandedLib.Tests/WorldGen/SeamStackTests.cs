#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.WorldGen;
using Xunit;

namespace ExpandedLib.Tests.WorldGen;

public class SeamStackTests
{
  private static readonly SeamLayer[] Measures =
  [
    new() { Block = "fireclay", Offset = 0 },
    new() { Block = "coal", Offset = 0 },
    new() { Block = "nodules", Offset = 1 },
    new() { Block = "nodules", Offset = 2 },
    new() { Block = "fireclay", Offset = 1 },
    new() { Block = "coal", Offset = 0 },
  ];

  private static readonly float[] Thickness = [1, 2, 1, 1, 1, 2];

  private static string Column(List<SeamCell> cells, int height) =>
    string.Join(
      ",",
      Enumerable
        .Range(0, height)
        .Select(y =>
          cells.Any(c => c.Y == y)
            ? Measures[cells.First(c => c.Y == y).Layer].Block
            : "host"
        )
    );

  // Fails when the taper is not applied to the offsets: the host between layers keeps its
  // full height at f 0.5 and the upper bottoms stay put.
  [Fact]
  public void Taper_halves_every_bottom_from_the_base()
  {
    var full = SeamStack.Spans(Measures, Thickness, 0, 1f);
    var half = SeamStack.Spans(Measures, Thickness, 0, 0.5f);

    for (int i = 0; i < Measures.Length; i++)
    {
      Assert.Equal(full[i].Bottom / 2, half[i].Bottom, 0.0001);
      Assert.Equal(full[i].Top / 2, half[i].Top, 0.0001);
    }
  }

  // Fails when two layers swap places in the stack.
  [Fact]
  public void The_coal_measures_stack_at_full_height_in_order()
  {
    var cells = SeamStack.Lay(Measures, Thickness, 0, 1f, 0f, 0, 0);

    Assert.Equal(
      "fireclay,coal,coal,host,nodules,host,host,nodules,host,fireclay,coal,coal",
      Column(cells, 12)
    );
  }

  // Fails when the cover depth is ignored: the stack then reaches the rock top.
  [Fact]
  public void Min_cover_keeps_rock_above_every_layer_block()
  {
    var covered = SeamStack.Lay(Measures, Thickness, 10, 1f, 0f, 4, 15);
    var open = SeamStack.Lay(Measures, Thickness, 10, 1f, 0f, 0, 15);

    Assert.Equal(11, covered.Max(c => c.Y));
    Assert.Equal(21, open.Max(c => c.Y));
  }

  // Fails when rounding uses a boundary without r: a half-height thin layer then never shows.
  [Fact]
  public void A_thin_layer_shows_in_a_share_of_columns()
  {
    SeamLayer[] one = [new() { Block = "coal" }];
    int shown = 0;
    for (int i = 0; i < 100; i++)
    {
      shown += SeamStack.Lay(one, [1f], 0, 0.5f, i / 100f, 0, 0).Count;
    }

    Assert.Equal(50, shown);
  }
}
#endif
