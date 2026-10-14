#if GAME_GE_1_22
using System;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestPlayer.UseControl"/> and <see cref="TestPlayer.LookAt"/>: a click and a look on a
/// control's selection box carry the box's id, and an unknown id names the ids the cell has.</summary>
public class ControlAimTests
{
  private sealed class Panel : Block
  {
    public string? Seen;
    public Vec3d? Hit;

    public override Cuboidf[] GetSelectionBoxes(
      IBlockAccessor blockAccessor,
      BlockPos pos
    ) =>
      [
        new CuboidfWithId(new Cuboidf(0.25f, 0.5f, 0.25f, 0.75f, 1f, 0.5f))
        {
          Id = "handle",
        },
        new Cuboidf(0f, 0f, 0f, 1f, 0.5f, 1f),
      ];

    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    )
    {
      Seen = blockSel.SelectionBoxId;
      Hit = blockSel.HitPosition;
      return true;
    }
  }

  private static BlockPos At => new(0, 1, 0);

  private static (TestWorld, Panel, TestPlayer) Stand()
  {
    var world = new TestWorld();
    var panel = TestBlocks.Configure(new Panel(), "test:panel", 1);
    world.Place(At, panel);
    return (world, panel, world.Player());
  }

  // Fails when a control click reaches the block without the id of the box it hit.
  [Fact]
  public void UseControl_sends_the_id()
  {
    var (_, panel, player) = Stand();

    Assert.True(player.UseControl(At, "handle"));
    Assert.Equal("handle", panel.Seen);
  }

  // Fails when an unknown id falls back to the first box instead of naming the ids the cell has.
  [Fact]
  public void An_unknown_id_names_the_cells_ids()
  {
    var (world, panel, player) = Stand();
    var plain = TestBlocks.Configure(new Block(), "test:plain", 1);
    world.Place(new BlockPos(2, 1, 0), plain);

    ArgumentException known = Assert.Throws<ArgumentException>(() =>
      player.UseControl(At, "lever")
    );
    Assert.Contains("its controls are handle.", known.Message);
    ArgumentException none = Assert.Throws<ArgumentException>(() =>
      player.UseControl(new BlockPos(2, 1, 0), "lever")
    );
    Assert.Contains("its controls are none", none.Message);
    Assert.Null(panel.Seen);
  }

  // Fails when the hit lands at the cell face's centre instead of the centre of the box's face.
  [Fact]
  public void The_hit_lies_inside_the_box()
  {
    var (_, panel, player) = Stand();

    Assert.True(player.UseControl(At, "handle", face: BlockFacing.UP));
    Assert.Equal(0.5, panel.Hit!.X);
    Assert.Equal(1.0, panel.Hit.Y);
    Assert.Equal(0.375, panel.Hit.Z);
  }

  // Fails when LookAt sets the selection's box index without its id.
  [Fact]
  public void LookAt_sets_the_selection_id()
  {
    var (_, _, player) = Stand();

    player.LookAt(At, "handle");
    Assert.Equal("handle", player.Player.CurrentBlockSelection.SelectionBoxId);
  }
}
#endif
