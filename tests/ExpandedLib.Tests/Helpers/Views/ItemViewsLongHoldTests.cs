using System;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The two-handed hold <see cref="ItemViews.Compute(Vintagestory.API.Common.ICoreAPI, CollectibleObject)"/>
/// gives a long item, on the eight long pipes of the views long fixture mod and a vanilla door.</summary>
[Collection("ItemViews")]
public class ItemViewsLongHoldTests {
  private static string Fixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Helpers",
      "Views",
      "Fixtures",
      "viewslong"
    );

  private static TestWorld World => GameWorlds.Get([Fixture]);

  private const string Pipes = "viewslong:";

  private static CollectibleObject Pipe(string stem) =>
    stem.StartsWith("pipe")
      ? World.World.GetBlock(new AssetLocation(Pipes + stem))
      : World.World.GetItem(new AssetLocation(Pipes + stem));

  // The ItemAnchor of holdbothhandslarge frame 0 on the 1.22 player shape: a column per anchor axis, in the player's
  // voxel frame (x back to front -1, y up, z right to left).
  private static readonly double[,] Anchor =
  {
    { 0.22609094703387927, 0.9612344097067841, 0.15783311840350134 },
    { -0.9428172527290073, 0.1752006950152712, 0.28354954491709694 },
    { 0.2449051073902919, -0.2129157722241732, 0.9458796764453361 },
  };

  private static Vec3d HeldDirection(ModelTransform tf, Vec3d v) =>
    CollectibleViews.Mul(
      Anchor,
      CollectibleViews.Mul(
        CollectibleViews.Rotation(
          new Vec3d(tf.Rotation.X, tf.Rotation.Y - 180, tf.Rotation.Z)
        ),
        v
      )
    );

  private static void Near(double expected, double actual) =>
    Assert.InRange(actual, expected - 1e-4, expected + 1e-4);

  private static Vec3d LongAxis(ItemViewSet set) {
    double[] ext =
    [
      set.BoxTo!.X - set.BoxFrom!.X,
      set.BoxTo.Y - set.BoxFrom.Y,
      set.BoxTo.Z - set.BoxFrom.Z,
    ];
    return set.BoxAxes![Array.IndexOf(ext, ext.Max())];
  }

  public static TheoryData<string, int, double> Straights =>
    new()
    {
      { "pipe-cast-straight2", 2, -0.825 },
      { "pipe-castsmall-straight2", 2, -0.825 },
      { "castpipe-straight2", 2, -0.825 },
      { "castpipesmall-straight2", 2, -0.825 },
      { "pipe-cast-straight3", 3, -1.325 },
      { "pipe-castsmall-straight3", 3, -1.325 },
      { "castpipe-straight3", 3, -1.325 },
      { "castpipesmall-straight3", 3, -1.325 },
    };

  // Fails when the long test is dropped: the pipes then take their kind's old hold, a block's 1.2 blocks or a bar's rod hold.
  [Theory]
  [MemberData(nameof(Straights))]
  public void A_long_pipe_takes_the_approved_hold(
    string stem,
    int cells,
    double z
  ) {
    ItemViewSet set = ItemViews.Compute(World.Api, Pipe(stem))!;
    ModelTransform tp = set.TpHand;
    Assert.Equal(
      stem.StartsWith("pipe") ? ItemViewKind.Multicell : ItemViewKind.Bar,
      set.Kind
    );
    Assert.Equal(12.69, tp.Rotation.X, 2);
    Assert.Equal(-165.82, tp.Rotation.Y, 2);
    Assert.Equal(76.51, tp.Rotation.Z, 2);
    Assert.Equal(1, tp.ScaleXYZ.X, 4);
    Near(-0.4669, tp.Translation.X);
    Near(-0.5363, tp.Translation.Y);
    Near(z, tp.Translation.Z);
    Assert.Equal(0.5, tp.Origin.X, 4);
    Assert.Equal(0.5, tp.Origin.Y, 4);
    Assert.Equal(cells / 2.0, tp.Origin.Z, 4);
    Assert.Equal("holdbothhandslarge", set.IdleClip);
    Assert.Equal("heldblockready", set.ReadyClip);
    Assert.Null(set.HitClip);
    Assert.Equal("twohandplaceblock", set.UseClip);
  }

  // Fails when the held length is a fixed 1.2 blocks again, or the hand scale is not 1: the held length is the drawn
  // length over 16 voxels, 2 and 3 blocks.
  [Theory]
  [MemberData(nameof(Straights))]
  public void A_long_pipe_is_held_at_its_drawn_length(
    string stem,
    int cells,
    double z
  ) {
    _ = z;
    ItemViewSet set = ItemViews.Compute(World.Api, Pipe(stem))!;
    double[] ext = CollectibleViews.BoxOf(World.Api, Pipe(stem))!.Extents();
    Assert.Equal(cells * 16, ext[0], 3);
    Assert.Equal(cells, set.TpHand.ScaleXYZ.X * ext[0] / 16, 4);
  }

  // Fails when the hold's rotation is not the one that lays the long axis along the seraph's left-right: the axis
  // then leaves the player's z.
  [Theory]
  [MemberData(nameof(Straights))]
  public void A_long_pipe_lies_along_the_seraphs_left_right(
    string stem,
    int cells,
    double z
  ) {
    _ = (cells, z);
    ItemViewSet set = ItemViews.Compute(World.Api, Pipe(stem))!;
    Vec3d along = HeldDirection(set.TpHand, LongAxis(set));
    Assert.InRange(Math.Abs(along.Z), Math.Cos(0.05 * Math.PI / 180), 1);
  }

  // Fails when the lay is not used: a pipe drawn on the x or y axis keeps the z-drawn pipe's rotation and lies across
  // the seraph's chest or upright.
  [Theory]
  [InlineData(0, 90, 0)]
  [InlineData(90, 0, 0)]
  [InlineData(0, 0, 90)]
  public void A_long_pipe_lies_along_the_seraphs_left_right_whatever_axis_it_is_drawn_on(
    double rx,
    double ry,
    double rz
  ) {
    var shape = new CompositeShape {
      Base = new AssetLocation(Pipes + "block/castpipe-straight3"),
      rotateX = (float)rx,
      rotateY = (float)ry,
      rotateZ = (float)rz,
    };
    ItemViewSet set = ItemViews.Compute(World.Api, shape, ItemViewKind.Bar);
    Vec3d along = HeldDirection(set.TpHand, LongAxis(set));
    Assert.InRange(Math.Abs(along.Z), Math.Cos(0.05 * Math.PI / 180), 1);
    Assert.Equal(1, set.TpHand.ScaleXYZ.X, 4);
  }

  // Fails when the long test drops its three-times-the-middle rule: a door, twice as long as it is wide, is then held level.
  [Fact]
  public void A_door_keeps_its_hold() {
    Block door = World
      .World.Blocks.Where(b =>
        b?.Code != null
        && b.Code.Domain == "game"
        && b.Code.Path.StartsWith("door-")
      )
      .Select(b => (Block: b, Box: CollectibleViews.BoxOf(World.Api, b)))
      .Where(x =>
        x.Box != null
        && CollectibleViews.KindOf(World.Api, x.Block) == ItemViewKind.Multicell
        && x.Box.Extents()[0] > 16
      )
      .Select(x => x.Block)
      .First();
    double[] ext = CollectibleViews.BoxOf(World.Api, door)!.Extents();
    Assert.True(ext[0] < 3 * ext[1], $"{door.Code} {ext[0]} x {ext[1]}");
    ItemViewSet set = ItemViews.Compute(World.Api, door)!;
    Assert.Equal(1.2, set.TpHand.ScaleXYZ.X * ext[0] / 16, 2);
    Assert.Equal(
      ModelTransform.BlockDefaultTp().Rotation.Y,
      set.TpHand.Rotation.Y,
      3
    );
  }
}
