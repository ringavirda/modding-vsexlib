using System;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The hand views and clips <see cref="ItemViews.Compute(Vintagestory.API.Common.ICoreAPI, CollectibleObject)"/>
/// computes, against vanilla items that ship their own, on the whole vanilla game with the views fixture mod.</summary>
[Collection("ItemViews")]
public class ItemViewsCalibrationTests {
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

  private static Item Coll(string code) =>
    World.World.GetItem(new AssetLocation(code));

  private static ItemViewSet Compute(CollectibleObject collectible) =>
    ItemViews.Compute(World.Api, collectible)!;

  private static Item Variant(string code, string? attributes) =>
    new() {
      Code = new AssetLocation("viewsfixture:probe"),
      Shape = Coll(code).Shape,
      Attributes =
        attributes == null ? null : new JsonObject(JToken.Parse(attributes)),
    };

  // The direction a shape-frame vector points in the hand frame under a hand transform.
  private static Vec3d Held(ModelTransform tf, Vec3d v) =>
    CollectibleViews.Mul(
      CollectibleViews.Rotation(
        new Vec3d(tf.Rotation.X, tf.Rotation.Y - 180, tf.Rotation.Z)
      ),
      v
    );

  private static double Angle(Vec3d a, Vec3d b) =>
    Math.Acos(
      Math.Clamp(
        (a.X * b.X + a.Y * b.Y + a.Z * b.Z) / (a.Length() * b.Length()),
        -1,
        1
      )
    )
    * 180
    / Math.PI;

  // Fails when the -180 is dropped from the write-back only: the working end of the spears turns backward.
  [Theory]
  [InlineData("game:spear-generic-flint", true)]
  [InlineData("game:spear-generic-copper", true)]
  [InlineData("game:spear-scrap-scrap", true)]
  [InlineData("game:oar-crude-aged", true)]
  [InlineData("game:plank-aged", false)]
  [InlineData("game:stick", false)]
  [InlineData("game:prospectingpick-iron", true)]
  public void Held_out_hands_match_their_shipped_views(
    string code,
    bool workingEnd
  ) {
    Item item = Coll(code);
    ItemViewSet set = Compute(item);
    double scale =
      Math.Abs(set.TpHand.ScaleXYZ.X - item.TpHandTransform.ScaleXYZ.X)
      / item.TpHandTransform.ScaleXYZ.X;
    double angle = Angle(
      Held(set.TpHand, set.BoxAxes![0]),
      Held(item.TpHandTransform, set.BoxAxes[0])
    );
    Assert.True(scale <= 0.25, $"{code}: hand scale off by {scale:P0}");
    Assert.True(
      Math.Min(angle, 180 - angle) <= 25,
      $"{code}: held axis off by {Math.Min(angle, 180 - angle):F1} degrees as a line"
    );
    if (workingEnd)
      Assert.True(
        angle <= 90,
        $"{code}: working end off by {angle:F1} degrees"
      );
  }

  private static float[] Numbers(ModelTransform tf) =>
    [
      tf.Translation.X,
      tf.Translation.Y,
      tf.Translation.Z,
      tf.Rotation.X,
      tf.Rotation.Y,
      tf.Rotation.Z,
      tf.Origin.X,
      tf.Origin.Y,
      tf.Origin.Z,
      tf.ScaleXYZ.X,
      tf.ScaleXYZ.Y,
      tf.ScaleXYZ.Z,
    ];

  // The largest projected extent of the drawn box under a slot transform, over the slot's 48 pixels.
  private static double SlotSpan(
    CollectibleViews.ViewBox box,
    ModelTransform tf
  ) {
    double[,] m = CollectibleViews.Rotation(
      new Vec3d(tf.Rotation.X, tf.Rotation.Y, tf.Rotation.Z)
    );
    var corners = box.Drawn.SelectMany(c => c).ToArray();
    double[][] p =
    [
      .. corners.Select(c =>
      {
        Vec3d v = CollectibleViews.Mul(
          m,
          new Vec3d(c.X / 16, c.Y / 16, c.Z / 16)
        );
        return new[] { v.X, v.Y };
      }),
    ];
    double w = p.Max(q => q[0]) - p.Min(q => q[0]);
    double h = p.Max(q => q[1]) - p.Min(q => q[1]);
    return 25.6 * tf.ScaleXYZ.X * Math.Max(w, h) / 48;
  }

  // Fails when ViewReferences' ground scales move 50%, and when the slot scale moves 50%.
  [Theory]
  [InlineData("game:spear-generic-flint")]
  [InlineData("game:spear-generic-copper")]
  [InlineData("game:spear-scrap-scrap")]
  [InlineData("game:oar-crude-aged")]
  [InlineData("game:plank-aged")]
  [InlineData("game:stick")]
  [InlineData("game:prospectingpick-iron")]
  public void Held_out_slots_and_grounds_match_their_shipped_views(string code) {
    Item item = Coll(code);
    ItemViewSet set = Compute(item);
    double ground =
      Math.Abs(set.Ground.ScaleXYZ.X - item.GroundTransform.ScaleXYZ.X)
      / item.GroundTransform.ScaleXYZ.X;
    CollectibleViews.ViewBox box = CollectibleViews.BoxOf(World.Api, item)!;
    double shipped = SlotSpan(box, item.GuiTransform);
    double span = Math.Abs(SlotSpan(box, set.Gui) - shipped) / shipped;
    Assert.True(ground <= 0.35, $"{code}: ground k off by {ground:P1}");
    Assert.True(span <= 0.33, $"{code}: slot span off by {span:P1}");
  }

  // Fails when a ground lay puts the shortest axis anywhere but up, and when it does not rest on y 0.
  [Theory]
  [InlineData("game:ingot-iron")]
  [InlineData("game:metalplate-iron")]
  [InlineData("viewsfixture:uprightsheet")]
  [InlineData("game:spear-generic-flint")]
  public void A_ground_item_lies_flat_and_rests_on_the_floor(string code) {
    Item item = Coll(code);
    ItemViewSet set = Compute(item);
    ModelTransform g = set.Ground;
    double[,] m = CollectibleViews.Mul(
      CollectibleViews.Mul(
        CollectibleViews.Rotation(new Vec3d(0, g.Rotation.Y, 0)),
        CollectibleViews.Rotation(new Vec3d(0, 0, g.Rotation.Z))
      ),
      CollectibleViews.Rotation(new Vec3d(g.Rotation.X, 0, 0))
    );
    Vec3d up = CollectibleViews.Mul(m, set.BoxAxes![2]);
    Vec3d along = CollectibleViews.Mul(m, set.BoxAxes[0]);
    Assert.True(Math.Abs(up.Y) > 0.999, $"{code}: shortest axis {up}");
    Assert.True(Math.Abs(along.X) > 0.999, $"{code}: longest axis {along}");
    double low = CollectibleViews
      .BoxOf(World.Api, item)!
      .Drawn.SelectMany(c => c)
      .Min(c =>
        CollectibleViews
          .Mul(
            m,
            new Vec3d(
              c.X / 16 - g.Origin.X,
              c.Y / 16 - g.Origin.Y,
              c.Z / 16 - g.Origin.Z
            )
          )
          .Y
      );
    Assert.Equal(0, g.Translation.Y + 0.2 * g.ScaleXYZ.X * low, 3);
  }

  // Fails when the refusal is removed.
  [Theory]
  [InlineData(ItemViewKind.None)]
  [InlineData(ItemViewKind.Tool)]
  [InlineData(ItemViewKind.Workpiece)]
  public void Compute_refuses_kinds_whose_reference_is_a_collectible(
    ItemViewKind kind
  ) {
    Assert.Throws<ArgumentException>(() =>
      ItemViews.Compute(World.Api, Coll("game:stick").Shape, kind)
    );
  }

  // Fails when the overload returns the kind's game defaults.
  [Fact]
  public void The_shape_overload_equals_the_collectible_overload() {
    Item spear = Coll("game:spear-generic-iron");
    ItemViewSet byItem = Compute(spear);
    ItemViewSet byShape = ItemViews.Compute(
      World.Api,
      spear.Shape,
      ItemViewKind.Longtool
    );
    Assert.Equal(ItemViewKind.Longtool, byItem.Kind);
    Assert.Equal(Numbers(byItem.Gui), Numbers(byShape.Gui));
    Assert.Equal(Numbers(byItem.TpHand), Numbers(byShape.TpHand));
    Assert.Equal(Numbers(byItem.Ground), Numbers(byShape.Ground));
    Assert.NotEqual(
      Numbers(ModelTransform.ItemDefaultGui()),
      Numbers(byShape.Gui)
    );
  }

  // Fails when a shape that does not load is not refused.
  [Fact]
  public void A_shape_that_does_not_load_is_refused() {
    var ex = Assert.Throws<InvalidOperationException>(() =>
      ItemViews.Compute(
        World.Api,
        new CompositeShape {
          Base = new AssetLocation("viewsfixture:item/nope"),
        },
        ItemViewKind.Lump
      )
    );
    Assert.Contains("viewsfixture:item/nope", ex.Message);
  }

  // Fails when the longtool's slot origin leaves 0.84 of its length, or its scale ignores the length.
  [Fact]
  public void A_longtool_slot_is_the_spears_close_up_scaled_by_length() {
    Item spear = Coll("game:spear-generic-flint");
    ItemViewSet set = Compute(spear);
    double length = set.BoxTo!.X - set.BoxFrom!.X;
    Assert.Equal(2.26 * 46.75 / length, set.Gui.ScaleXYZ.X, 3);
    double along = (set.Gui.Origin.X * 16 - set.BoxFrom.X) / length;
    Assert.Equal(0.84, along, 3);
  }

  // Fails when a grip is left where the reference's fractions put it instead of moved onto the drawn shape.
  [Theory]
  [InlineData("game:spear-generic-flint")]
  [InlineData("game:oar-crude-aged")]
  [InlineData("game:plank-aged")]
  [InlineData("game:stick")]
  [InlineData("game:prospectingpick-iron")]
  public void The_grip_lies_on_the_drawn_shape(string code) {
    ItemViewSet set = Compute(Coll(code));
    CollectibleViews
      .BoxOf(World.Api, Coll(code))!
      .Nearest(set.Grip!, out double distance);
    Assert.True(distance <= 0.01, $"{code}: grip {distance:F2} voxels off");
  }

  // Fails when the hit clip is the idle clip, and when a lump takes any clip.
  [Fact]
  public void A_longtool_carries_the_spear_clips() {
    ItemViewSet spear = Compute(Coll("game:spear-generic-flint"));
    Assert.Equal(ItemViewKind.Longtool, spear.Kind);
    Assert.Equal("spearidle", spear.IdleClip);
    Assert.Equal("spearidle", spear.ReadyClip);
    Assert.Equal("spearhit", spear.HitClip);
    Assert.Null(spear.UseClip);
    ItemViewSet ingot = Compute(Coll("game:ingot-iron"));
    Assert.Equal(ItemViewKind.Lump, ingot.Kind);
    Assert.Null(ingot.IdleClip);
    Assert.Null(ingot.ReadyClip);
    Assert.Null(ingot.HitClip);
  }

  // Fails when the translation is not the negative of the origin over the scale: the grip then misses the hand.
  [Fact]
  public void The_origin_is_the_grip_and_the_translation_returns_it() {
    ItemViewSet set = Compute(Coll("game:ingot-iron"));
    ModelTransform hand = set.TpHand;
    Assert.Equal(set.Grip!.X / 16, hand.Origin.X, 3);
    Assert.Equal(set.Grip.Y / 16, hand.Origin.Y, 3);
    Assert.Equal(set.Grip.Z / 16, hand.Origin.Z, 3);
    Assert.Equal(-hand.Origin.X / hand.ScaleXYZ.X, hand.Translation.X, 3);
    Assert.Equal(-hand.Origin.Y / hand.ScaleXYZ.X, hand.Translation.Y, 3);
    Assert.Equal(-hand.Origin.Z / hand.ScaleXYZ.X, hand.Translation.Z, 3);
    Assert.Equal(0.75, hand.ScaleXYZ.X, 4);
    Assert.True(hand.Rotate);
  }

  // Fails when the work item's transforms are replaced by the game's defaults.
  [Fact]
  public void A_workpiece_follows_the_vanilla_work_item() {
    Item reference = World
      .World.Items.Where(i => i is ItemWorkItem && i.Code?.Domain == "game")
      .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal)
      .First();
    ItemViewSet set = Compute(new ItemWorkItem());
    Assert.Equal(ItemViewKind.Workpiece, set.Kind);
    Assert.Equal(reference.TpHandTransform.Rotation.Y, set.TpHand.Rotation.Y);
    Assert.Equal(reference.TpHandTransform.ScaleXYZ.X, set.TpHand.ScaleXYZ.X);
    Assert.Equal(reference.GroundTransform.ScaleXYZ.X, set.Ground.ScaleXYZ.X);
    Assert.Null(set.Grip);
    Assert.NotEqual(
      ModelTransform.ItemDefaultTp().ScaleXYZ.X,
      set.TpHand.ScaleXYZ.X
    );
  }

  // Fails when a declared 'none' is computed anyway.
  [Fact]
  public void A_declared_none_computes_nothing() {
    Assert.Null(
      ItemViews.Compute(
        World.Api,
        Variant(
          "game:spear-generic-flint",
          "{\"itemViews\":{\"kind\":\"none\"}}"
        )
      )
    );
  }

  [Fact]
  public void An_item_with_no_shape_computes_nothing() {
    Assert.Null(ItemViews.Compute(World.Api, new Item()));
  }

  // Fails when the tip attribute is ignored: the working end then stays at the larger cross-section, +x.
  [Fact]
  public void A_declared_tip_turns_the_working_end() {
    Vec3d plain = Compute(Variant("game:spear-generic-flint", null)).BoxAxes![
      0
    ];
    Vec3d turned = Compute(
      Variant("game:spear-generic-flint", "{\"itemViews\":{\"tip\":\"-x\"}}")
    ).BoxAxes![0];
    Assert.True(plain.X > 0.99);
    Assert.True(turned.X < -0.99);
  }

  // Fails when a tip is read without checking, each of its sign, axis and length alone.
  [Theory]
  [InlineData("xy")]
  [InlineData("+w")]
  [InlineData("+yy")]
  public void A_malformed_tip_is_refused(string tip) {
    Assert.Throws<ArgumentException>(() =>
      Compute(
        Variant(
          "game:spear-generic-flint",
          $"{{\"itemViews\":{{\"tip\":\"{tip}\"}}}}"
        )
      )
    );
  }

  // Fails when counts tie and the larger cross-section is not the working end: the head, 3 x 3 at the low
  // end against a 1 x 1 shaft at the high end, then stays at +x.
  [Fact]
  public void The_larger_cross_section_wins_when_the_elements_tie() {
    Assert.True(Compute(Coll("viewsfixture:bigend")).BoxAxes![0].X < -0.99);
  }

  // Fails when a block kind declared on an item is laid out as a bar.
  [Fact]
  public void A_declared_block_kind_computes_nothing() {
    Assert.Null(
      ItemViews.Compute(
        World.Api,
        Variant("game:stick", "{\"itemViews\":{\"kind\":\"cube\"}}")
      )
    );
  }

  // Fails when bulk takes the lump's empty clips or a grip off the drawn shape.
  [Fact]
  public void A_bulk_item_is_held_in_both_hands() {
    ItemViewSet set = Compute(
      Variant("game:ingot-iron", "{\"itemViews\":{\"kind\":\"bulk\"}}")
    );
    Assert.Equal(ItemViewKind.Bulk, set.Kind);
    Assert.Equal("holdbothhandslarge", set.IdleClip);
    Assert.Equal("holdbothhandslarge", set.ReadyClip);
    Assert.Null(set.HitClip);
    Assert.Equal(0.75, set.TpHand.ScaleXYZ.X, 4);
  }

  // Fails when a tool no vanilla item carries stays a tool: it then has no reference and computes nothing.
  [Fact]
  public void A_tool_without_a_carrier_is_laid_by_its_box() {
    EnumTool tool = Enum.GetValues<EnumTool>()
      .First(t => World.World.Items.All(i => i?.Tool != t));
    Item stick = Variant("game:stick", null);
    stick.Tool = tool;
    Assert.Equal(ItemViewKind.Bar, Compute(stick).Kind);
  }

  // Fails when the longest code is taken for the carrier, or the order is not ordinal.
  [Fact]
  public void The_tool_carrier_is_the_shortest_code_beginning_with_the_value() {
    Item pick = Coll("game:prospectingpick-iron");
    Item carrier = ItemViews.CarrierOf(World.Api, pick)!;
    Assert.Equal("game:pickaxe-gold", carrier.Code.ToString());
    Assert.Equal(
      carrier.TpHandTransform.ScaleXYZ.X,
      Compute(pick).TpHand.ScaleXYZ.X,
      4
    );
  }

  // Fails when a tool value that no code begins with finds no carrier.
  [Fact]
  public void A_tool_value_no_code_begins_with_takes_the_first_code() {
    Item blade = Variant("game:stick", null);
    blade.Tool = EnumTool.Sword;
    Assert.Equal(
      "game:blade-arming-ruined",
      ItemViews.CarrierOf(World.Api, blade)!.Code.ToString()
    );
  }

  // Fails when the mirroring of the shortest axis is not undone: a sheet whose thin side is z is then
  // gripped on the wrong face and held mirrored against the metal plate it matches.
  [Fact]
  public void A_mirrored_lay_holds_the_sheet_as_the_metal_plate_is_held() {
    Item plate = Coll("game:metalplate-iron");
    Item sheet = Coll("viewsfixture:uprightsheet");
    ItemViewSet held = Compute(sheet);
    ItemViewSet reference = Compute(plate);
    Assert.Equal(ItemViewKind.Plate, held.Kind);
    Vec3d Offset(ItemViewSet set) {
      Vec3d centre = new(
        (set.BoxFrom!.X + set.BoxTo!.X) / 2,
        (set.BoxFrom.Y + set.BoxTo.Y) / 2,
        (set.BoxFrom.Z + set.BoxTo.Z) / 2
      );
      Vec3d centred = new(0, 0, 0);
      for (int k = 0; k < 3; k++) {
        Vec3d a = set.BoxAxes![k];
        double along =
          a.X * set.Grip!.X
          + a.Y * set.Grip.Y
          + a.Z * set.Grip.Z
          - (
            k == 0 ? centre.X
            : k == 1 ? centre.Y
            : centre.Z
          );
        centred += new Vec3d(a.X * along, a.Y * along, a.Z * along);
      }
      return Held(set.TpHand, centred);
    }
    Vec3d got = Offset(held);
    Vec3d want = Offset(reference);
    Assert.True(
      got.DistanceTo(want) < 0.05,
      $"grip offset in the hand {got} against the metal plate's {want}"
    );
  }

  // Fails when the composite rotation is dropped from the item box, in either overload: the turned stick then lays
  // its axes as the unturned one does.
  [Fact]
  public void A_turned_item_shape_turns_its_box_axes() {
    Item stick = Coll("game:stick");
    Item turned = Coll("viewsfixture:turnedstick");
    ItemViewSet plain = Compute(stick);
    ItemViewSet byItem = Compute(turned);
    ItemViewSet byShape = ItemViews.Compute(
      World.Api,
      turned.Shape,
      ItemViewKind.Bar
    );
    Vec3d expected = CollectibleViews.Mul(
      CollectibleViews.Rotation(new Vec3d(0, 90, 0)),
      plain.BoxAxes![0]
    );
    Assert.Equal(
      plain.BoxTo!.X - plain.BoxFrom!.X,
      byItem.BoxTo!.X - byItem.BoxFrom!.X,
      3
    );
    foreach (ItemViewSet set in new[] { byItem, byShape }) {
      Vec3d a = set.BoxAxes![0];
      double dot = Math.Abs(
        a.X * expected.X + a.Y * expected.Y + a.Z * expected.Z
      );
      Assert.True(dot > 0.999, $"turned axis {a} against {expected}");
    }
    Assert.True(Math.Abs(plain.BoxAxes[0].X) < 0.999);
  }
}
