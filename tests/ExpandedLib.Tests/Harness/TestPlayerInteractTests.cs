using System.Collections.Generic;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestPlayer.Interact"/>: a right-click run as the server runs a player's block
/// use, a held one stepped at the runner's tick.</summary>
public class TestPlayerInteractTests {
  private sealed class Lever : Block {
    public bool Takes = true;
    public float DeclinesAt = float.MaxValue;
    public readonly List<string> Calls = new();
    public bool SneakAtStart;
    public bool CtrlAtStart;
    public ItemStack? HeldAtStart;

    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) {
      Calls.Add("start");
      SneakAtStart = byPlayer.Entity.Controls.ShiftKey;
      CtrlAtStart = byPlayer.Entity.Controls.CtrlKey;
      HeldAtStart = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
      return Takes;
    }

    public override bool OnBlockInteractStep(
      float secondsUsed,
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) {
      Calls.Add($"step {secondsUsed:0.0}");
      return secondsUsed < DeclinesAt;
    }

    public override void OnBlockInteractStop(
      float secondsUsed,
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) => Calls.Add($"stop {secondsUsed:0.0}");
  }

  private static readonly BlockPos At = new(0, 1, 0);

  private static (TestWorld, Lever, TestPlayer) Stand() {
    var world = new TestWorld();
    var lever = TestBlocks.Configure(new Lever(), "test:lever", 1);
    world.Place(At, lever);
    return (world, lever, world.Player());
  }

  #region Holding

  // Fails when a held interaction skips its steps or its stop.
  [Fact]
  public void A_held_interaction_steps_with_the_elapsed_time_then_stops() {
    var (world, lever, player) = Stand();

    bool taken = player.Interact(At, seconds: 0.3f);

    Assert.True(taken);
    Assert.Equal(
      ["start", "step 0.1", "step 0.2", "step 0.3", "stop 0.3"],
      lever.Calls
    );
  }

  // Fails when a declined step does not stop the hold there, as the server's use loop does.
  [Fact]
  public void A_declined_step_stops_the_hold_at_its_time() {
    var (world, lever, player) = Stand();
    lever.DeclinesAt = 0.2f;

    player.Interact(At, seconds: 1f);

    Assert.Equal(["start", "step 0.1", "step 0.2", "stop 0.2"], lever.Calls);
  }

  // Fails when a click with no hold steps, or does not stop.
  [Fact]
  public void A_click_starts_and_stops_without_a_step() {
    var (world, lever, player) = Stand();

    player.Interact(At);

    Assert.Equal(["start", "stop 0.0"], lever.Calls);
  }

  // Fails when a start that declines is still stepped or stopped.
  [Fact]
  public void A_declined_start_is_neither_stepped_nor_stopped() {
    var (world, lever, player) = Stand();
    lever.Takes = false;

    Assert.False(player.Interact(At, seconds: 0.5f));
    Assert.Equal(["start"], lever.Calls);
  }

  #endregion

  #region Gates and keys

  // Fails when the use-access check is skipped.
  [Fact]
  public void Without_use_access_the_block_is_not_asked() {
    var (world, lever, player) = Stand();
    world
      .World.Claims.TryAccess(
        player.Player,
        Arg.Any<BlockPos>(),
        EnumBlockAccessFlags.Use
      )
      .Returns(false);

    Assert.False(player.Interact(At));
    Assert.Empty(lever.Calls);
  }

  // Fails when a spectator's click reaches the block.
  [Fact]
  public void A_spectator_does_not_reach_the_block() {
    var (world, lever, player) = Stand();
    player.GameMode = EnumGameMode.Spectator;

    Assert.False(player.Interact(At));
    Assert.Empty(lever.Calls);
  }

  // Fails when the keys or the held stack are not in place at the start, or the keys outlast the
  // call.
  [Fact]
  public void Keys_and_the_held_stack_hold_for_the_call_only() {
    var (world, lever, player) = Stand();
    var stack = new ItemStack(world.RegisterItem("game:stick"));

    player.Interact(At, held: stack, sneak: true, ctrl: true);

    Assert.True(lever.SneakAtStart);
    Assert.True(lever.CtrlAtStart);
    Assert.Same(stack, lever.HeldAtStart);
    Assert.False(player.Sneaking);
    Assert.False(player.CtrlHeld);
  }

  [Fact]
  public void A_negative_hold_throws() {
    var (world, lever, player) = Stand();

    Assert.Throws<System.ArgumentOutOfRangeException>(() =>
      player.Interact(At, seconds: -1f)
    );
  }

  #endregion
}
