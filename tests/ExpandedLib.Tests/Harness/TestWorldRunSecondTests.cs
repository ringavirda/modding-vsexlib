using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A run second fires each tick listener at its own interval with that interval as dt,
/// carries the time a listener over 1000 ms accrued into the next second, and fires the network
/// every 200 ms among them in time order.</summary>
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
      [
        "network",
        "a250",
        "network",
        "a250",
        "c500",
        "network",
        "a250",
        "network",
        "a250",
        "b1000",
        "c500",
        "network",
      ],
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

  // Fails when the network runs after its anchor at the second's end, or only once in the second.
  [Fact]
  public void The_network_runs_before_its_anchor_where_both_are_due_and_on_its_own_between() {
    var world = new TestWorld();
    var log = new List<string>();
    long anchor = world.World.RegisterGameTickListener(_ => log.Add("a"), 250);

    world.RunSecond(None, () => log.Add("network"), anchor);

    Assert.Equal(
      [
        "network",
        "a",
        "network",
        "a",
        "network",
        "a",
        "network",
        "network",
        "a",
      ],
      log
    );
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

  // Fails when the network runs once at the second's end: it then sees the load the 250 ms
  // listener raised at 250 and 750 ms only after the listener dropped it again.
  [Fact]
  public void A_load_raised_and_dropped_inside_a_second_reaches_the_network() {
    var world = new TestWorld();
    var log = new List<string>();
    bool raised = false;
    world.World.RegisterGameTickListener(
      _ => {
        raised = !raised;
        log.Add("a");
      },
      250
    );

    world.RunSecond(None, () => log.Add(raised ? "n1" : "n0"), null);

    Assert.Equal(["n0", "a", "n1", "a", "n0", "a", "n1", "a", "n0"], log);
  }

  // Fails when the network fires other than five times a second 200 ms apart, or before the
  // listeners due with it when no listener anchors it.
  [Fact]
  public void The_network_fires_every_200_ms_after_the_listeners_due_with_it() {
    var world = new TestWorld();
    int fired = 0;
    var seen = new List<int>();
    world.World.RegisterGameTickListener(_ => fired++, 100);

    world.RunSecond(None, () => seen.Add(fired), null);

    Assert.Equal([2, 4, 6, 8, 10], seen);
  }

  // Fails when the network runs before its anchor only at the second's end, or after it.
  [Fact]
  public void The_network_fires_before_its_anchor_at_every_moment_both_are_due() {
    var world = new TestWorld();
    int fired = 0;
    var seen = new List<int>();
    long anchor = world.World.RegisterGameTickListener(_ => fired++, 100);

    world.RunSecond(None, () => seen.Add(fired), anchor);

    Assert.Equal([1, 3, 5, 7, 9], seen);
  }

  private static List<string> SecondLog(
    System.Action<TestWorld, System.Action> run
  ) {
    var world = new TestWorld();
    var log = new List<string>();
    world.World.RegisterGameTickListener(_ => log.Add("a"), 250);
    world.World.RegisterGameTickListener(_ => log.Add("b"), 1000);
    world.World.RegisterGameTickListener(_ => log.Add("c"), 300);
    world.World.RegisterCallback(_ => log.Add("callback"), 10);
    for (int s = 0; s < 2; s++)
      run(world, () => log.Add("network"));
    log.Add($"at {world.MsIntoSecond}");
    return log;
  }

  // Fails when a slice restarts the network's 200 ms count, or a delayed callback comes due before
  // the second's end.
  [Fact]
  public void Slices_of_any_lengths_run_a_second_as_run_second_does() {
    List<string> whole = SecondLog((w, n) => w.RunSecond(None, n, null));
    List<string> sliced = SecondLog(
      (w, n) => {
        foreach (int ms in new[] { 30, 170, 130, 70, 450, 150 })
          w.RunSlice(ms, None, n, null);
      }
    );

    Assert.Equal(
      [
        "network",
        "a",
        "c",
        "network",
        "a",
        "c",
        "network",
        "a",
        "network",
        "c",
        "a",
        "b",
        "callback",
        "network",
        "c",
        "network",
        "a",
        "network",
        "a",
        "c",
        "network",
        "a",
        "c",
        "network",
        "a",
        "b",
        "network",
        "at 0",
      ],
      whole
    );
    Assert.Equal(whole, sliced);
  }

  // Fails when a slice may run past the second's end or no time at all, or a second ended leaves its
  // time standing.
  [Fact]
  public void A_slice_stops_at_the_second_s_end() {
    var world = new TestWorld();
    Assert.Throws<System.ArgumentOutOfRangeException>(() =>
      world.RunSlice(0, None, () => { }, null)
    );
    world.RunSlice(600, None, () => { }, null);

    Assert.Equal(600, world.MsIntoSecond);
    Assert.Throws<System.ArgumentOutOfRangeException>(() =>
      world.RunSlice(500, None, () => { }, null)
    );
    world.RunSlice(400, None, () => { }, null);
    Assert.Equal(0, world.MsIntoSecond);
  }
}
