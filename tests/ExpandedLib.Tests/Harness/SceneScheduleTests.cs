using System;
using System.Collections.Generic;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="Scene.At"/> and <see cref="Scene.Every"/>: an action runs before the ticks of
/// the second it is due at, once or on its period.</summary>
public class SceneScheduleTests {
  private sealed class Counter : BlockEntity {
    public int Ticks;

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      RegisterGameTickListener(_ => Ticks++, 1000);
    }
  }

  private static (Scene, Counter) Stand() {
    var scene = new Scene();
    var counter = new Counter { Pos = new BlockPos(0, 1, 0) };
    scene.Machine(
      counter.Pos,
      TestBlocks.Configure(new Block(), "test:counter", 1),
      counter
    );
    return (scene, counter);
  }

  // Fails when a scheduled action runs after its second's ticks.
  [Fact]
  public void An_action_runs_before_its_seconds_ticks() {
    var (scene, counter) = Stand();
    int seen = -1;
    scene.At(2, () => seen = counter.Ticks);

    scene.Step(5);

    Assert.Equal(2, seen);
    Assert.Equal(5, scene.Second);
  }

  // Fails when an action due once runs again.
  [Fact]
  public void An_action_at_a_second_runs_once() {
    var (scene, _) = Stand();
    int runs = 0;
    scene.At(0, () => runs++);

    scene.Step(4);

    Assert.Equal(1, runs);
  }

  // Fails when a repeating action fires between its periods.
  [Fact]
  public void A_repeating_action_fires_on_its_period_and_not_between() {
    var (scene, _) = Stand();
    var at = new List<int>();
    scene.Every(3, () => at.Add(scene.Second), first: 1);

    scene.Step(10);

    Assert.Equal([1, 4, 7], at);
  }

  [Fact]
  public void A_passed_second_or_a_period_below_one_is_refused() {
    var (scene, _) = Stand();
    scene.Step(3);

    Assert.Throws<ArgumentOutOfRangeException>(() => scene.At(2, () => { }));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      scene.Every(2, () => { }, first: 1)
    );
    Assert.Throws<ArgumentOutOfRangeException>(() => scene.Every(0, () => { }, 3));
    Assert.Throws<ArgumentNullException>(() => scene.At(3, null!));
  }
}
