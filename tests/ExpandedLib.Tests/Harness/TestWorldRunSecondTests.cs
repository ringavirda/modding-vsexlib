using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A run second fires each tick listener at its own interval with that interval as dt,
/// carries the time a listener over 1000 ms accrued into the next second, and runs the network
/// once, at the second's end.</summary>
public class TestWorldRunSecondTests {
  private static readonly HashSet<long> None = [];

  // Fails when a second fires every listener once with dt 1, or fires the listeners due at one
  // moment other than in registration order.
  [Fact]
  public void Each_listener_fires_on_its_own_interval_with_that_interval_as_dt() {
    var world = new TestWorld();
    var log = new List<string>();
    world.World.RegisterGameTickListener(
      dt => log.Add($"a{(int)(dt * 1000)}"),
      250
    );
    world.World.RegisterGameTickListener(
      dt => log.Add($"b{(int)(dt * 1000)}"),
      1000
    );
    world.World.RegisterGameTickListener(
      dt => log.Add($"c{(int)(dt * 1000)}"),
      500
    );

    world.RunSecond(None, () => log.Add("network"), null);

    Assert.Equal(
      ["a250", "a250", "c500", "a250", "a250", "b1000", "c500", "network"],
      log
    );
  }

  // Fails when a listener over 1000 ms fires every second or loses the time it accrued at a
  // second's end.
  [Fact]
  public void A_listener_over_a_second_fires_when_its_accrued_time_reaches_its_interval() {
    var world = new TestWorld();
    var fired = new List<float>();
    world.World.RegisterGameTickListener(dt => fired.Add(dt), 1500);
    var counts = new List<int>();

    for (int s = 0; s < 3; s++) {
      world.RunSecond(None, () => { }, null);
      counts.Add(fired.Count);
    }

    Assert.Equal([0, 1, 2], counts);
    Assert.All(fired, dt => Assert.Equal(1.5f, dt));
  }

  // Fails when the network runs before its anchor's first fire of the second instead of its fire at
  // the second's end.
  [Fact]
  public void The_network_runs_before_its_anchor_only_at_the_seconds_end() {
    var world = new TestWorld();
    var log = new List<string>();
    long anchor = world.World.RegisterGameTickListener(_ => log.Add("a"), 250);

    world.RunSecond(None, () => log.Add("network"), anchor);

    Assert.Equal(["a", "a", "a", "network", "a"], log);
  }

  // Fails when a listener in except accrues time while it does not fire.
  [Fact]
  public void A_listener_in_except_accrues_no_time() {
    var world = new TestWorld();
    int fired = 0;
    long id = world.World.RegisterGameTickListener(_ => fired++, 1500);

    world.RunSecond(new HashSet<long> { id }, () => { }, null);
    world.RunSecond(None, () => { }, null);

    Assert.Equal(0, fired);
  }

  // Fails when a listener another listener removed earlier at the same moment still fires.
  [Fact]
  public void A_listener_removed_during_the_second_fires_no_more() {
    var world = new TestWorld();
    int fired = 0;
    long removed = 0;
    world.World.RegisterGameTickListener(
      _ => world.World.UnregisterGameTickListener(removed),
      500
    );
    removed = world.World.RegisterGameTickListener(_ => fired++, 250);

    world.RunSecond(None, () => { }, null);

    Assert.Equal(1, fired);
  }
}
