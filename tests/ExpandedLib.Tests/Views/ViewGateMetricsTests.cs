using System;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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

    ViewGroup ingot = GroupOf("game:ingot-iron");
    SlotMetrics ingotSlot = ViewGates.Slot(ingot);
    GroundMetrics ingotGround = ViewGates.Ground(ingot);
    Assert.Equal(0.763, ingotSlot.Span, 0.01);
    Assert.Equal(2.042, ingotSlot.Centre, 0.01);
    Assert.Equal(0.000, ingotGround.Rest, 0.01);
    Assert.Equal(0.96, ingotGround.Size, 0.01);

    ViewGroup hay = GroupOf("game:hay-normal-ud");
    SlotMetrics haySlot = ViewGates.Slot(hay);
    GroundMetrics hayGround = ViewGates.Ground(hay);
    Assert.Equal(0.782, haySlot.Span, 0.01);
    Assert.Equal(0.707, haySlot.Centre, 0.01);
    Assert.Equal(0.000, hayGround.Rest, 0.01);
    Assert.Equal(0.300, hayGround.Size, 0.01);
  }

  // Fails when the group key adds the code: the ingot's metals then form one group each.
  [Fact]
  public void One_group_names_every_code() {
    ViewGroup ingots = GroupOf("game:ingot-iron");
    Assert.Contains("game:ingot-copper", ingots.Codes);
    Assert.Contains("game:ingot-steel", ingots.Codes);
    Assert.True(ingots.Codes.Count > 5);
  }

#if GAME_GE_1_22
  // Fails when the hand pose is taken at rest instead of the clip's frame 0: the spear's first-person axis then equals its third-person one.
  [Fact]
  public void Hand_metrics_match_the_spec_rows()
  {
    HandMetrics spear = ViewGates.Hand(
      World,
      GroupOf("game:spear-generic-iron")
    );
    Assert.Equal(1.24, spear.Size, 0.02);
    Assert.Equal(
      0,
      ViewGates.AxisDegrees(World, GroupOf("game:spear-generic-iron")),
      0.02
    );
    HandMetrics fp = ViewGates.Hand(
      World,
      GroupOf("game:spear-generic-iron"),
      true
    );
    Assert.Equal(
      8.9,
      Math.Acos(
        spear.Axis.X * fp.Axis.X
          + spear.Axis.Y * fp.Axis.Y
          + spear.Axis.Z * fp.Axis.Z
      )
        * 180
        / Math.PI,
      0.5
    );
    Assert.Equal(
      0.68,
      ViewGates.Hand(World, GroupOf("game:rod-iron")).Size,
      0.02
    );
    Assert.Equal(
      0.32,
      ViewGates.Hand(World, GroupOf("game:metalplate-iron")).Grip!.Value,
      0.02
    );
    Assert.Equal(
      0,
      ViewGates.Hand(World, GroupOf("game:hay-normal-ud")).Centre!.Value,
      0.02
    );
    Assert.Equal(
      0.259,
      ViewGates.Hand(World, GroupOf("game:stonebricks-granite")).Centre!.Value,
      0.02
    );
  }
#endif

#if GAME_GE_1_22
  // Fails when G10 takes the box axis's sign in place of the working end: the unturned blackbronze then reads near 180.
  [Fact]
  public void Spear_axis_is_judged_by_its_working_end()
  {
    ViewGroup spear = GroupOf("game:spear-generic-blackbronze");
    Assert.InRange(ViewGates.AxisDegrees(World, spear), 0, 3.3);
    Assert.InRange(ViewGates.AxisDegrees(World, spear, true), 0, 3.3);

    Item item = World.World.GetItem(
      new AssetLocation("game:spear-generic-blackbronze")
    );
    ModelTransform authored = item.TpHandTransform;
    try
    {
      ModelTransform turned = authored.Clone();
      turned.Rotation = new FastVec3f(
        authored.Rotation.X,
        authored.Rotation.Y + 180,
        authored.Rotation.Z
      );
      item.TpHandTransform = turned;
      Assert.InRange(
        ViewGates.AxisDegrees(World, GroupOf("game:spear-generic-blackbronze")),
        170,
        180
      );
    }
    finally
    {
      item.TpHandTransform = authored;
    }
  }
#endif

#if GAME_GE_1_22
  // Fails when G10 ignores a declared tip: the count rule then takes the butt for the working end and the axis reads near 180.
  [Fact]
  public void A_declared_tip_sets_the_working_end()
  {
    ViewGroup declared = ViewGates
      .Groups(World, "viewsfixture")
      .Single(g => g.Codes.Contains("viewsfixture:declaredtip"));
    Item spear = World.World.GetItem(
      new AssetLocation("game:spear-generic-iron")
    );
    ViewGroup held = declared with { Hand = spear.TpHandTransform.Clone() };
    Assert.InRange(ViewGates.AxisDegrees(World, held), 0, 3.3);
  }
#endif

  // Fails when the animation code is matched case-sensitively, or when an unmapped code is posed as a clip.
  [Fact]
  public void Held_code_resolves_through_the_players_animation_list() {
    string? scythe = ViewGates.HeldClip(World, "scytheIdle");
    Assert.NotNull(scythe);
    Assert.Equal(scythe, ViewGates.HeldClip(World, "scytheidle"));
    Assert.Equal("idle1", ViewGates.HeldClip(World, "idle"));
    Assert.Null(ViewGates.HeldClip(World, "beginholditem"));

    HandMetrics axe = ViewGates.Hand(World, GroupOf("game:axe-scrap-scrap"));
    Assert.Equal(0.950, axe.Size, 0.01);
    Assert.Equal(0.000, axe.Grip!.Value, 0.01);
    HandMetrics scytheHand = ViewGates.Hand(
      World,
      GroupOf("game:scythe-copper")
    );
    Assert.Equal(0.800, scytheHand.Size, 0.01);
    Assert.Equal(0.079, scytheHand.Grip!.Value, 0.01);
  }

  // Fails when the hit clip leaves the group key: the two ingots then share a group.
  [Fact]
  public void Groups_split_on_the_hit_clip() {
    Item ingot = World.World.GetItem(new AssetLocation("game:ingot-copper"));
    string? authored = ingot.HeldTpHitAnimation;
    try {
      ingot.HeldTpHitAnimation = "axechop";
      Assert.DoesNotContain(
        "game:ingot-iron",
        GroupOf("game:ingot-copper").Codes
      );
    } finally {
      ingot.HeldTpHitAnimation = authored;
    }
    Assert.Contains("game:ingot-iron", GroupOf("game:ingot-copper").Codes);
  }
}
