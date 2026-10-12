using System;
using System.IO;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The drawn box of a collectible's shape and the kind its box gives it, on the whole
/// vanilla game with the views fixture mod.</summary>
[Collection("ItemViews")]
public class CollectibleViewsTests {
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

  private static CollectibleObject Coll(string code) =>
    World.World.GetItem(new AssetLocation(code));

  private static double[] ExtentsOf(string code) =>
    CollectibleViews.BoxOf(World.Api, Coll(code))!.Extents();

  private static ItemViewKind? KindOf(CollectibleObject collectible) =>
    CollectibleViews.KindOf(World.Api, collectible);

  private static CompositeShape? Shape(string? baseCode) =>
    baseCode == null
      ? null
      : new CompositeShape { Base = new AssetLocation(baseCode) };
  [Fact]
  public void Metalbit_boxes_its_drawn_faces() {
    double[] extents = ExtentsOf("game:nugget-hematite");
    Assert.Equal(2.5, extents[0], 3);
    Assert.Equal(1.5, extents[1], 3);
    Assert.Equal(1.5, extents[2], 3);
  }
  [Fact]
  public void Stick_boxes_in_its_least_volume_frame() {
    Assert.Equal(ItemViewKind.Bar, KindOf(Coll("game:stick")));
  }
  [Theory]
  [InlineData("game:spear-generic-iron", ItemViewKind.Longtool)]
  [InlineData("game:ingot-iron", ItemViewKind.Lump)]
  [InlineData("game:metalplate-iron", ItemViewKind.Plate)]
  [InlineData("game:rod-iron", ItemViewKind.Bar)]
  [InlineData("game:stick", ItemViewKind.Bar)]
  public void Reference_items_classify(string code, ItemViewKind kind) {
    Assert.Equal(kind, KindOf(Coll(code)));
  }
  [Fact]
  public void A_selection_boxes_only_the_elements_it_keeps() {
    var item = new Item {
      Code = new AssetLocation("viewsfixture:selected"),
      Shape = new CompositeShape {
        Base = new AssetLocation("game:item/nugget"),
        SelectiveElements = ["origin/native2"],
      },
    };
    double[] extents = CollectibleViews.BoxOf(World.Api, item)!.Extents();
    Assert.Equal(1.5, extents[0], 3);
    Assert.Equal(1.5, extents[1], 3);
    Assert.Equal(1.0, extents[2], 3);
  }
  [Fact]
  public void A_declared_kind_overrides_the_box() {
    Assert.Equal(ItemViewKind.Lump, KindOf(Coll("viewsfixture:declaredlump")));
    Assert.Equal(ItemViewKind.Bar, KindOf(Coll("game:stick")));
  }
  [Fact]
  public void A_declared_none_classifies_by_the_box() {
    var item = new Item {
      Code = new AssetLocation("viewsfixture:undeclared"),
      Shape = Shape("game:item/stick"),
      Attributes = new JsonObject(
        JToken.Parse("{\"itemViews\":{\"kind\":\"none\"}}")
      ),
    };
    Assert.Equal(ItemViewKind.Bar, KindOf(item));
  }
  [Fact]
  public void A_declared_kind_in_capitals_is_refused() {
    var item = new Item {
      Code = new AssetLocation("viewsfixture:capital"),
      Attributes = new JsonObject(
        JToken.Parse("{\"itemViews\":{\"kind\":\"Lump\"}}")
      ),
    };
    Assert.Throws<InvalidOperationException>(() => KindOf(item));
  }
  [Fact]
  public void A_work_item_is_a_workpiece_without_a_shape() {
    Assert.Equal(ItemViewKind.Workpiece, KindOf(new ItemWorkItem()));
  }
  [Fact]
  public void An_item_with_no_drawn_box_has_no_kind() {
    Assert.Null(KindOf(new Item()));
    Assert.Null(
      KindOf(new Item { Shape = Shape("viewsfixture:item/missing") })
    );
    Assert.Null(KindOf(new Item { Shape = Shape("viewsfixture:item/empty") }));
  }
  [Theory]
  [InlineData(
    "viewsfixture:block/offset",
    "game:block/basic/cube",
    ItemViewKind.Partial
  )]
  [InlineData(null, "game:block/basic/cube", ItemViewKind.Cube)]
  [InlineData(null, null, ItemViewKind.Cube)]
  public void A_block_boxes_its_inventory_shape_else_its_placed_one(
    string? inventory,
    string? placed,
    ItemViewKind kind
  ) {
    var block = new Block {
      Code = new AssetLocation("viewsfixture:block"),
      Shape = Shape(placed),
      ShapeInventory = Shape(inventory),
    };
    Assert.Equal(kind, KindOf(block));
  }
  [Fact]
  public void A_block_turns_its_inventory_shape_about_the_block_centre() {
    var block = new Block {
      Code = new AssetLocation("viewsfixture:turned"),
      ShapeInventory = new CompositeShape {
        Base = new AssetLocation("viewsfixture:block/offset"),
        rotateY = 90,
      },
    };
    CollectibleViews.ViewBox box = CollectibleViews.BoxOf(World.Api, block)!;
    Assert.Equal(0, box.From.X, 3);
    Assert.Equal(0, box.From.Y, 3);
    Assert.Equal(12, box.From.Z, 3);
    Assert.Equal(2, box.To.X, 3);
    Assert.Equal(2, box.To.Y, 3);
    Assert.Equal(16, box.To.Z, 3);
  }
  [Theory]
  [InlineData(16, 16, 16, ItemViewKind.Cube)]
  [InlineData(15, 15, 15, ItemViewKind.Cube)]
  [InlineData(48, 31, 28, ItemViewKind.Multicell)]
  [InlineData(16.6, 16, 16, ItemViewKind.Multicell)]
  [InlineData(16, 16, 14.9, ItemViewKind.Partial)]
  [InlineData(8, 8, 8, ItemViewKind.Partial)]
  public void Block_extents_classify(
    double l,
    double m,
    double s,
    ItemViewKind kind
  ) {
    Assert.Equal(kind, CollectibleViews.ByBox([l, m, s], true, false));
  }
  [Theory]
  [InlineData(32, 5, 1, false, ItemViewKind.Longtool)]
  [InlineData(10, 8, 2, true, ItemViewKind.Tool)]
  [InlineData(10, 8, 2, false, ItemViewKind.Plate)]
  [InlineData(12, 10, 10, false, ItemViewKind.Bulk)]
  [InlineData(30, 8, 8, false, ItemViewKind.Bar)]
  [InlineData(20, 8, 6, false, ItemViewKind.Lump)]
  public void Item_extents_classify(
    double l,
    double m,
    double s,
    bool tool,
    ItemViewKind kind
  ) {
    Assert.Equal(kind, CollectibleViews.ByBox([l, m, s], false, tool));
  }
}
