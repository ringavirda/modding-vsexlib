using System.Collections.Generic;
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
public class ItemViewTipTests {
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
        JToken.Parse(
          $"{{\"itemViews\":{{\"kind\":\"{kind}\",\"tip\":\"{tip}\"}}}}"
        )
      ),
    };

  private static Item Plain(string kind) =>
    new() {
      Code = new AssetLocation("viewsfixture:probe"),
      Shape = World
        .World.GetItem(new AssetLocation("viewsfixture:declaredtip"))
        .Shape,
      Attributes = new JsonObject(
        JToken.Parse($"{{\"itemViews\":{{\"kind\":\"{kind}\"}}}}")
      ),
    };

  private static string Show(ModelTransform t) =>
    string.Join(
      ",",
      new[]
      {
        t.Translation.X,
        t.Translation.Y,
        t.Translation.Z,
        t.Rotation.X,
        t.Rotation.Y,
        t.Rotation.Z,
        t.Origin.X,
        t.Origin.Y,
        t.Origin.Z,
        t.ScaleXYZ.X,
      }.Select(v =>
        System
          .Math.Round(v, 2)
          .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
      )
    );

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
      Held = set.IdleClip ?? set.ReadyClip ?? "helditemready",
    };
  }

  private static GateReading Tip(ViewGroup group) =>
    ViewGates.Rules(World, group).Single(r => r.Gate == "G14");

  // Fails when the tip grasp is not a vanilla tip-up hold (the bar's, say): the elevation then falls under the gate's minimum.
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
    if (kind is not ("longtool" or "bar"))
      Assert.True(g14.Value > ViewGates.TipElevationDegrees, g14.Detail);
  }

  // Fails when G14 is skipped: a tip held down then raises no finding.
  [Fact]
  public void A_tip_held_down_fails_G14() {
    GateReading g14 = Tip(GroupOf("lump", "+x", "-x"));
    Assert.False(g14.Pass);
    Assert.True(g14.Value < 0);
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
      Box = box with {
        From = new Vec3d(2 * grip - box.To.X - 1, box.From.Y, box.From.Z),
      },
    };

    GateReading g14 = Tip(held);
    Assert.False(g14.Pass);
    Assert.True(g14.Value > ViewGates.TipElevationDegrees);
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

  // Fails when a tip orients the slot or ground lay of a lump, bar or plate: a -x tip then flips them.
  [Theory]
  [InlineData("lump")]
  [InlineData("bar")]
  [InlineData("plate")]
  public void A_tip_leaves_the_slot_and_ground_views_alone(string kind) {
    ItemViewSet plain = ItemViews.Compute(World.Api, Plain(kind))!;
    foreach (string tip in new[] { "+x", "-x" }) {
      ItemViewSet tipped = ItemViews.Compute(World.Api, Tipped(kind, tip))!;
      Assert.Equal(Show(plain.Gui), Show(tipped.Gui));
      Assert.Equal(Show(plain.Ground), Show(tipped.Ground));
    }
  }

  // Fails when the gate's minimum rises past the iron chisel's elevation, which the minimum is measured from.
  [Fact]
  public void The_chisel_and_knife_carry_their_tips_above_the_gate_minimum() {
    foreach (
      (string code, string tip) in new[]
      {
        ("game:chisel-iron", "-x"),
        ("game:knife-generic-iron", "+x"),
      }
    ) {
      ViewGroup group = ViewGates
        .Groups(World, "game")
        .Single(g => g.Codes.Contains(code));
      Assert.True(
        ViewGates.TipElevation(World, group, tip)
          > ViewGates.TipElevationDegrees,
        code
      );
    }
  }

  // Fails when KeyOf leaves the tip out: the tipless twin then represents declaredtip and G14 is never read for it.
  [Fact]
  public void A_tipped_item_grouped_with_a_tipless_twin_is_judged() {
    IReadOnlyList<ViewGroup> groups = ViewGates.Groups(World, "viewsfixture");
    ViewGroup tipped = groups.Single(g =>
      g.Codes.Contains("viewsfixture:authoredtipped")
    );
    ViewGroup twin = groups.Single(g =>
      g.Codes.Contains("viewsfixture:authoredplain")
    );
    Assert.DoesNotContain("viewsfixture:authoredplain", tipped.Codes);
    GateReading g14 = Tip(tipped);
    Assert.True(g14.Pass, g14.Detail);
    Assert.DoesNotContain(ViewGates.Rules(World, twin), r => r.Gate == "G14");
  }

  // Fails when a tipped longtool leaves the long hold or a carrier tool leaves its carrier's, as when either takes the tip grasp.
  [Fact]
  public void A_tipped_longtool_and_tool_keep_their_kinds_views() {
    ItemViewSet longtool = ItemViews.Compute(
      World.Api,
      Tipped("longtool", "+x")
    )!;
    Item tool = Tipped("tool", "+x");
    tool.Tool = EnumTool.Chisel;
    ItemViewSet carried = ItemViews.Compute(World.Api, tool)!;
    Assert.Equal(
      "-1.16,-0.19,0.39,-31.71,109.48,-42.71,1.25,0.09,0.09,1",
      Show(longtool.TpHand)
    );
    Assert.Equal("0.9,0.91,0,33,0,-127,2.1,0.09,0.09,2.64", Show(longtool.Gui));
    Assert.Equal(
      "-1.18,-0.16,-0.08,-93,-170,2,0.9,0.12,0.06,0.76",
      Show(carried.TpHand)
    );
    Assert.Equal(
      "1.75,0.91,0,56.85,35.74,-120.84,1.25,0.09,0.09,0.77",
      Show(carried.Gui)
    );
  }
}
