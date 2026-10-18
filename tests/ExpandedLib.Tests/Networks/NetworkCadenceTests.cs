using System.Collections.Generic;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Exercises the single 200 ms listener: <see cref="BlockNetwork.StepIntervalMs"/> and
/// <see cref="BlockNetwork.OnTickPublished"/>.
/// </summary>
public class NetworkCadenceTests {
  private sealed class Member(BlockEntity be, string networkType)
    : BEBehaviorNetworkMember(be) {
    public override string NetworkType => networkType;
  }

  private class RecordingNetwork(
    BlockNetworkModSystem system,
    string type,
    List<string> log
  ) : BlockNetwork(system) {
    public List<float> Steps { get; } = [];

    public override string NetworkType => type;

    public override void OnMerge(BlockNetwork other, IBlockAccessor world) { }

    public override void OnSplitFragment(
      BlockNetwork original,
      IBlockAccessor world
    ) { }

    public override void OnTick(
      IBlockAccessor world,
      float dt,
      BlockNetworkModSystem manager
    ) {
      Steps.Add(dt);
      log.Add($"tick {type}");
    }

    public override void OnTickPublished(
      IBlockAccessor world,
      BlockNetworkModSystem manager
    ) => log.Add($"published {type}");
  }

  private sealed class FastNetwork(
    BlockNetworkModSystem system,
    List<string> log
  ) : RecordingNetwork(system, "fast", log) {
    public override int StepIntervalMs => 200;
  }

  private static (TestWorld World, List<string> Log) NewWorld() {
    var log = new List<string>();
    var w = new TestWorld();
    w.RegisterNetwork("lumped", sys => new RecordingNetwork(sys, "lumped", log));
    w.RegisterNetwork("fast", sys => new FastNetwork(sys, log));
    return (w, log);
  }

  private static RecordingNetwork Place(
    TestWorld w,
    string networkType,
    BlockPos pos
  ) {
    TestMemberBlockEntity be = TestMemberBlockEntity.Carrying();
    be.Behaviors.Add(new Member(be, networkType));
    w.Place(pos, TestNetworkBlock.Create(networkType, "ns", 1), be);
    w.Initialize(be);
    return (RecordingNetwork)w.Networks.GetNetworkAt(pos)!;
  }

  // Fails when a default-interval network steps at the listener's 200 ms instead of once per 1000 ms.
  [Fact]
  public void Lumped_CadenceUnchanged() {
    var (w, _) = NewWorld();
    var net = Place(w, "lumped", new BlockPos(0, 0, 0));

    w.Tick(3);

    Assert.Equal(3, net.Steps.Count);
    Assert.All(net.Steps, dt => Assert.Equal(1f, dt, 3));
  }

  // Fails when a network with a 200 ms interval is stepped once per second, or its dt is not the accrued 0.2 s.
  [Fact]
  public void A_200_ms_network_steps_five_times_a_second() {
    var (w, _) = NewWorld();
    var net = Place(w, "fast", new BlockPos(0, 0, 0));

    w.Tick(2);

    Assert.Equal(10, net.Steps.Count);
    Assert.All(net.Steps, dt => Assert.Equal(0.2f, dt, 3));
  }

  // Fails when the accrued time is passed uncapped instead of at twice the interval.
  [Fact]
  public void A_stalled_server_caps_dt_at_twice_the_interval() {
    var (w, _) = NewWorld();
    var lumped = Place(w, "lumped", new BlockPos(0, 0, 0));
    var fast = Place(w, "fast", new BlockPos(5, 0, 0));

    w.Networks.ServerTick(w.Accessor, 10f);

    Assert.Equal([2f], lumped.Steps);
    Assert.Equal([0.4f], fast.Steps);
  }

  // Fails when OnTickPublished runs after each network's own step instead of after every network has stepped,
  // or runs for a network that did not step.
  [Fact]
  public void Published_follows_every_step_and_only_for_stepped_networks() {
    var (w, log) = NewWorld();
    Place(w, "lumped", new BlockPos(0, 0, 0));
    Place(w, "fast", new BlockPos(5, 0, 0));

    w.Tick(1);
    log.Clear();
    w.Networks.ServerTick(w.Accessor, 0.2f);

    Assert.Equal(["tick fast", "published fast"], log);

    for (int i = 0; i < 3; i++)
      w.Networks.ServerTick(w.Accessor, 0.2f);
    log.Clear();
    w.Networks.ServerTick(w.Accessor, 0.2f);

    Assert.Equal(
      ["tick lumped", "tick fast", "published lumped", "published fast"],
      log
    );
  }
}
