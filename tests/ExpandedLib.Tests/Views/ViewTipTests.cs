using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A declared tip held up in every kind, and G14 hand.tip, on the views fixture mod.</summary>
[Collection("ItemViews")]
public class ViewTipTests {
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

  private static Item Tipped(string kind, string tip) =>
    new() {
      Code = new AssetLocation("viewsfixture:probe"),
      Shape = World
        .World.GetItem(new AssetLocation("viewsfixture:declaredtip"))
        .Shape,
      Attributes = new JsonObject(
        JToken.Parse($"{{\"itemViews\":{{\"kind\":\"{kind}\",\"tip\":\"{tip}\"}}}}")
      ),
    };

  private static ViewGroup GroupOf(string kind, string tip, string handTip) {
    Item probe = Tipped(kind, tip);
    ItemViewSet set = ItemViews.Compute(World.Api, Tipped(kind, handTip))!;
    ViewGroup declared = ViewGates
      .Groups(World, "viewsfixture")
      .Single(g => g.Codes.Contains("viewsfixture:declaredtip"));
    return declared with {
      Representative = probe,
      Kind = set.Kind,
      Hand = set.TpHand,
      Held = set.IdleClip ?? "",
    };
  }

  private static GateReading Tip(ViewGroup group) =>
    ViewGates.Rules(World, group).Single(r => r.Gate == "G14");

  // Fails when the tip is ignored for a lump: the longest +end then goes into the fist and G14 fails.
  [Theory]
  [InlineData("lump", "+x")]
  [InlineData("lump", "-x")]
  [InlineData("bar", "+x")]
  [InlineData("bar", "-x")]
  [InlineData("bulk", "+x")]
  [InlineData("bulk", "-x")]
  [InlineData("plate", "+x")]
  [InlineData("plate", "-x")]
  [InlineData("longtool", "+x")]
  [InlineData("longtool", "-x")]
  public void A_declared_tip_is_held_up_in_every_kind(string kind, string tip) {
    GateReading g14 = Tip(GroupOf(kind, tip, tip));
    Assert.True(g14.Pass, g14.Detail);
    Assert.True(g14.Value > 0.5);
  }

  // Fails when G14 is skipped: a tip held down then raises no finding.
  [Fact]
  public void A_tip_held_down_fails_G14() {
    GateReading g14 = Tip(GroupOf("lump", "+x", "-x"));
    Assert.False(g14.Pass);
    Assert.True(g14.Value < -1);
    Assert.Contains("below the grip end", g14.Detail);
  }

  // Fails when the half test is dropped: a grip in the tip's half, the tip still up, then passes.
  [Fact]
  public void A_grip_in_the_tip_half_fails_G14() {
    ViewGroup group = GroupOf("lump", "+x", "+x");
    Assert.True(Tip(group).Pass);

    double[] f = ViewGates.GripOf(World, group).Fractions;
    CollectibleViews.ViewBox box = group.Box;
    Assert.Equal(1, box.Axes[0].X, 3);
    double grip = box.From.X + f[0] * (box.To.X - box.From.X);
    ViewGroup held = group with {
      Box = box with { From = new Vec3d(2 * grip - box.To.X - 1, box.From.Y, box.From.Z) },
    };

    GateReading g14 = Tip(held);
    Assert.False(g14.Pass);
    Assert.True(g14.Value > 0.5);
    Assert.Contains("handle half", g14.Detail);
  }

  // Fails when G14 reads a group with no declared tip: the plain fixture lump then carries a reading.
  [Fact]
  public void A_group_with_no_tip_has_no_G14() {
    ViewGroup plain = ViewGates
      .Groups(World, "viewsfixture")
      .Single(g => g.Codes.Contains("viewsfixture:declaredlump"));
    Assert.DoesNotContain(ViewGates.Rules(World, plain), r => r.Gate == "G14");
  }
}
