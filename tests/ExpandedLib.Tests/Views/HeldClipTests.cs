using System;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The clips a mod's patch adds to the player, the pose at a frame, the left anchor, the declared grips and
/// G17 hand.left, on the clips fixture mod and the whole vanilla game.</summary>
[Collection("ItemViews")]
public class HeldClipTests {
  private static string Fixtures(string name) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Helpers",
      "Views",
      "Fixtures",
      name
    );

  private static string Fixture => Fixtures("clipsfixture");

  private static TestWorld World => GameWorlds.Get([Fixture]);

  private static ViewGroup Ladle() =>
    ViewGates
      .Groups(World, "clipsfixture")
      .Single(g => g.Codes.Contains("clipsfixture:ladle"));

  private static GateReading? LeftGrip(ViewGroup group) =>
    ViewGates.Rules(World, group).SingleOrDefault(r => r.Gate == "G17");

  private static ViewGroup Moved(ViewGroup group, float voxels) {
    ModelTransform hand = group.Hand.Clone();
    hand.Translation = new FastVec3f(
      hand.Translation.X + voxels / 16,
      hand.Translation.Y,
      hand.Translation.Z
    );
    return group with { Hand = hand };
  }

  // Fails when the mod's patch is not applied to the shape or to the player's animation list: the clip is then missing
  // from the shape, or HeldClip finds no animation of that code.
  [Fact]
  public void A_mods_patch_adds_its_clips_and_their_twins_to_the_player() {
    Assert.Contains("iiex-ladlehold", SeraphPose.Clips(World.Api));
    Assert.Contains("iiex-ladlepour-fp", SeraphPose.Clips(World.Api));
    Assert.Equal("iiex-ladlehold", ViewGates.HeldClip(World, "iiex-ladlehold"));
    Assert.Equal(
      "iiex-ladlehold-fp",
      ViewGates.HeldClip(World, "iiex-ladlehold", true)
    );
    Assert.Equal("iiex-ladlepour", ViewGates.HeldClip(World, "iiex-ladlepour"));
  }

  // Fails when the left anchor is read from the right hand's attachment point, or the clip is posed at rest.
  [Theory]
  [InlineData("iiex-ladlehold", 0, 5.00, 19.00, 10.41)]
  [InlineData("iiex-ladlehold-fp", 0, 5.00, 19.00, 10.41)]
  [InlineData("iiex-ladlepour", 0, 5.00, 19.00, 10.41)]
  [InlineData("iiex-ladlepour", 10, 5.00, 19.45, 10.37)]
  [InlineData("iiex-ladlepour", 19, 4.98, 19.85, 10.27)]
  public void The_left_hand_at_a_frame_is_where_the_clip_was_solved(
    string clip,
    int frame,
    double x,
    double y,
    double z
  ) {
    Vec3d p = SeraphPose.At(World.Api, clip, frame).LeftAnchor.Position;
    Assert.Equal(x, p.X * 16, 0.02);
    Assert.Equal(y, p.Y * 16, 0.02);
    Assert.Equal(z, p.Z * 16, 0.02);
  }

  // Fails when ItemAnchor is read from the LeftHand attachment point: x is then 5.0, not -3.51.
  [Fact]
  public void The_right_hand_keeps_the_holds_place_through_the_pour() {
    Vec3d hold = SeraphPose.At(World.Api, "iiex-ladlehold").ItemAnchor.Position;
    Vec3d pour = SeraphPose
      .At(World.Api, "iiex-ladlepour", 19)
      .ItemAnchor.Position;
    Assert.Equal(-3.51, hold.X * 16, 0.1);
    Assert.Equal(19.00, hold.Y * 16, 0.1);
    Assert.Equal(10.12, hold.Z * 16, 0.1);
    Assert.Equal(hold.X, pour.X, 3);
    Assert.Equal(hold.Y, pour.Y, 3);
    Assert.Equal(hold.Z, pour.Z, 3);
  }

  // Fails when the frame is not part of the cache key: frame 10 then answers with frame 0's pose.
  [Fact]
  public void A_pose_is_built_once_per_clip_and_frame() {
    Assert.Same(
      SeraphPose.At(World.Api, "iiex-ladlepour", 10),
      SeraphPose.At(World.Api, "iiex-ladlepour", 10)
    );
    Assert.NotSame(
      SeraphPose.At(World.Api, "iiex-ladlepour", 10),
      SeraphPose.At(World.Api, "iiex-ladlepour", 0)
    );
  }

  // Fails when the frame is not checked against the clip: 20 and -1 then pose the clip's end and start.
  [Theory]
  [InlineData(20)]
  [InlineData(-1)]
  public void A_frame_outside_the_clip_is_refused(int frame) {
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      SeraphPose.At(World.Api, "iiex-ladlepour", frame)
    );
  }

  // Fails when the declaration is not read from itemViews.grips, or the right and left points are swapped.
  [Fact]
  public void Declared_grips_are_the_items_two_points_in_voxels() {
    ItemGrips grips = CollectibleViews.GripsOf(
      World.World.GetItem(new AssetLocation("clipsfixture:ladle"))
    )!;
    Assert.Equal(
      [8, 6.75, 21.1],
      [grips.Right.X, grips.Right.Y, grips.Right.Z]
    );
    Assert.Equal(
      [5.5, 6.75, 29.25],
      [grips.Left.X, grips.Left.Y, grips.Left.Z]
    );
    Assert.Null(
      CollectibleViews.GripsOf(
        World.World.GetItem(new AssetLocation("clipsfixture:bareladle"))
      )
    );
  }

  // Fails when G17 is read from the right anchor, or from the left anchor of the rest pose: the miss is then voxels.
  [Fact]
  public void The_ladles_left_grip_lies_on_its_left_hand() {
    GateReading g = LeftGrip(Ladle())!;
    Assert.Equal("hand.left", g.Name);
    Assert.Equal("tp", g.Target);
    Assert.True(g.Pass, g.Detail);
    Assert.True(g.Value < 0.1, g.Detail);
  }

  // Fails when the limit moves off 0.5 voxel: the grip 0.49 off passes and 0.51 off fails.
  [Fact]
  public void G17_limit_is_half_a_voxel() {
    GateReading near = LeftGrip(Moved(Ladle(), 0.49f))!;
    Assert.True(near.Pass);
    Assert.Equal(ViewGates.LeftGripVoxels, near.Max);
    GateReading far = LeftGrip(Moved(Ladle(), 0.51f))!;
    Assert.False(far.Pass);
    Assert.Equal(0.51, far.Value, 0.02);
  }

  // Fails when G17 is read on an item that declares no grips.
  [Fact]
  public void An_item_with_no_grips_has_no_G17() {
    ViewGroup bare = ViewGates
      .Groups(World, "clipsfixture")
      .Single(g => g.Codes.Contains("clipsfixture:bareladle"));
    Assert.Null(LeftGrip(bare));
  }

  // Fails when grips that do not parse stop the pass or go unlogged, or a missing clip is logged on the server, goes
  // unlogged on the client, is logged per item, or is logged though the shape or the game names it.
  [Fact]
  public void A_grip_declaration_that_does_not_parse_and_a_missing_clip_are_logged() {
    using var world = new TestWorld();
    world.LoadAsGame([Fixture, Fixtures("clipsfaults")]);
    world.Log.Expect(EnumLogType.Error, "clipsfaults:badgrips");
    world.Log.Expect(EnumLogType.Warning, "clipsfaults-nothere");

    world.Api.Side.Returns(EnumAppSide.Client);
    ItemViewsPass.Run(world.Api);

    Assert.Contains(
      world.Log.Errors,
      e => e.Contains("clipsfaults:badgrips") && e.Contains("grips")
    );
    Assert.Single(
      world.Log.Warnings,
      w =>
        w.Contains("'clipsfaults-nothere'")
        && w.Contains("clipsfaults:noclip,")
        && w.Contains("clipsfaults:noclip2")
    );
    Assert.DoesNotContain(
      world.Log.Warnings,
      w => w.Contains("iiex-ladlehold") || w.Contains("heldblockready")
    );
  }

  private static void PlayerEntity(
    TestWorld world,
    string? shapeBase,
    params (string Code, string Animation)[] animations
  ) =>
    world
      .Api.World.GetEntityType(Arg.Any<AssetLocation>())
      .Returns(
        new EntityProperties {
          Client = new EntityClientProperties([], []) {
            Shape =
              shapeBase == null
                ? null
                : new CompositeShape { Base = new AssetLocation(shapeBase) },
            Animations = animations
              .Select(a => new AnimationMetaData {
                Code = a.Code,
                Animation = a.Animation,
              })
              .ToArray(),
          },
        }
      );

  // Fails when the warning reads a fixed shape instead of the player entity type's own: a player pointed at a shape
  // without the clip is then not warned.
  [Fact]
  public void A_player_shape_that_lacks_a_named_clip_is_warned() {
    using var world = new TestWorld();
    world.LoadAsGame([Fixture, Fixtures("clipsfaults")]);
    world.Log.Expect(EnumLogType.Error, "clipsfaults:badgrips");
    world.Log.Expect(EnumLogType.Warning, "The player's shape has no clip");
    PlayerEntity(
      world,
      "clipsfixture:item/ladle",
      ("iiex-ladlehold", "iiex-ladlehold")
    );
    world.Api.Side.Returns(EnumAppSide.Client);

    ItemViewsPass.Run(world.Api);

    Assert.Contains(
      world.Log.Warnings,
      w => w.Contains("'iiex-ladlehold'") && w.Contains("clipsfixture:ladle")
    );
  }

  // Fails when a name is checked against the shape's clips without the player's animation list: interactstatic-beams
  // is then warned though it maps to interactstatic; or when a name the list does not map is not warned.
  [Fact]
  public void A_name_is_checked_through_the_players_animation_list() {
    using var world = new TestWorld();
    world.LoadAsGame([Fixture, Fixtures("clipsbeams")]);
    world.Log.Expect(EnumLogType.Warning, "The player's shape has no clip");
    PlayerEntity(
      world,
      "game:entity/humanoid/seraph-faceless",
      ("interactstatic-beams", "interactstatic")
    );
    world.Api.Side.Returns(EnumAppSide.Client);

    ItemViewsPass.Run(world.Api);

    Assert.DoesNotContain(
      world.Log.Warnings,
      w => w.Contains("interactstatic-beams")
    );
    Assert.Contains(world.Log.Warnings, w => w.Contains("'iiex-ladlehold'"));
  }
}
