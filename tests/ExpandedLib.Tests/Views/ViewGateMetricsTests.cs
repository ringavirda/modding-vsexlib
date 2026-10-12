using System;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The slot and ground metrics of the view gates, on the whole vanilla game with the views fixture mod.</summary>
[Collection("ItemViews")]
public class ViewGateMetricsTests {
  private static string Fixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Helpers",
      "Views",
      "Fixtures",
      "viewsfixture"
    );

  private static TestWorld World => GameWorlds.Get([Fixture]);

  private static ViewGroup GroupOf(string code) =>
    ViewGates.Groups(World, "game").Single(g => g.Codes.Contains(code));

  // Fails when T(-o) is dropped from the slot matrix: metalplate's G2 then moves past 1 px.
  [Fact]
  public void Slot_and_ground_metrics_match_the_spec_rows() {
    ViewGroup plate = GroupOf("game:metalplate-iron");
    SlotMetrics slot = ViewGates.Slot(plate);
    GroundMetrics ground = ViewGates.Ground(plate);
    Assert.Equal(0.785, slot.Span, 0.01);
    Assert.Equal(0.61, slot.Centre, 0.01);
    Assert.Equal(0.000, ground.Rest, 0.01);
    Assert.Equal(0.66, ground.Size, 0.01);

    Assert.Equal(0.96, ViewGates.Ground(GroupOf("game:ingot-iron")).Size, 0.01);

    ViewGroup hay = GroupOf("game:hay-normal-ud");
    Assert.Equal(0.782, ViewGates.Slot(hay).Span, 0.01);
    Assert.Equal(0.300, ViewGates.Ground(hay).Size, 0.01);
  }

  // Fails when the group key adds the code: the ingot's metals then form one group each.
  [Fact]
  public void One_group_names_every_code() {
    ViewGroup ingots = GroupOf("game:ingot-iron");
    Assert.Contains("game:ingot-copper", ingots.Codes);
    Assert.Contains("game:ingot-steel", ingots.Codes);
    Assert.True(ingots.Codes.Count > 5);
  }
}
