using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="BlockExStairs"/>, vanilla's <c>BlockStairs</c> over a dashed code: placement, the
/// canonical drop and pick, rotation and both flips, with and without <c>noDownVariant</c>.
/// </summary>
public class ExStairsTests {
  private const string Stem = "exlib:slag-stairs";

  #region Placement

  // Fails when a placement builds the code from the first dash-segment, or reads the vertical
  // orientation off another input than vanilla's.
  [Theory]
  [InlineData("north", "up", 0.5, false, "up-north-free")]
  [InlineData("east", "down", 0.5, false, "down-east-free")]
  [InlineData("south", "north", 0.2, false, "up-south-free")]
  [InlineData("west", "north", 0.8, false, "down-west-free")]
  [InlineData("west", "north", 0.8, true, "up-west-free")]
  public void A_placement_lands_the_look_and_the_half_on_the_whole_code(
    string look,
    string face,
    double hitY,
    bool noDown,
    string expected
  ) {
    var rig = new StairsRig(noDown);

    Assert.True(rig.Place(look, face, hitY));

    Assert.Equal($"{Stem}-{expected}", rig.PlacedCode);
  }

  // Fails when a missing state is dereferenced rather than refused.
  [Fact]
  public void A_state_the_blocktype_lacks_is_refused() {
    var rig = new StairsRig(noDown: false, skip: "down-north-free");

    Assert.False(rig.Place("north", "north", 0.8));

    Assert.Null(rig.PlacedCode);
  }

  // Fails when the cell's CanPlaceBlock is skipped.
  [Fact]
  public void An_occupied_cell_is_refused() {
    var rig = new StairsRig(noDown: false);
    rig.World.Place(
      StairsRig.Pos,
      TestBlocks.Configure(new Block(), "game:rock-granite", 90)
    );

    Assert.False(rig.Place("north", "up", 0.5));

    Assert.Equal(
      "game:rock-granite",
      rig.World.GetBlock(StairsRig.Pos).Code.ToString()
    );
  }

  #endregion

  #region Drop and pick

  // Fails when the drop or the pick is built from the first dash-segment, or keeps the broken
  // variant's facing or cover.
  [Fact]
  public void A_drop_and_a_pick_are_the_up_north_free_variant() {
    var rig = new StairsRig(noDown: false);
    BlockExStairs broken = rig.Of("down-west-snow");
    rig.World.Place(StairsRig.Pos, broken);

    ItemStack[] drops = broken.GetDrops(rig.World.World, StairsRig.Pos, null!);
    ItemStack pick = broken.OnPickBlock(rig.World.World, StairsRig.Pos);

    Assert.Equal(
      [$"{Stem}-up-north-free"],
      drops.Select(d => d.Collectible.Code.ToString())
    );
    Assert.Equal($"{Stem}-up-north-free", pick.Collectible.Code.ToString());
  }

  #endregion

  #region Rotation and flips

  // Fails when a rotation builds the code from the first dash-segment or turns the wrong way.
  [Theory]
  [InlineData("up-north-free", 90, "up-east-free")]
  [InlineData("down-east-snow", 180, "down-west-snow")]
  [InlineData("up-south-free", 270, "up-east-free")]
  public void A_rotation_turns_the_horizontal_orientation(
    string from,
    int angle,
    string to
  ) {
    var rig = new StairsRig(noDown: false);

    Assert.Equal(
      $"{Stem}-{to}",
      rig.Of(from).GetRotatedBlockCode(angle).ToString()
    );
  }

  // Fails when a vertical flip builds from the first dash-segment or ignores noDownVariant.
  [Theory]
  [InlineData(false, "up-north-free", "down-north-free")]
  [InlineData(false, "down-north-free", "up-north-free")]
  [InlineData(true, "up-north-free", "up-north-free")]
  public void A_vertical_flip_turns_the_stairs_over_unless_they_have_no_down(
    bool noDown,
    string from,
    string to
  ) {
    var rig = new StairsRig(noDown);

    Assert.Equal(
      $"{Stem}-{to}",
      rig.Of(from).GetVerticallyFlippedBlockCode().ToString()
    );
  }

  // Fails when a horizontal flip builds from the first dash-segment or reverses a facing on another
  // axis.
  [Theory]
  [InlineData("up-north-free", "Z", "up-south-free")]
  [InlineData("up-east-snow", "X", "up-west-snow")]
  [InlineData("up-east-free", "Z", "up-east-free")]
  public void A_horizontal_flip_reverses_a_facing_on_its_axis(
    string from,
    string axis,
    string to
  ) {
    var rig = new StairsRig(noDown: false);

    Assert.Equal(
      $"{Stem}-{to}",
      rig.Of(from)
        .GetHorizontallyFlippedBlockCode(System.Enum.Parse<EnumAxis>(axis))
        .ToString()
    );
  }

  #endregion

  #region Rig

  /// <summary>Every state of a stairs blocktype <c>exlib:slag-stairs</c> in vanilla's
  /// <c>verticalorientation</c>, <c>horizontalorientation</c> and <c>cover</c> groups, loaded with
  /// or without <c>noDownVariant</c>.</summary>
  private sealed class StairsRig {
    internal static readonly BlockPos Pos = new(64, 16, 64, 0);

    private readonly Dictionary<string, BlockExStairs> _byState = [];

    internal StairsRig(bool noDown, string? skip = null) {
      var attributes = new JsonObject(
        Newtonsoft.Json.Linq.JToken.Parse(
          $$"""{"noDownVariant":{{(noDown ? "true" : "false")}}}"""
        )
      );
      int id = 1;
      foreach (string v in new[] { "up", "down" })
        foreach (string h in new[] { "north", "east", "south", "west" })
          foreach (string cover in new[] { "free", "snow" }) {
            string state = $"{v}-{h}-{cover}";
            if (state == skip)
              continue;
            BlockExStairs block = TestBlocks.Configure(
              new BlockExStairs { Attributes = attributes },
              $"{Stem}-{state}",
              id++,
              ("verticalorientation", v),
              ("horizontalorientation", h),
              ("cover", cover)
            );
            World.Register(block);
            block.OnLoaded(World.Api);
            _byState[state] = block;
          }
    }

    internal TestWorld World { get; } = new();

    internal string? PlacedCode {
      get {
        Block at = World.GetBlock(Pos);
        return at.BlockId == 0 ? null : at.Code.ToString();
      }
    }

    internal BlockExStairs Of(string state) => _byState[state];

    /// <summary>Places the <c>up-north-free</c> state with the player five blocks back from the
    /// cell, looking <paramref name="look"/>, against <paramref name="face"/> at height
    /// <paramref name="hitY"/>.</summary>
    internal bool Place(string look, string face, double hitY) {
      BlockFacing towards = BlockFacing.FromCode(look);
      TestPlayer player = World.Player();
      player.Entity.Pos.SetPos(
        Pos.X + 0.5 - towards.Normali.X * 5,
        Pos.Y + 0.5,
        Pos.Z + 0.5 - towards.Normali.Z * 5
      );
      player.Entity.LocalEyePos.Returns(new Vec3d());
      string failure = "";
      return Of("up-north-free")
        .TryPlaceBlock(
          World.World,
          player.Player,
          new ItemStack(Of("up-north-free")),
          new BlockSelection {
            Position = Pos.Copy(),
            Face = BlockFacing.FromCode(face),
            HitPosition = new Vec3d(0.5, hitY, 0.5),
          },
          ref failure
        );
    }
  }

  #endregion
}
