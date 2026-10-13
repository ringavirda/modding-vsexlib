using System;
using System.IO;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The view matrices and the seraph pose, on the whole vanilla game with the views fixture mod.</summary>
[Collection("ItemViews")]
public class ViewMatricesTests {
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

  private static ModelTransform Plain(
    double[]? rotation = null,
    double[]? origin = null
  ) {
    ModelTransform tf = ModelTransform.NoTransform;
    tf.Origin = new FastVec3f(
      (float)(origin?[0] ?? 0),
      (float)(origin?[1] ?? 0),
      (float)(origin?[2] ?? 0)
    );
    tf.Rotation = new FastVec3f(
      (float)(rotation?[0] ?? 0),
      (float)(rotation?[1] ?? 0),
      (float)(rotation?[2] ?? 0)
    );
    return tf;
  }

  // Fails when a clip is posed at rest (its easing factor left at 0) or the anchor is read at its element centre
  // instead of its From corner. The positions are fk.py's, in voxels.
  [Theory]
  [InlineData("spearidle-fp", 6.63, 16.62, -2.61)]
  [InlineData("holdbothhandslarge", -0.01, 16.17, 5.10)]
  public void Right_hand_at_a_clip_is_where_fk_puts_it(
    string clip,
    double x,
    double y,
    double z
  ) {
    Vec3d p = SeraphPose.At(World.Api, clip).ItemAnchor.Position;
    Assert.Equal(x, p.X * 16, 0.05);
    Assert.Equal(y, p.Y * 16, 0.05);
    Assert.Equal(z, p.Z * 16, 0.05);
  }

  // Fails when a pose is built again per call: the same clip then gives a new object.
  [Fact]
  public void A_clip_is_posed_once_per_api() {
    Assert.Same(
      SeraphPose.At(World.Api, "spearidle"),
      SeraphPose.At(World.Api, "spearidle")
    );
    Assert.NotSame(
      SeraphPose.At(World.Api, "spearidle"),
      SeraphPose.At(World.Api, null)
    );
  }

  // Fails when the anchor is read at its element centre instead of its From corner.
  [Fact]
  public void Right_hand_at_rest_is_where_fk_puts_it() {
    Vec3d p = SeraphPose.At(World.Api, null).ItemAnchor.Position;
    Assert.Equal(8.71, p.X * 16, 0.05);
    Assert.Equal(10.67, p.Y * 16, 0.05);
    Assert.Equal(3.51, p.Z * 16, 0.05);
  }

  // Fails when S(s) is left out of the hand matrix: the spear is then 2.92 block long.
  [Fact]
  public void Iron_spear_is_held_2_34_block_long() {
    CollectibleObject spear = Coll("game:spear-generic-iron");
    CollectibleViews.ViewBox box = CollectibleViews.BoxOf(World.Api, spear)!;
    Mat4 hand = ViewMatrices.Hand(
      ViewMatrices.Transform(spear, ViewTarget.Hand),
      SeraphPose.At(World.Api, "spearidle")
    );
    int axis = LongestAxis(box);
    Vec3d a = hand.Apply(AxisEnd(box, axis, 0));
    Vec3d b = hand.Apply(AxisEnd(box, axis, 1));
    Assert.Equal(2.34, (a - b).Length(), 0.02);
  }

  // Fails when the game's resolution of an omitted key is not the type default: the origin is then 0.
  [Fact]
  public void Omitted_transform_keys_keep_the_type_default() {
    ModelTransform expected = ModelTransform.ItemDefaultGui();
    ModelTransform gui = ViewMatrices.Transform(
      Coll("viewsfixture:lonescale"),
      ViewTarget.Gui
    );
    Assert.Equal(expected.Translation, gui.Translation);
    Assert.Equal(expected.Rotation, gui.Rotation);
    Assert.Equal(expected.Origin, gui.Origin);
    Assert.Equal(new FastVec3f(1.3f, 1.3f, 1.3f), gui.ScaleXYZ);
  }

  // Fails when a transform without keys is not the type default, or the block and item defaults swap.
  [Fact]
  public void A_collectible_without_transforms_takes_its_type_default() {
    Assert.Equal(
      ModelTransform.ItemDefaultTp().ScaleXYZ,
      ViewMatrices.Transform(new Item(), ViewTarget.Hand).ScaleXYZ
    );
    Assert.Equal(
      ModelTransform.BlockDefaultGround().ScaleXYZ,
      ViewMatrices.Transform(new Block(), ViewTarget.Ground).ScaleXYZ
    );
    Assert.False(
      ViewMatrices
        .Transform(Coll("viewsfixture:lonescale"), ViewTarget.Gui)
        .Rotate
    );
    Assert.False(
      ViewMatrices
        .Transform(Coll("game:metalplate-iron"), ViewTarget.Gui)
        .Rotate
    );
  }

  // Fails when the item shift of (-3, -1) is dropped, or applied to a block.
  [Fact]
  public void An_item_sits_3_px_left_and_1_up_of_the_slot_centre() {
    ModelTransform tf = Plain(origin: [0.5, 0.5, 0.5]);
    Vec3d centre = new(0.5, 0.5, 0.5);
    Vec3d item = ViewMatrices.Slot(tf, false).Apply(centre);
    Vec3d block = ViewMatrices.Slot(tf, true).Apply(centre);
    Assert.Equal(-2.5, item.X, 6);
    Assert.Equal(-0.5, item.Y, 6);
    Assert.Equal(0.5, block.X, 6);
    Assert.Equal(0.5, block.Y, 6);
  }

  // Fails when a block's extra 180 degrees about x is dropped: its top then points down the screen.
  [Fact]
  public void A_block_turns_180_about_x_in_the_slot() {
    ModelTransform tf = Plain(origin: [0.5, 0.5, 0.5]);
    Vec3d top = new(0.5, 1, 0.5);
    Assert.Equal(-12.3, ViewMatrices.Slot(tf, true).Apply(top).Y, 6);
    Assert.Equal(12.3, ViewMatrices.Slot(tf, false).Apply(top).Y, 6);
  }

  // Fails when the GUI scale is left out of the slot size, or the translation is not scaled with it.
  [Fact]
  public void The_slot_scales_with_the_gui_scale() {
    ModelTransform tf = Plain(origin: [0, 0, 0]);
    tf.Translation = new FastVec3f(10, 0, 0);
    Vec3d p = ViewMatrices.Slot(tf, true, 2).Apply(new Vec3d(1, 0, 0));
    Assert.Equal(20 + 51.2, p.X, 6);
  }

  // Fails when the ground rotates in the hand's order (x, y, z in call order) instead of y, z, x.
  [Fact]
  public void The_ground_turns_about_y_then_z_then_x() {
    ModelTransform tf = Plain(rotation: [90, 0, 90]);
    tf.ScaleXYZ = new FastVec3f(1, 1, 1);
    Vec3d p = ViewMatrices.Ground(tf).Apply(new Vec3d(0, 1, 0));
    Assert.Equal(0, p.X, 6);
    Assert.Equal(0, p.Y, 6);
    Assert.Equal(0.2, p.Z, 6);
  }

  // Fails when the jitter is not added to the scale, or the yaw not to the rotation about y.
  [Fact]
  public void The_ground_adds_the_random_scale_and_yaw() {
    ModelTransform tf = Plain();
    tf.ScaleXYZ = new FastVec3f(1, 1, 1);
    Vec3d p = ViewMatrices.Ground(tf, 90, 0.05).Apply(new Vec3d(1, 0, 0));
    Assert.Equal(0, p.X, 6);
    Assert.Equal(-0.25, p.Z, 6);
  }

  // Fails when the pitch follow is left out, its factor ignored, or the height offset dropped.
  [Fact]
  public void The_first_person_hand_follows_the_pitch_and_rises_by_the_offset() {
    SeraphPose pose = SeraphPose.At(World.Api, "spearidle-fp");
    ModelTransform tf = Plain();
    Vec3d mesh = new();
    Vec3d level = ViewMatrices.HandFp(tf, pose).Apply(mesh);
    Vec3d fixedPoint = ViewMatrices.Hand(tf, pose).Apply(mesh);
    Assert.Equal(fixedPoint.X - 0.5, level.X, 6);
    Assert.Equal(fixedPoint.Y, level.Y, 6);
    Assert.Equal(fixedPoint.Z - 0.5, level.Z, 6);

    Vec3d tilted = ViewMatrices
      .HandFp(tf, pose, ViewMatrices.LevelPitch + Math.PI / 2, 0.5)
      .Apply(mesh);
    Vec3d eye = new(0, ViewMatrices.EyeHeight, 0);
    double before = Math.Atan2(level.Y - eye.Y, level.X - eye.X);
    double after = Math.Atan2(tilted.Y - eye.Y, tilted.X - eye.X);
    Assert.Equal(Math.PI / 4, after - before, 6);

    Vec3d raised = ViewMatrices.HandFp(tf, pose, yOffset: 0.3).Apply(mesh);
    Assert.Equal(level.Y + 0.3, raised.Y, 6);
  }

  // Fails when the clip is not looked up: the rest pose and an unknown clip both pass.
  [Fact]
  public void A_clip_the_shape_lacks_is_refused() {
    Assert.Throws<ArgumentException>(() =>
      SeraphPose.At(World.Api, "nosuchclip")
    );
  }

  private static int LongestAxis(CollectibleViews.ViewBox box) {
    double[] e =
    [
      box.To.X - box.From.X,
      box.To.Y - box.From.Y,
      box.To.Z - box.From.Z,
    ];
    return Array.IndexOf(e, Math.Max(e[0], Math.Max(e[1], e[2])));
  }

  private static Vec3d AxisEnd(CollectibleViews.ViewBox box, int axis, int end) {
    double[] from = [box.From.X, box.From.Y, box.From.Z];
    double[] to = [box.To.X, box.To.Y, box.To.Z];
    double[] c = new double[3];
    for (int i = 0; i < 3; i++)
      c[i] = i == axis ? (end == 0 ? from[i] : to[i]) : (from[i] + to[i]) / 2;
    return new Vec3d(
        box.Axes[0].X * c[0] + box.Axes[1].X * c[1] + box.Axes[2].X * c[2],
        box.Axes[0].Y * c[0] + box.Axes[1].Y * c[1] + box.Axes[2].Y * c[2],
        box.Axes[0].Z * c[0] + box.Axes[1].Z * c[1] + box.Axes[2].Z * c[2]
      ) / 16;
  }
}
