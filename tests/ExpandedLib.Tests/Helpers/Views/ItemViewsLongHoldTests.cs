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
    Assert.InRange(actual, expected - 5e-4, expected + 5e-4);

  private static Vec3d LongAxis(ItemViewSet set) {
    double[] ext =
    [
      set.BoxTo!.X - set.BoxFrom!.X,
      set.BoxTo.Y - set.BoxFrom.Y,
      set.BoxTo.Z - set.BoxFrom.Z,
    ];
    return set.BoxAxes![Array.IndexOf(ext, ext.Max())];
  }

  public static TheoryData<string, int, string, double, double> Straights =>
    new()
    {
      { "pipe-cast-straight2", 2, "exlib-longcarry2", 8, 3.0 },
      { "pipe-castsmall-straight2", 2, "exlib-longcarry2", 8, 1.0 },
      { "castpipe-straight2", 2, "exlib-longcarry2", 8, 3.0 },
      { "castpipesmall-straight2", 2, "exlib-longcarry2", 8, 1.0 },
      { "pipe-cast-straight3", 3, "exlib-longcarry3", 10, 3.0 },
      { "pipe-castsmall-straight3", 3, "exlib-longcarry3", 10, 1.0 },
      { "castpipe-straight3", 3, "exlib-longcarry3", 10, 3.0 },
      { "castpipesmall-straight3", 3, "exlib-longcarry3", 10, 1.0 },
    };

  // The hands' midpoint (h times the player's left) and the player's up in the hold's anchor frame, to 4 places.
  private static readonly double[] Left = [0.2449, -0.2129, 0.9459];
  private static readonly double[] Up = [-0.9428, 0.1752, 0.2835];

  // Fails when the long test is dropped, a band picks another's carry, the offset from the hands is halved, or ready
  // and hit stay the vanilla clips.
  [Theory]
  [MemberData(nameof(Straights))]
  public void A_long_pipe_takes_its_bands_carry_resting_on_the_hands(
    string stem,
    int cells,
    string carry,
    double half,
    double rest
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
    Assert.Equal(0.5, tp.Origin.X, 4);
    Assert.Equal(0.5, tp.Origin.Y, 4);
    Assert.Equal(cells / 2.0, tp.Origin.Z, 4);
    Near((half * Left[0] + rest * Up[0]) / 16 - tp.Origin.X, tp.Translation.X);
    Near((half * Left[1] + rest * Up[1]) / 16 - tp.Origin.Y, tp.Translation.Y);
    Near((half * Left[2] + rest * Up[2]) / 16 - tp.Origin.Z, tp.Translation.Z);
    Assert.Equal(carry, set.IdleClip);
    Assert.Equal(carry, set.ReadyClip);
    Assert.Equal(carry, set.HitClip);
    Assert.Equal("twohandplaceblock", set.UseClip);
  }

  // Fails when the held length is a fixed 1.2 blocks again, or the hand scale is not 1: the held length is the drawn
  // length over 16 voxels, 2 and 3 blocks.
  [Theory]
  [MemberData(nameof(Straights))]
  public void A_long_pipe_is_held_at_its_drawn_length(
    string stem,
    int cells,
    string carry,
    double half,
    double rest
  ) {
    _ = (carry, half, rest);
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
    string carry,
    double half,
    double rest
  ) {
    _ = (cells, carry, half, rest);
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

  private static Vec3d[] Cuboid(
    double x0,
    double y0,
    double z0,
    double x1,
    double y1,
    double z1
  ) =>
    [
      .. Enumerable
        .Range(0, 8)
        .Select(i => new Vec3d(
          (i & 1) == 0 ? x0 : x1,
          (i & 2) == 0 ? y0 : y1,
          (i & 4) == 0 ? z0 : z1
        )),
    ];

  private static CollectibleViews.ViewBox Boxed(params Vec3d[][] elements) {
    Vec3d[] all = [.. elements.SelectMany(e => e)];
    return new CollectibleViews.ViewBox(
      new Vec3d(all.Min(p => p.X), all.Min(p => p.Y), all.Min(p => p.Z)),
      new Vec3d(all.Max(p => p.X), all.Max(p => p.Y), all.Max(p => p.Z)),
      [new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, 1)],
      elements,
      [.. elements.Select(_ => new bool[6])]
    );
  }

  private static ModelTransform HandOf(
    CollectibleViews.ViewBox box,
    out string carry
  ) => ItemViews.LongHand(Lay.Of(box, false, null), box, out _, out carry);

  // Fails when a band edge falls in the wrong band: a length up to 24 voxels is the 1-cell carry, over 24 up to 40 the
  // 2-cell, over 40 the 3-cell (the upper edge read without its tolerance).
  [Theory]
  [InlineData(16.5, "exlib-longcarry1")]
  [InlineData(24, "exlib-longcarry1")]
  [InlineData(24.5, "exlib-longcarry2")]
  [InlineData(40, "exlib-longcarry2")]
  [InlineData(40.5, "exlib-longcarry3")]
  [InlineData(64, "exlib-longcarry3")]
  public void A_length_picks_its_carry_at_the_band_edges(
    double length,
    string carry
  ) {
    HandOf(Boxed(Cuboid(6, 6, 0, 10, 10, length)), out string picked);
    Assert.Equal(carry, picked);
  }

  // Fails when the rest height is taken from the whole box instead of the drawn underside at the two hand stations: a
  // flange at each end of the shaft then lifts the piece off the hands.
  [Fact]
  public void A_flange_at_an_end_does_not_lift_the_piece_off_the_hands() {
    ModelTransform flanged = HandOf(
      Boxed(
        Cuboid(6, 6, 0, 10, 10, 32),
        Cuboid(4, 4, 0, 12, 12, 1),
        Cuboid(4, 4, 31, 12, 12, 32)
      ),
      out _
    );
    ModelTransform plain = HandOf(Boxed(Cuboid(6, 6, 0, 10, 10, 32)), out _);
    Near(plain.Translation.X, flanged.Translation.X);
    Near(plain.Translation.Y, flanged.Translation.Y);
    Near(plain.Translation.Z, flanged.Translation.Z);
  }

  // Fails when the lower of the two undersides is not the one the piece rests on: the higher one then sinks the thick half
  // into the hand.
  [Fact]
  public void A_piece_rests_on_the_lower_of_its_two_undersides() {
    ModelTransform stepped = HandOf(
      Boxed(Cuboid(4, 4, 0, 12, 12, 16), Cuboid(6, 6, 16, 10, 10, 32)),
      out _
    );
    ModelTransform thick = HandOf(Boxed(Cuboid(4, 4, 0, 12, 12, 32)), out _);
    Near(thick.Translation.X, stepped.Translation.X);
    Near(thick.Translation.Y, stepped.Translation.Y);
    Near(thick.Translation.Z, stepped.Translation.Z);
  }

  // Fails when the load pass leaves the carry off the item: its idle, ready or hit clip stays the game's.
  [Fact]
  public void The_load_pass_sets_the_carry_on_a_long_item() {
    Item item = World.World.GetItem(
      new AssetLocation(Pipes + "castpipe-straight2")
    );
    Assert.Equal("exlib-longcarry2", item.HeldRightTpIdleAnimation);
    Assert.Equal("exlib-longcarry2", item.HeldRightReadyAnimation);
    Assert.Equal("exlib-longcarry2", item.HeldTpHitAnimation);
  }

  // Fails when the load pass overwrites what the item names: its idle, ready and hit clips and its hand transform.
  [Fact]
  public void An_item_that_names_its_own_clips_and_hand_transform_keeps_them() {
    Item item = World.World.GetItem(
      new AssetLocation(Pipes + "ownclips-straight2")
    );
    Assert.Equal("holdbothhandslarge", item.HeldRightTpIdleAnimation);
    Assert.Equal("heldblockready", item.HeldRightReadyAnimation);
    Assert.Equal("spearhit", item.HeldTpHitAnimation);
    Assert.Equal(-0.3f, item.TpHandTransform.Translation.X);
    Assert.Equal(20f, item.TpHandTransform.Rotation.Y);
  }

  // Fails when the load pass fills the carry for an item that sets only its hand transform.
  [Fact]
  public void An_item_with_only_a_hand_transform_keeps_its_kinds_clips() {
    Item item = World.World.GetItem(
      new AssetLocation(Pipes + "ownhand-straight2")
    );
    Assert.DoesNotContain(
      ViewReferences.LongCarries,
      c =>
        c.Clip == item.HeldRightTpIdleAnimation
        || c.Clip == item.HeldRightReadyAnimation
        || c.Clip == item.HeldTpHitAnimation
    );
    Assert.Equal(-0.3f, item.TpHandTransform.Translation.X);
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
