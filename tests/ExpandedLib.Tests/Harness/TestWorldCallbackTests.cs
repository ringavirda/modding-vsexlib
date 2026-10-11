using System.Collections.Generic;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A delayed callback runs once, in the step that passes its due time, and never after it
/// is unregistered or its block entity is removed.</summary>
public class TestWorldCallbackTests {
  [Fact]
  public void A_delayed_callback_fires_once_in_the_step_that_passes_its_due_time_with_its_lateness() {
    var world = new TestWorld();
    var fired = new List<float>();
    world.World.RegisterCallback(dt => fired.Add(dt), 1500);

    world.FireBlockEntityTicks();
    Assert.Empty(fired);

    world.FireBlockEntityTicks();
    Assert.Equal(0.5f, Assert.Single(fired));

    world.FireBlockEntityTicks();
    Assert.Single(fired);
  }

  [Fact]
  public void A_callback_comes_due_when_block_entity_time_advances_past_it() {
    var world = new TestWorld();
    var fired = new List<float>();
    world.World.RegisterCallback(dt => fired.Add(dt), 1000);

    world.AdvanceBlockEntityTime(1500);

    Assert.Equal(0.5f, Assert.Single(fired));
  }

  [Fact]
  public void An_unregistered_callback_never_fires() {
    var world = new TestWorld();
    int fired = 0;
    long id = world.World.RegisterCallback(_ => fired++, 1000);
    world.World.UnregisterCallback(id);

    world.FireBlockEntityTicks(times: 2);

    Assert.Equal(0, fired);
  }

  [Fact]
  public void A_callback_registered_by_an_entity_fires_once_when_due() {
    var (world, _, be) = Placed(1);

    world.FireBlockEntityTicks();
    Assert.Equal(1, be.Fired);

    world.FireBlockEntityTicks();
    Assert.Equal(1, be.Fired);
  }

  [Fact]
  public void A_callback_registered_by_a_removed_entity_never_fires() {
    var (world, pos, be) = Placed(2);

    world.Accessor.RemoveBlockEntity(pos);

    world.FireBlockEntityTicks(times: 2);
    Assert.Equal(0, be.Fired);
  }

  [Fact]
  public void A_callback_unregistered_by_an_earlier_callback_in_the_same_step_never_fires() {
    var world = new TestWorld();
    int second = 0;
    long later = 0;
    world.World.RegisterCallback(
      _ => world.World.UnregisterCallback(later),
      1000
    );
    later = world.World.RegisterCallback(_ => second++, 1000);

    world.FireBlockEntityTicks();

    Assert.Equal(0, second);
  }

  private static (TestWorld, BlockPos, CallbackBe) Placed(int id) {
    var world = new TestWorld();
    var pos = new BlockPos(id, 0, 0);
    var be = new CallbackBe();
    world.Place(
      pos,
      TestBlocks.Configure(new Block(), $"test:callbacker{id}", id),
      be
    );
    world.Initialize(be);
    return (world, pos, be);
  }

  private sealed class CallbackBe : BlockEntity {
    public int Fired;

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      RegisterDelayedCallback(_ => Fired++, 1000);
    }
  }
}
