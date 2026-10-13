using System;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>The views <see cref="ItemViews.Compute(Vintagestory.API.Common.ICoreAPI, CollectibleObject)"/>
/// computes for blocks and for items that draw a game item's shape, on the whole vanilla game.</summary>
[Collection("ItemViews")]
public class ItemViewsBlockTests(ITestOutputHelper output) {
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

  private static CollectibleViews.ViewBox Box(Block block) =>
    CollectibleViews.BoxOf(World.Api, block)!;

  // The largest projected extent of the box corners over the slot's 48 pixels, with the block slot matrix: no item shift
  // and 180 added to the x rotation.
  private static double SlotSpan(
    CollectibleViews.ViewBox box,
    ModelTransform tf
  ) {
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
    double w = p.Max(q => q[0]) - p.Min(q => q[0]);
    double h = p.Max(q => q[1]) - p.Min(q => q[1]);
    return 25.6 * tf.ScaleXYZ.X * Math.Max(w, h) / 48;
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

  // The ground pose: the lowest drawn point under it, which rests on y 0.
  private static double GroundRest(
    CollectibleViews.ViewBox box,
    ModelTransform tf
  ) {
    double[,] turn = CollectibleViews.Mul(
      CollectibleViews.Mul(
        CollectibleViews.Rotation(new Vec3d(0, tf.Rotation.Y, 0)),
        CollectibleViews.Rotation(new Vec3d(0, 0, tf.Rotation.Z))
      ),
      CollectibleViews.Rotation(new Vec3d(tf.Rotation.X, 0, 0))
    );
    return box
      .Drawn.SelectMany(c => c)
      .Min(c =>
        tf.Translation.Y
        + tf.ScaleXYZ.X
          * 0.2
          * CollectibleViews
            .Mul(
              turn,
              new Vec3d(
                c.X / 16 - tf.Origin.X,
                c.Y / 16 - tf.Origin.Y,
                c.Z / 16 - tf.Origin.Z
              )
            )
            .Y
      );
  }

  // Fails when a block median in ViewReferences moves 20%: the partial's slot, ground or held extent, or
  // the multicell's, and the held-out block then leaves its kind's median.
  [Theory]
  [InlineData("game:forge", ItemViewKind.Partial, 0.7101, 0.300, 0.400)]
#if GAME_GE_1_22
  [InlineData(
    "game:bellows-crude-north",
    ItemViewKind.Multicell,
    0.8884,
    0.8629,
    1.200
  )]
#endif
  public void Held_out_blocks_meet_their_kind_medians(
    string code,
    ItemViewKind kind,
    double span,
    double ground,
    double held
  ) {
    Block block = World.World.GetBlock(new AssetLocation(code));
    CollectibleViews.ViewBox box = Box(block);
    double longest = box.Extents()[0] / 16;
    ItemViewSet set = ItemViews.Compute(World.Api, block)!;
    double gotSpan = SlotSpan(box, set.Gui);
    double gotGround = set.Ground.ScaleXYZ.X * 0.2 * longest;
    double gotHeld = set.TpHand.ScaleXYZ.X * longest;
    output.WriteLine(
      $"{code} {box.Extents()[0]:F2} x {box.Extents()[1]:F2} x {box.Extents()[2]:F2}: span {gotSpan:F3} (median {span}), ground {gotGround:F3} (median {ground}), held {gotHeld:F3} (median {held})"
    );
    Assert.Equal(kind, set.Kind);
    Assert.InRange(gotSpan, span * 0.95, span * 1.05);
    Assert.InRange(gotGround, ground * 0.95, ground * 1.05);
    Assert.InRange(gotHeld, held * 0.95, held * 1.05);
  }

  // Fails when the cube's scales are not the game's, or its slot origin is off the drawn box centre.
  [Fact]
  public void A_held_out_cube_keeps_the_default_scales() {
    Block block = World.World.GetBlock(new AssetLocation("game:plaster-plain"));
    ItemViewSet set = ItemViews.Compute(World.Api, block)!;
    Assert.Equal(ItemViewKind.Cube, set.Kind);
    Assert.Equal(
      ModelTransform.BlockDefaultGui().ScaleXYZ.X,
      set.Gui.ScaleXYZ.X
    );
    Assert.Equal(
      ModelTransform.BlockDefaultTp().ScaleXYZ.X,
      set.TpHand.ScaleXYZ.X
    );
    Assert.Equal(
      ModelTransform.BlockDefaultGround().ScaleXYZ.X,
      set.Ground.ScaleXYZ.X
    );
    Assert.Equal(0.5, set.Gui.Origin.X, 3);
    Assert.Equal(0, GroundRest(Box(block), set.Ground), 3);
    CollectibleViews.ViewBox box = Box(block);
    double longest = box.Extents()[0] / 16;
    output.WriteLine(
      $"game:plaster-plain {box.Extents()[0]:F2} x {box.Extents()[1]:F2} x {box.Extents()[2]:F2}: span {SlotSpan(box, set.Gui):F3}, ground {set.Ground.ScaleXYZ.X * 0.2 * longest:F3}, held {set.TpHand.ScaleXYZ.X * longest:F3}"
    );
  }

  // Fails when a block's held clips are not the game's block clips: BlockReady, BlockUse or the idle clip changed, or
  // a hit clip set.
  [Theory]
  [InlineData("game:forge")]
  [InlineData("game:plaster-plain")]
  public void A_block_is_held_with_the_block_clips(string code) {
    ItemViewSet set = ItemViews.Compute(
      World.Api,
      World.World.GetBlock(new AssetLocation(code))
    )!;
    Assert.Equal("holdbothhandslarge", set.IdleClip);
    Assert.Equal("heldblockready", set.ReadyClip);
    Assert.Null(set.HitClip);
    Assert.Equal("twohandplaceblock", set.UseClip);
  }

  // Fails when the ground translation is not taken from the drawn bottom (the forge floats or sinks), or the
  // slot origin is off the drawn box centre.
  [Theory]
  [InlineData("game:forge")]
#if GAME_GE_1_22
  [InlineData("game:bellows-crude-north")]
#endif
  [InlineData("game:plaster-plain")]
  public void A_block_sits_on_its_box(string code) {
    Block block = World.World.GetBlock(new AssetLocation(code));
    CollectibleViews.ViewBox box = Box(block);
    ItemViewSet set = ItemViews.Compute(World.Api, block)!;
    Assert.Equal(0, GroundRest(box, set.Ground), 3);
    Assert.Equal((box.From.X + box.To.X) / 32, set.Gui.Origin.X, 3);
    Assert.Equal((box.From.Y + box.To.Y) / 32, set.Gui.Origin.Y, 3);
    Assert.Equal((box.From.Z + box.To.Z) / 32, set.Gui.Origin.Z, 3);
  }

  // Fails when the cube's ground translation is dropped: the lifted bottom then sits 0.5 voxel above the ground.
  [Fact]
  public void A_cube_with_a_lifted_bottom_rests_on_the_ground() {
    var block = new Block {
      Code = new AssetLocation("viewsfixture:lifted"),
      Shape = new CompositeShape {
        Base = new AssetLocation("viewsfixture:block/lifted"),
      },
    };
    CollectibleViews.ViewBox box = Box(block);
    ItemViewSet set = ItemViews.Compute(World.Api, block)!;
    Assert.Equal(0.5, box.From.Y, 3);
    Assert.Equal(ItemViewKind.Cube, set.Kind);
    Assert.Equal(0, GroundRest(box, set.Ground), 3);
  }

  // Fails when the hand translation is not the one that puts the box centre where the default puts a cube's.
  [Theory]
  [InlineData("game:forge")]
#if GAME_GE_1_22
  [InlineData("game:bellows-crude-north")]
#endif
  public void A_block_is_carried_by_its_centre(string code) {
    Block block = World.World.GetBlock(new AssetLocation(code));
    CollectibleViews.ViewBox box = Box(block);
    ModelTransform tp = ItemViews.Compute(World.Api, block)!.TpHand;
    ModelTransform game = ModelTransform.BlockDefaultTp();
    Assert.Equal(
      (
        0.5
        + game.ScaleXYZ.X * game.Translation.X
        - (box.From.X + box.To.X) / 32
      ) / tp.ScaleXYZ.X,
      tp.Translation.X,
      3
    );
    Assert.Equal(
      (
        0.5
        + game.ScaleXYZ.X * game.Translation.Y
        - (box.From.Y + box.To.Y) / 32
      ) / tp.ScaleXYZ.X,
      tp.Translation.Y,
      3
    );
    Assert.Equal(
      (
        0.5
        + game.ScaleXYZ.X * game.Translation.Z
        - (box.From.Z + box.To.Z) / 32
      ) / tp.ScaleXYZ.X,
      tp.Translation.Z,
      3
    );
  }

  // Fails when the leaf rule is dropped, or either of its thin-axis branches: a slab then stands on its edge on the
  // ground, the ns pane thin in z and the ew pane thin in x.
  [Theory]
  [InlineData("game:glasspane-leaded-aged-ns")]
  [InlineData("game:glasspane-leaded-aged-ew")]
  public void A_leaf_lies_on_its_largest_face(string code) {
    Block slab = World.World.GetBlock(new AssetLocation(code));
    ItemViewSet set = ItemViews.Compute(World.Api, slab)!;
    double[] ext = Box(slab).Extents();
    double[,] turn = CollectibleViews.Mul(
      CollectibleViews.Mul(
        CollectibleViews.Rotation(new Vec3d(0, set.Ground.Rotation.Y, 0)),
        CollectibleViews.Rotation(new Vec3d(0, 0, set.Ground.Rotation.Z))
      ),
      CollectibleViews.Rotation(new Vec3d(set.Ground.Rotation.X, 0, 0))
    );
    double[] ys =
    [
      .. Box(slab)
        .Drawn.SelectMany(c => c)
        .Select(c => CollectibleViews.Mul(turn, c).Y),
    ];
    double height = ys.Max() - ys.Min();
    Assert.Equal(ext[2], height, 1);
  }

  // Fails when the vanilla-shape rule is skipped: greensand gets a plate's views, held on edge.
  [Fact]
  public void Greensand_takes_flours_views() {
    Item greensand = World.World.GetItem(
      new AssetLocation("viewsfixture:greensand")
    );
    Item flour = World.World.GetItem(new AssetLocation("game:flour-spelt"));
    ItemViewSet set = ItemViews.Compute(World.Api, greensand)!;
    Assert.Equal(Numbers(flour.GuiTransform), Numbers(set.Gui));
    Assert.Equal(Numbers(flour.TpHandTransform), Numbers(set.TpHand));
    Assert.Equal(Numbers(flour.GroundTransform), Numbers(set.Ground));
    Assert.Equal(flour.HeldRightTpIdleAnimation, set.IdleClip);
    Assert.Equal(flour.HeldRightReadyAnimation, set.ReadyClip);
    Assert.NotNull(set.IdleClip);
  }

  // Fails when the tool exemption returns: the pickaxe-shaped fixture then takes the carrier's held views.
  [Fact]
  public void A_tool_that_draws_a_game_shape_takes_its_shipped_views() {
    Item pick = World.World.GetItem(
      new AssetLocation("viewsfixture:pickaxefixture")
    );
    Item pickaxe = World.World.GetItem(
      new AssetLocation("game:pickaxe-bismuthbronze")
    );
    ItemViewSet set = ItemViews.Compute(World.Api, pick)!;
    Assert.Equal(Numbers(pickaxe.GuiTransform), Numbers(set.Gui));
    Assert.Equal(Numbers(pickaxe.TpHandTransform), Numbers(set.TpHand));
    Assert.Equal(Numbers(pickaxe.GroundTransform), Numbers(set.Ground));
    Assert.Equal(pickaxe.HeldRightTpIdleAnimation, set.IdleClip);
  }

  // Fails when the twin's default ready clip is passed through: plainbar reports "helditemready".
  [Fact]
  public void A_twin_with_the_default_ready_clip_leaves_it_unset() {
    Item bar = World.World.GetItem(new AssetLocation("viewsfixture:plainbar"));
    Item twin = World.World.GetItem(new AssetLocation("game:ingot-iron"));
    Assert.Equal("helditemready", twin.HeldRightReadyAnimation);
    Assert.Null(ItemViews.Compute(World.Api, bar)!.ReadyClip);
  }
}
