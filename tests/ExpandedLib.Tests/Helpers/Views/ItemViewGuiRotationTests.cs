using System;
using System.IO;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A declared gui rotation: the slot view takes it and recomputes its origin and scale, on the views
/// fixture mod.</summary>
[Collection("ItemViews")]
public class ItemViewGuiRotationTests {
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

  private static Item ItemOf(string code) =>
    World.World.GetItem(new AssetLocation(code));

  private static Item Barred(string attributes) =>
    new() {
      Code = new AssetLocation("viewsfixture:probe"),
      Shape = ItemOf("viewsfixture:rotatedshaft").Shape,
      Attributes = new JsonObject(JToken.Parse(attributes)),
    };

  private static bool Same(ModelTransform a, ModelTransform b) =>
    a.Translation.Equals(b.Translation)
    && a.Rotation.Equals(b.Rotation)
    && a.Origin.Equals(b.Origin)
    && a.ScaleXYZ.Equals(b.ScaleXYZ);

  private static void SameTurn(Vec3d expected, ModelTransform tf) {
    double[,] a = CollectibleViews.Rotation(expected);
    double[,] b = CollectibleViews.Rotation(
      new Vec3d(tf.Rotation.X, tf.Rotation.Y, tf.Rotation.Z)
    );
    for (int i = 0; i < 3; i++)
      for (int j = 0; j < 3; j++)
        Assert.Equal(a[i, j], b[i, j], 4);
  }

  // The item slot's span at the transform's scale over the slot's 48 pixels, from the box corners in the lay frame.
  private static double ItemSlotSpan(ItemViewSet set) {
    ModelTransform tf = set.Gui;
    double[,] m = CollectibleViews.Rotation(
      new Vec3d(tf.Rotation.X, tf.Rotation.Y, tf.Rotation.Z)
    );
    double reach = 0;
    for (int i = 0; i < 8; i++) {
      double[] u =
      [
        (i & 1) == 0 ? set.BoxFrom!.X : set.BoxTo!.X,
        (i & 2) == 0 ? set.BoxFrom!.Y : set.BoxTo!.Y,
        (i & 4) == 0 ? set.BoxFrom!.Z : set.BoxTo!.Z,
      ];
      Vec3d p = new(
        set.BoxAxes!.Select((a, k) => a.X * u[k]).Sum() - tf.Origin.X * 16,
        set.BoxAxes!.Select((a, k) => a.Y * u[k]).Sum() - tf.Origin.Y * 16,
        set.BoxAxes!.Select((a, k) => a.Z * u[k]).Sum() - tf.Origin.Z * 16
      );
      Vec3d v = CollectibleViews.Mul(m, p);
      reach = Math.Max(reach, Math.Max(Math.Abs(v.X), Math.Abs(v.Y)));
    }
    return tf.ScaleXYZ.X
      * 2
      * reach
      / 16
      * ViewReferences.SlotItemPixels
      / ViewReferences.SlotPixels;
  }

  // Fails when the scale is taken from the unrotated kind: the rotated box then overruns or underfills the slot.
  [Fact]
  public void A_rotated_gui_view_fills_the_slots_span() {
    Item item = ItemOf("viewsfixture:rotatedshaft");
    ItemViewSet set = ItemViews.Compute(World.Api, item)!;

    SameTurn(new Vec3d(20, 50, 70), set.Gui);
    Assert.Equal(ViewReferences.SlotSpan, ItemSlotSpan(set), 3);
    Assert.True(Same(set.Gui, item.GuiTransform));
  }

  // Fails when the rotation is applied to every view: the hand or ground view then takes the declared rotation.
  [Fact]
  public void A_gui_rotation_leaves_hand_and_ground_as_they_were() {
    const string plain = "{\"itemViews\":{\"kind\":\"bar\"}}";
    ItemViewSet bare = ItemViews.Compute(World.Api, Barred(plain))!;
    ItemViewSet turned = ItemViews.Compute(
      World.Api,
      Barred("{\"itemViews\":{\"kind\":\"bar\",\"guiRotation\":[20,50,70]}}")
    )!;

    Assert.True(Same(bare.TpHand, turned.TpHand));
    Assert.True(Same(bare.Ground, turned.Ground));
    Assert.False(Same(bare.Gui, turned.Gui));
  }

  // Fails when the pass overwrites an authored GuiTransform with the computed view.
  [Fact]
  public void An_authored_gui_transform_survives_a_gui_rotation() {
    Item item = ItemOf("viewsfixture:rotatedauthored");

    Assert.Equal(2.5f, item.GuiTransform.ScaleXYZ.X);
    Assert.Equal(2f, item.GuiTransform.Translation.Y);
    Assert.Equal(0f, item.GuiTransform.Rotation.X);
  }

  // Fails when a block ignores the declared rotation or takes its scale from the game's: the partial block then
  // leaves its kind's slot span.
  [Fact]
  public void A_rotated_block_view_fills_its_kinds_span() {
    Block block = World.World.GetBlock(
      new AssetLocation("viewsfixture:rotatedblock")
    );
    ItemViewSet set = ItemViews.Compute(World.Api, block)!;
    CollectibleViews.ViewBox box = CollectibleViews.BoxOf(World.Api, block)!;
    ModelTransform tf = set.Gui;
    double[,] m = CollectibleViews.Rotation(
      new Vec3d(tf.Rotation.X + 180, tf.Rotation.Y, tf.Rotation.Z)
    );
    double[][] p =
    [
      .. Enumerable
        .Range(0, 8)
        .Select(i =>
        {
          Vec3d v = CollectibleViews.Mul(
            m,
            new Vec3d(
              ((i & 1) == 0 ? box.From.X : box.To.X) / 16 - tf.Origin.X,
              ((i & 2) == 0 ? box.From.Y : box.To.Y) / 16 - tf.Origin.Y,
              ((i & 4) == 0 ? box.From.Z : box.To.Z) / 16 - tf.Origin.Z
            )
          );
          return new[] { v.X, v.Y };
        }),
    ];
    double span =
      Math.Max(
        p.Max(q => q[0]) - p.Min(q => q[0]),
        p.Max(q => q[1]) - p.Min(q => q[1])
      )
      * tf.ScaleXYZ.X
      * ViewReferences.SlotItemPixels
      / ViewReferences.SlotPixels;

    Assert.Equal(30f, tf.Rotation.X);
    Assert.Equal(60f, tf.Rotation.Y);
    Assert.Equal(10f, tf.Rotation.Z);
    Assert.Equal(ViewReferences.PartialSlotSpan, span, 3);
  }

  // Fails when guiRotation is dropped from the written attribute.
  [Fact]
  public void Views_writes_the_gui_rotation_for_items_and_blocks() {
    JToken? item = ExItemDef
      .Create("d", "c")
      .Views(ItemViewKind.Bar, null, new Vec3d(1, 2, 3))
      .ToJson()["attributes"]!["itemViews"]!["guiRotation"];
    JToken? block = ExBlockDef
      .Create("d", "c")
      .Views(ItemViewKind.Partial, new Vec3d(4, 5, 6))
      .ToJson()["attributes"]!["itemViews"]!["guiRotation"];

    Assert.Equal([1d, 2d, 3d], item!.Select(t => (double)t));
    Assert.Equal([4d, 5d, 6d], block!.Select(t => (double)t));
  }

  // Fails when a malformed rotation is read as none, or the number check is dropped: a string or null entry then
  // throws a JSON exception, a numeral string is accepted, and the load pass dies on it.
  [Theory]
  [InlineData("[1,2]")]
  [InlineData("[\"a\",0,0]")]
  [InlineData("[1,null,3]")]
  [InlineData("[\"1\",\"2\",\"3\"]")]
  public void A_gui_rotation_that_is_not_three_numbers_is_refused(string value) {
    Assert.Throws<ArgumentException>(() =>
      ItemViews.Compute(
        World.Api,
        Barred(
          $"{{\"itemViews\":{{\"kind\":\"bar\",\"guiRotation\":{value}}}}}"
        )
      )
    );
  }
}
