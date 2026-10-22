using System.Linq;
using ExpandedLib.FastForwarding;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A group with a member away is frozen: nothing in it steps live or in a replay until the last member is back, then the whole gap replays.</summary>
public class FastForwardFreezeTests {
  #region Fixtures

  private static readonly BlockPos A = new(0, 5, 0);

  // A chunk of its own, so one can be unloaded without the other.
  private static readonly BlockPos B = new(40, 5, 0);

  private static FastForwardDriver Driver(TestWorld w) =>
    w.Networks.FastForward;

  private static TickMember Member(TestWorld w, BlockPos pos) =>
    Assert.IsType<TickMember>(w.GetBlockEntity(pos));

  private static TestWorld Pair() {
    var w = new TestWorld();
    FastForwardMemberTests.Place(w, A, B);
    FastForwardMemberTests.Place(w, B);
    w.Tick();
    return w;
  }

  private sealed class LineNode : BlockNetworkNode {
    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.EAST || face == BlockFacing.WEST;
  }

  // A plain block entity hosting the behaviour directly, as the bench and the mill do.
  private sealed class HostedMachine : BlockEntity {
    public HostedMachine() {
      Behaviors.Add(new HostedProduction(this));
    }
  }

  private sealed class HostedProduction(BlockEntity be)
    : BEBehaviorProductionMachine(be) {
    protected override void OnProductionTick(float dt) { }
  }

  #endregion

  // Fails when IsFrozen answers false, so a caller reading it never sees the group away.
  [Fact]
  public void IsFrozen_holds_while_a_member_is_away() {
    TestWorld w = Pair();
    var inNone = new BlockPos(0, 5, 20);
    w.UnloadChunkAt(B, unloadEntities: true);
    w.Tick();

    Assert.True(FastForward.IsFrozen(w.World, A));
    Assert.False(FastForward.IsFrozen(w.World, inNone));

    w.LoadChunkAt(B);
    w.Tick();

    Assert.False(FastForward.IsFrozen(w.World, A));
  }

  // Fails when the behaviour's save skips ff_peers, so a hosted machine reloaded beside an absent peer runs alone.
  [Fact]
  public void A_hosted_behaviour_saves_its_peers() {
    var w = new TestWorld();
    var host = new HostedMachine();
    w.Place(
      A,
      TestBlocks.Configure(new Block(), "test:hostedmachine", 95),
      host
    );
    w.Initialize(host);
    FastForwardMemberTests.Place(w, B, A);
    w.Tick();
    w.UnloadChunkAt(B, unloadEntities: true);
    Driver(w).Remove(B);

    w.Reload(A);
    w.AdvanceHours(1);
    w.Tick(2);

    Assert.True(Driver(w).Groups.Single().Frozen);
    Assert.True(FastForward.IsFrozen(w.World, A));
  }

  // Fails when an absent member is ignored, so the loaded member steps through the hour its partner missed.
  [Fact]
  public void An_absent_member_freezes_its_group() {
    TestWorld w = Pair();
    double cursor = Driver(w).CursorOf(A)!.Value;
    w.UnloadChunkAt(B, unloadEntities: true);
    w.AdvanceHours(1);

    w.Tick(5);
    w.FireBlockEntityTicks();

    Assert.Equal(0, Member(w, A).Ticks);
    Assert.Equal(cursor, Driver(w).CursorOf(A)!.Value, 9);
  }

  // Fails when a loaded member takes the present as its cursor, so a reloaded member has no gap left to replay.
  [Fact]
  public void The_gap_replays_when_the_last_member_loads() {
    TestWorld w = Pair();
    w.UnloadChunkAt(B, unloadEntities: true);
    w.AdvanceHours(1);
    w.Tick(5);
    w.Reload(A);

    w.LoadChunkAt(B);
    w.Tick();

    Assert.InRange(Member(w, A).Ticks, 119, 120);
    Assert.InRange(Member(w, B).Ticks, 119, 120);
  }

  // Fails when ff_peers is not read, so a member whose partner has not loaded since a restart runs alone.
  [Fact]
  public void Saved_peers_freeze_after_a_restart() {
    TestWorld w = Pair();
    w.UnloadChunkAt(B, unloadEntities: true);
    Driver(w).Remove(B);
    w.Reload(A);
    w.AdvanceHours(1);

    w.Tick(5);
    w.FireBlockEntityTicks();

    Assert.Equal(0, Member(w, A).Ticks);
    Assert.True(Driver(w).Groups.Single().Frozen);
  }

  // Fails when a saved peer found empty is kept, so the member stays frozen for a partner that is gone.
  [Fact]
  public void A_peer_that_loads_empty_is_dropped() {
    TestWorld w = Pair();
    w.UnloadChunkAt(B, unloadEntities: true);
    Driver(w).Remove(B);
    w.Reload(A);
    w.Accessor.SetBlock(0, B);
    w.AdvanceHours(1);

    w.LoadChunkAt(B);
    w.Tick();

    Assert.InRange(Member(w, A).Ticks, 119, 120);
    Assert.False(Driver(w).Groups.Single().Frozen);
  }

  // Fails when a group of networks with no member is frozen by a chunk away.
  [Fact]
  public void A_run_with_no_member_keeps_stepping() {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    for (int x = 30; x < 34; x++) {
      var pos = new BlockPos(x, 0, 0);
      w.Place(
        pos,
        TestBlocks.Configure(new LineNode(), $"test:freezeline", 93)
      );
      w.AddNode(pos, "pipe");
    }
    var run = (PipeNetwork)w.NetworkAt(new BlockPos(30, 0, 0))!;
    run.TryProduceGas(6f, 20f, "Steam", w.Accessor);
    float before = run.State!.Volume;
    w.UnloadChunkAt(new BlockPos(33, 0, 0));

    w.Tick(2);

    Assert.False(Driver(w).IsNetworkHeld(run.Id));
    Assert.NotEqual(before, run.State!.Volume);
  }
}
