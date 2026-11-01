using System;
using System.IO;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><c>attributes.itemViews.held: "hidden"</c> through the load pass, the explicit API and gate G18, on the views fixture mod.</summary>
[Collection("ItemViews")]
public class ItemViewsHiddenHoldTests {
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

  private static Item HiddenItem =>
    World.World.GetItem(new AssetLocation("viewsfixture:hiddenlump"));

  private static Block HiddenBlock =>
    World.World.GetBlock(new AssetLocation("viewsfixture:hiddenblock"));

  private static Item Unhidden(Item hidden) =>
    new() {
      Code = hidden.Code,
      Shape = hidden.Shape,
      Attributes = new JsonObject(
        JToken.Parse("{\"itemViews\":{\"kind\":\"lump\"}}")
      ),
    };

  private static Block Unhidden(Block hidden) =>
    new() {
      Code = hidden.Code,
      Shape = hidden.Shape,
      Attributes = new JsonObject(JToken.Parse("{\"itemViews\":{}}")),
    };

  private static bool Same(ModelTransform a, ModelTransform b) =>
    ViewGates.TransformKey(a) == ViewGates.TransformKey(b);

  private static void AssertEmptyHands(CollectibleObject c) {
    Assert.Equal(0, c.TpHandTransform.ScaleXYZ.X);
    Assert.Equal(0, c.FpHandTransform.ScaleXYZ.Y);
    Assert.Equal(0, c.TpOffHandTransform.ScaleXYZ.Z);
    Assert.Null(c.HeldRightTpIdleAnimation);
    Assert.Null(c.HeldRightReadyAnimation);
  }

  // Fails when the hidden branch of Compute is ignored: the hand keeps scale 1 and the item keeps its authored idle clip.
  [Fact]
  public void A_hidden_item_draws_nothing_in_either_hand_and_keeps_its_slot_and_ground() {
    Item item = HiddenItem;
    ItemViewSet plain = ItemViews.Compute(World.Api, Unhidden(item))!;

    AssertEmptyHands(item);
    Assert.True(Same(plain.Gui, item.GuiTransform));
    Assert.True(Same(plain.Ground, item.GroundTransform));
    Assert.False(Same(plain.TpHand, item.TpHandTransform));
    Assert.True(ItemViews.Compute(World.Api, item)!.Hidden);
    Assert.False(plain.Hidden);
  }

  // Fails when the hidden branch of Compute is ignored for a block.
  [Fact]
  public void A_hidden_block_draws_nothing_in_either_hand_and_keeps_its_slot_and_ground() {
    Block block = HiddenBlock;
    ItemViewSet plain = ItemViews.Compute(World.Api, Unhidden(block))!;

    AssertEmptyHands(block);
    Assert.True(Same(plain.Gui, block.GuiTransform));
    Assert.True(Same(plain.Ground, block.GroundTransform));
    Assert.False(Same(plain.TpHand, block.TpHandTransform));
  }

  // Fails when the value check is dropped: an unknown hold is then read as no declaration.
  [Fact]
  public void An_unknown_held_value_throws_naming_it() {
    Item item = Unhidden(HiddenItem);
    item.Attributes = new JsonObject(
      JToken.Parse("{\"itemViews\":{\"kind\":\"lump\",\"held\":\"visible\"}}")
    );

    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      ItemViews.Compute(World.Api, item)
    );

    Assert.Contains("'visible'", e.Message);
    Assert.Contains("viewsfixture:hiddenlump", e.Message);
  }

  // Fails when Views replaces the whole itemViews object and drops the hold, or HiddenInHand drops the kind.
  [Fact]
  public void HiddenInHand_and_Views_keep_each_others_entries_in_either_order() {
    JToken HeldOf(ExItemDef d) => d.ToJson()["attributes"]!["itemViews"]!;

    JToken first = HeldOf(
      ExItemDef
        .Create("viewsfixture", "a", "a")
        .HiddenInHand()
        .Views(ItemViewKind.Bar, "+x")
    );
    JToken second = HeldOf(
      ExItemDef
        .Create("viewsfixture", "b", "b")
        .Views(ItemViewKind.Bar, "+x")
        .HiddenInHand()
    );

    foreach (JToken views in new[] { first, second }) {
      Assert.Equal("hidden", (string?)views["held"]);
      Assert.Equal("bar", (string?)views["kind"]);
      Assert.Equal("+x", (string?)views["tip"]);
    }
    foreach (
      ExBlockDef block in new[]
      {
        ExBlockDef
          .Create("viewsfixture", "c", "c")
          .Views(ItemViewKind.Partial)
          .HiddenInHand(),
        ExBlockDef
          .Create("viewsfixture", "d", "d")
          .HiddenInHand()
          .Views(ItemViewKind.Partial),
      }
    ) {
      JToken views = block.ToJson()["attributes"]!["itemViews"]!;
      Assert.Equal("hidden", (string?)views["held"]);
      Assert.Equal("partial", (string?)views["kind"]);
    }
  }

  // Fails when the twin test is the old Exists check: a lone held key then drops the game item's gui and ground views.
  [Fact]
  public void A_hidden_item_drawing_a_game_items_shape_keeps_that_items_slot_and_ground() {
    Item plain = World.World.GetItem(
      new AssetLocation("viewsfixture:plainbar")
    );
    Item hidden = new() {
      Code = new AssetLocation("viewsfixture", "hiddenbar"),
      Shape = plain.Shape,
      Attributes = new JsonObject(
        JToken.Parse("{\"itemViews\":{\"held\":\"hidden\"}}")
      ),
    };

    ItemViewSet twin = ItemViews.Compute(World.Api, plain)!;
    ItemViewSet set = ItemViews.Compute(World.Api, hidden)!;

    Assert.True(set.Hidden);
    Assert.True(Same(twin.Gui, set.Gui));
    Assert.True(Same(twin.Ground, set.Ground));
  }

  // Fails when G18 always passes: a hand that draws the item at scale 1 raises no finding.
  [Fact]
  public void G18_passes_a_hidden_group_and_fails_one_that_still_draws() {
    ViewGroup group = ViewGates
      .Groups(World, "viewsfixture")
      .Single(g => g.Codes.Contains("viewsfixture:hiddenlump"));

    GateReading[] rules = [.. ViewGates.Rules(World, group)];

    Assert.True(group.Hidden);
    Assert.All(rules, r => Assert.True(r.Pass, r.Detail));
    Assert.Equal(2, rules.Count(r => r.Gate == "G18"));
    Assert.DoesNotContain(rules, r => r.Gate == "G9");

    ViewGroup drawn = group with { Hand = ModelTransform.ItemDefaultTp() };
    GateReading fail = ViewGates
      .Rules(World, drawn)
      .Single(r => r.Gate == "G18" && r.Target == "tp");
    Assert.False(fail.Pass);
    Assert.True(fail.Value > 0);
  }

  // Fails when G18 reads only the scale: a hand at scale 0 whose representative still names an idle clip raises a finding.
  [Fact]
  public void G18_fails_a_hidden_hand_whose_representative_names_a_held_clip() {
    ViewGroup group = ViewGates
      .Groups(World, "viewsfixture")
      .Single(g => g.Codes.Contains("viewsfixture:hiddenlump"));
    Item clipped = new() {
      Code = group.Representative.Code,
      HeldRightTpIdleAnimation = "holdbothhandslarge",
    };
    ModelTransform none = ItemViews.HiddenHand;

    ViewGroup drawn = group with { Representative = clipped, Hand = none };
    GateReading fail = ViewGates
      .Rules(World, drawn)
      .Single(r => r.Gate == "G18" && r.Target == "tp");

    Assert.False(fail.Pass);
    Assert.Equal(0, fail.Value);
  }

  // Fails when the Longtool and Bulk kind rules of G11 run on a hidden group: the cleared clips are then reported.
  [Theory]
  [InlineData(ItemViewKind.Longtool)]
  [InlineData(ItemViewKind.Bulk)]
  public void G11_passes_a_hidden_group_of_a_kind_that_demands_clips(
    ItemViewKind kind
  ) {
    ViewGroup group = ViewGates
      .Groups(World, "viewsfixture")
      .Single(g => g.Codes.Contains("viewsfixture:hiddenlump"));

    GateReading clips = ViewGates
      .Rules(World, group with { Kind = kind })
      .Single(r => r.Gate == "G11");

    Assert.True(clips.Pass, clips.Detail);
  }
}
