using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The aiming preview: tints once per selection, in two colours, and clears when the player
/// looks away, switches item or leaves the world.</summary>
public class ExCoursePreviewTests {
  private const int Change = 0x11223344;
  private const int Refuse = 0x55667788;

  private sealed class Rig {
    public readonly TestWorld World = new();
    public readonly TestPlayer Player;
    public readonly ExCoursePreview Preview;
    public readonly Item Held;
    public readonly Item Other;
    public int Computes;
    public Action<float>? Tick;

    public Rig(string key) {
      World
        .ClientApi.Event.RegisterGameTickListener(
          Arg.Any<Action<float>>(),
          Arg.Any<int>()
        )
        .Returns(call => {
          Tick = call.Arg<Action<float>>();
          return 1L;
        });
      Player = World.Player();
      Held = World.RegisterItem("hoop-test");
      Other = World.RegisterItem("stick-test");
      Player.Hold(new ItemStack(Held));
      Preview = new ExCoursePreview(World.ClientApi, key, Change, Refuse);
    }

    public BlockSelection Aim(int x) =>
      new() { Position = new BlockPos(x, 5, 5) };

    public void Drive(BlockSelection? sel) => Drive(Held, sel);

    public void Drive(CollectibleObject held, BlockSelection? sel) =>
      Preview.Drive(
        Player.Player,
        held,
        sel,
        s => {
          Computes++;
          return ([s.Position], [s.Position.AddCopy(1, 0, 0)]);
        }
      );

    public List<(List<BlockPos> Positions, List<int> Colors)> Pushes() =>
      World
        .ClientApi.World.ReceivedCalls()
        .Where(c =>
          c.GetMethodInfo().Name == nameof(IWorldAccessor.HighlightBlocks)
        )
        .Select(c =>
          (
            (List<BlockPos>)c.GetArguments()[2]!,
            (List<int>)c.GetArguments()[3]!
          )
        )
        .ToList();
  }

  // Fails when Drive recomputes on every call.
  [Fact]
  public void A_selection_is_computed_once_and_again_when_it_changes() {
    var rig = new Rig("test:preview-" + Guid.NewGuid());

    rig.Drive(rig.Aim(1));
    rig.Drive(rig.Aim(1));
    rig.Drive(rig.Aim(1));
    Assert.Equal(1, rig.Computes);
    Assert.Single(rig.Pushes());

    rig.Drive(rig.Aim(2));
    Assert.Equal(2, rig.Computes);
    Assert.Equal(2, rig.Pushes().Count);
  }

  // Fails when the change cells or the refusing cells take the other colour.
  [Fact]
  public void The_change_cells_and_the_refusing_cells_take_their_own_colours() {
    var rig = new Rig("test:preview-" + Guid.NewGuid());

    rig.Drive(rig.Aim(1));

    var (positions, colors) = Assert.Single(rig.Pushes());
    Assert.Equal([new BlockPos(1, 5, 5), new BlockPos(2, 5, 5)], positions);
    Assert.Equal([Change, Refuse], colors);
  }

  // Fails when the game tick listener is not registered, so a switch of item never clears the tint.
  [Fact]
  public void Switching_item_clears_the_tint() {
    var rig = new Rig("test:preview-" + Guid.NewGuid());
    rig.Drive(rig.Aim(1));

    rig.Tick!(0.1f);
    Assert.Single(rig.Pushes());

    rig.Player.Hold(new ItemStack(rig.Other));
    rig.Tick!(0.1f);

    var (positions, _) = rig.Pushes().Last();
    Assert.Empty(positions);
    Assert.Equal(2, rig.Pushes().Count);
  }

  // Fails when the held item is not compared, so a second item aimed at the same block reuses the first item's tint.
  [Fact]
  public void A_second_item_on_the_same_aim_recomputes() {
    var rig = new Rig("test:preview-" + Guid.NewGuid());

    rig.Drive(rig.Held, rig.Aim(1));
    rig.Drive(rig.Other, rig.Aim(1));

    Assert.Equal(2, rig.Computes);
    Assert.Equal(2, rig.Pushes().Count);
  }

  // Fails when a null selection leaves the tint up.
  [Fact]
  public void Looking_away_clears_the_tint_and_aiming_again_recomputes() {
    var rig = new Rig("test:preview-" + Guid.NewGuid());
    rig.Drive(rig.Aim(1));

    rig.Drive(null);
    Assert.Empty(rig.Pushes().Last().Positions);

    rig.Drive(rig.Aim(1));
    Assert.Equal(2, rig.Computes);
  }

  // Fails when the leave-world handler is not registered.
  [Fact]
  public void Leaving_the_world_clears_the_tint() {
    var rig = new Rig("test:preview-" + Guid.NewGuid());
    rig.Drive(rig.Aim(1));

    rig.World.ClientApi.Event.LeaveWorld += Raise.Event<Action>();

    Assert.Empty(rig.Pushes().Last().Positions);
  }
}
