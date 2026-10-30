using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>What the finalize pass leaves on the collectibles of a mod that depends on exlib, on the whole
/// vanilla game with the views fixture mod.</summary>
[Collection("ItemViews")]
public class ItemViewsPassTests {
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

  private static string FaultFixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Helpers",
      "Views",
      "Fixtures",
      "viewsfaults"
    );

  private static TestWorld World => GameWorlds.Get([Fixture]);

  private static Item ItemOf(string code) =>
    World.World.GetItem(new AssetLocation(code));

  private static bool Same(ModelTransform a, ModelTransform b) =>
    a.Translation.Equals(b.Translation)
    && a.Rotation.Equals(b.Rotation)
    && a.Origin.Equals(b.Origin)
    && a.ScaleXYZ.Equals(b.ScaleXYZ);

  // Fails when the pass is skipped, and when an absent clip is read as null only: breakhand stays.
  [Fact]
  public void An_untransformed_long_item_gets_computed_views_and_clips() {
    Item item = ItemOf("viewsfixture:longshaft");

    ItemViewSet views = ItemViews.Compute(World.Api, item)!;

    Assert.Equal(ItemViewKind.Longtool, views.Kind);
    Assert.True(Same(views.Gui, item.GuiTransform));
    Assert.True(Same(views.TpHand, item.TpHandTransform));
    Assert.True(Same(views.Ground, item.GroundTransform));
    Assert.False(Same(ModelTransform.ItemDefaultGui(), item.GuiTransform));
    Assert.False(Same(ModelTransform.ItemDefaultTp(), item.TpHandTransform));
    Assert.False(
      Same(ModelTransform.ItemDefaultGround(), item.GroundTransform)
    );
    Assert.Equal("exlib-longcarry2", item.HeldRightTpIdleAnimation);
    Assert.Equal("exlib-longcarry2", item.HeldRightReadyAnimation);
    Assert.Equal("exlib-longcarry2", item.HeldTpHitAnimation);
    Assert.Equal("twohandplaceblock", item.HeldTpUseAnimation);
  }

  // Fails when an absent clip is read as null only: helditemready and interactstatic stay.
  [Fact]
  public void An_untransformed_block_gets_the_block_clips() {
    Block block = World.World.GetBlock(
      new AssetLocation("viewsfixture:offsetblock")
    );

    ItemViewSet views = ItemViews.Compute(World.Api, block)!;

    Assert.Equal(ViewReferences.BlockReady, block.HeldRightReadyAnimation);
    Assert.Equal(ViewReferences.BlockUse, block.HeldTpUseAnimation);
    Assert.True(Same(views.Gui, block.GuiTransform));
    Assert.False(Same(ModelTransform.BlockDefaultGui(), block.GuiTransform));
  }

  // Fails when the pass overwrites authored values.
  [Fact]
  public void An_authored_gui_is_kept_and_hand_and_ground_computed() {
    Item item = ItemOf("viewsfixture:authoredshaft");
    ItemViewSet views = ItemViews.Compute(World.Api, item)!;

    Assert.Equal(2.5f, item.GuiTransform.ScaleXYZ.X);
    Assert.Equal(2f, item.GuiTransform.Translation.Y);
    Assert.False(Same(views.Gui, item.GuiTransform));
    Assert.True(Same(views.TpHand, item.TpHandTransform));
    Assert.True(Same(views.Ground, item.GroundTransform));
  }

  // Fails when the domain filter is dropped: a vanilla item still at the game's defaults is computed.
  [Fact]
  public void Game_items_are_untouched() {
    Item[] atDefaults =
    [
      .. World
        .World.Items.Where(i =>
          i?.Code?.Domain == "game"
          && Same(ModelTransform.ItemDefaultGui(), i.GuiTransform)
          && !CollectibleViews.OptedOut(i)
        )
        .Where(i =>
        {
          ItemViewSet? v = ItemViews.Compute(World.Api, i);
          return v != null && !Same(v.Gui, ModelTransform.ItemDefaultGui());
        }),
    ];

    Assert.NotEmpty(atDefaults);
  }

  // Fails when none is read as no declaration.
  [Fact]
  public void Kind_none_keeps_the_defaults() {
    Item item = ItemOf("viewsfixture:noneshaft");

    Assert.True(Same(ModelTransform.ItemDefaultGui(), item.GuiTransform));
    Assert.True(Same(ModelTransform.ItemDefaultTp(), item.TpHandTransform));
    Assert.True(Same(ModelTransform.ItemDefaultGround(), item.GroundTransform));
    Assert.Equal("breakhand", item.HeldTpHitAnimation);
    Assert.Equal("helditemready", item.HeldRightReadyAnimation);
    Assert.Null(item.HeldRightTpIdleAnimation);
  }

  // Fails when a declaration that does not parse stops the pass, when a shape that does not load is not
  // warned about, or when the pass reports a loaded shape as missing.
  [Fact]
  public void A_declaration_that_does_not_parse_leaves_its_item_and_the_pass_goes_on() {
    using var world = new TestWorld();
    world.LoadAsGame([Fixture, FaultFixture]);
    world.Log.Expect(EnumLogType.Error, "viewsfaults:badkind");
    world.Log.Expect(EnumLogType.Error, "viewsfaults:badtip");
    world.Log.Expect(EnumLogType.Error, "viewsfaults:badheld");
    world.Log.Expect(EnumLogType.Warning, "viewsfaults:missingshape");

    Assert.Contains(
      world.Log.Errors,
      e => e.Contains("viewsfaults:badkind") && e.Contains("'Longtool'")
    );
    Assert.Contains(
      world.Log.Errors,
      e => e.Contains("viewsfaults:badtip") && e.Contains("'+w'")
    );
    Assert.Contains(
      world.Log.Errors,
      e => e.Contains("viewsfaults:badheld") && e.Contains("'visible'")
    );
    Assert.Contains(
      world.Log.Warnings,
      w =>
        w.Contains("viewsfaults:missingshape")
        && w.Contains("viewsfixture:item/nowhere")
    );
    Assert.DoesNotContain(
      world.Log.Warnings,
      w => w.Contains("viewsfixture:longshaft")
    );
    foreach (
      string code in new[] { "badkind", "badtip", "badheld", "missingshape" }
    )
      Assert.True(
        Same(
          ModelTransform.ItemDefaultGui(),
          world
            .World.GetItem(new AssetLocation("viewsfaults:" + code))
            .GuiTransform
        )
      );
    Assert.False(
      Same(
        ModelTransform.ItemDefaultGui(),
        world
          .World.GetItem(new AssetLocation("viewsfixture:longshaft"))
          .GuiTransform
      )
    );
  }
}
