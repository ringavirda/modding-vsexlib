using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The listeners a tick names in its except set do not fire, under either
/// <see cref="TestWorld"/> overload the setup runner ticks the world with.</summary>
public class SetupTickExceptTests {
  // Fails when FireBlockEntityTicks ignores its except set and fires a listener named in it.
  [Fact]
  public void A_listener_named_in_except_does_not_fire_under_either_tick_overload() {
    var world = new TestWorld();
    int skipped = 0;
    int ticked = 0;
    long skippedId = world.World.RegisterGameTickListener(_ => skipped++, 1000);
    world.World.RegisterGameTickListener(_ => ticked++, 1000);
    var except = new HashSet<long> { skippedId };

    world.FireBlockEntityTicks(1f, except);
    Assert.Equal(0, skipped);
    Assert.Equal(1, ticked);

    world.FireBlockEntityTicks(1f, except, () => { }, null);
    Assert.Equal(0, skipped);
    Assert.Equal(2, ticked);
  }

  // Fails when the except skip runs before the networkBefore test: the network then runs last, after the listener.
  [Fact]
  public void The_network_runs_at_the_place_of_its_anchor_listener_when_that_listener_is_in_except() {
    var world = new TestWorld();
    var order = new List<string>();
    long anchor = world.World.RegisterGameTickListener(
      _ => order.Add("anchor"),
      1000
    );
    world.World.RegisterGameTickListener(_ => order.Add("after"), 1000);

    world.FireBlockEntityTicks(
      1f,
      new HashSet<long> { anchor },
      () => order.Add("network"),
      anchor
    );

    Assert.Equal(["network", "after"], order);
  }
}
