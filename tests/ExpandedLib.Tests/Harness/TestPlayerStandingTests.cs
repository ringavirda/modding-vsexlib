using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A <see cref="TestPlayer"/> made by <see cref="TestPlayer.Create"/> stands as a player does:
/// the game's placement reads its position and eye position.</summary>
public class TestPlayerStandingTests {
  // Fails when TestPlayer.Create leaves the entity's position or eye position unset.
  [Fact]
  public void A_new_test_player_is_oriented_by_its_position() {
    using var world = new TestWorld();
    TestPlayer player = world.Player();
    player.Entity.Pos.SetPos(10.5, 1, 4.5);
    var selection = new BlockSelection {
      Position = new BlockPos(4, 1, 4),
      Face = BlockFacing.UP,
      HitPosition = new Vec3d(0.5, 0, 0.5),
    };

    BlockFacing[] facing = Block.SuggestedHVOrientation(
      player.Player,
      selection
    );

    Assert.Equal(BlockFacing.WEST, facing[0]);

    player.Entity.Pos.SetPos(-1.5, 1, 4.5);
    Assert.Equal(
      BlockFacing.EAST,
      Block.SuggestedHVOrientation(player.Player, selection)[0]
    );
  }

  // Fails when TestPlayer.Create leaves the entity's eye offset unset or non-zero.
  [Fact]
  public void A_new_test_player_stands_at_the_origin_with_a_zero_eye_offset() {
    using var world = new TestWorld();
    TestPlayer player = world.Player();

    Assert.Equal(new Vec3d(), player.Entity.LocalEyePos);
    Assert.Equal(
      (0d, 0d, 0d),
      (player.Entity.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z)
    );
  }
}
