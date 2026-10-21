using System;
using System.Collections.Generic;
using ExpandedLib.FastForwarding;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A block entity with a registered tick that joins the fast-forward group through the behaviour.</summary>
internal sealed class TickMember : BlockEntity, IFastForwardMember {
  public int Ticks;
  public List<BlockPos> Declared = [];

  public TickMember() {
    Behaviors.Add(new BEBehaviorFastForward(this, _ => Ticks++, 1000));
  }

  public void FastForwardStep(float dt) => Ticks++;

  public int FastForwardIntervalMs => 1000;

  public IEnumerable<BlockPos> FastForwardCouplings => Declared;
}

/// <summary>Block entities with a registered tick: what a replay steps, what holds their live tick, and what couples them.</summary>
public class FastForwardMemberTests {
  private const double OneMachineSecond = 1.0 / GameTime.MachineSecondsPerHour;

  internal static TickMember Place(
    TestWorld w,
    BlockPos pos,
    params BlockPos[] declared
  ) {
    var member = new TickMember { Declared = [.. declared] };
    w.Place(
      pos,
      TestBlocks.Configure(new Block(), "test:tickmember", 94),
      member
    );
    w.Initialize(member);
    return member;
  }

  // Fails when the replay skips registered members.
  [Fact]
  public void A_registered_tick_is_stepped_by_a_replay() {
    var w = new TestWorld();
    TickMember member = Place(w, new BlockPos(0, 5, 0));
    w.AdvanceHours(5 * OneMachineSecond);

    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(4, member.Ticks);
  }

  // Fails when the behaviour's hold check is removed, so a live tick runs beside the replay of the same time.
  [Fact]
  public void A_registered_tick_is_held_while_its_group_replays() {
    var w = new TestWorld();
    TickMember member = Place(w, new BlockPos(0, 5, 0));
    w.Networks.FastForward.MaxStepsPerCall = 1;
    w.AdvanceHours(10 * OneMachineSecond);

    for (int i = 0; i < 3; i++) {
      w.Networks.ListenerTick(w.Accessor, 0.2f);
      w.FireBlockEntityTicks();
    }

    Assert.Equal(3, member.Ticks);
  }

  // Fails when FastForwardCouplings is not read, so two members linked only by a declared position stay apart.
  [Fact]
  public void A_declared_coupling_joins_its_group() {
    var w = new TestWorld();
    var far = new BlockPos(40, 5, 0);
    Place(w, new BlockPos(0, 5, 0), far);
    Place(w, far);

    w.Tick();

    FastForwardGroup group = Assert.Single(w.Networks.FastForward.Groups);
    Assert.Equal(2, group.Members.Count);
  }
}
