using System.Collections.Generic;
using System.Linq;
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

  private sealed class MixedNetwork(
    BlockNetworkModSystem system,
    List<string> log
  ) : RecordingNetwork(system, "mixed", log) {
    public override int StepIntervalMs => Nodes.Any(p => p.X == 0) ? 1000 : 200;
  }

  private static (TestWorld World, List<string> Log) NewWorld() {
    var log = new List<string>();
    var w = new TestWorld();
    w.RegisterNetwork(
      "lumped",
      sys => new RecordingNetwork(sys, "lumped", log)
    );
    w.RegisterNetwork("fast", sys => new FastNetwork(sys, log));
    w.RegisterNetwork("mixed", sys => new MixedNetwork(sys, log));
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

    w.Networks.ListenerTick(w.Accessor, 10f);

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
    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(["tick fast", "published fast"], log);

    for (int i = 0; i < 3; i++)
      w.Networks.ListenerTick(w.Accessor, 0.2f);
    log.Clear();
    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(
      ["tick lumped", "tick fast", "published lumped", "published fast"],
      log
    );
  }

  // Fails when accrued time is kept per network instance, so a rebuild between ticks restarts the interval.
  [Fact]
  public void A_rebuilt_network_keeps_its_types_phase() {
    var (w, _) = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    var before = Place(w, "lumped", pos);
    for (int i = 0; i < 3; i++)
      w.Networks.ListenerTick(w.Accessor, 0.2f);

    var after = (RecordingNetwork)
      w.Networks.RebuildFromRoot(w.Accessor, pos, "lumped")!;
    Assert.NotSame(before, after);
    w.Networks.ListenerTick(w.Accessor, 0.2f);
    Assert.Empty(after.Steps);
    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal([1f], after.Steps);
  }

  // Fails when ServerTick waits for the interval to accrue instead of stepping each network once per call.
  [Fact]
  public void ServerTick_steps_every_network_once_with_its_dt() {
    var (w, _) = NewWorld();
    var lumped = Place(w, "lumped", new BlockPos(0, 0, 0));
    var fast = Place(w, "fast", new BlockPos(5, 0, 0));

    w.Networks.ServerTick(w.Accessor, 0.5f);
    w.Networks.ServerTick(w.Accessor, 5f);

    Assert.Equal([0.5f, 2f], lumped.Steps);
    Assert.Equal([0.4f, 0.4f], fast.Steps);
  }

  // Fails when a late fire resets the type's phase to zero instead of keeping its overshoot, or dt is not the real
  // time since the type's last step.
  [Fact]
  public void Late_fires_keep_the_cadence_and_the_elapsed_time() {
    var (w, _) = NewWorld();
    var net = Place(w, "lumped", new BlockPos(0, 0, 0));

    int fires = (int)(60f / 0.233f);
    for (int i = 0; i < fires; i++)
      w.Networks.ListenerTick(w.Accessor, 0.233f);

    double elapsed = fires * 0.233;
    Assert.InRange(net.Steps.Count, 59, 61);
    Assert.InRange(net.Steps.Sum(), elapsed - 1.0, elapsed);
    Assert.All(net.Steps, dt => Assert.InRange(dt, 0.9f, 1.2f));
  }

  // Fails when each network reads its own interval, so a network whose type's first network is slower steps faster.
  [Fact]
  public void A_types_interval_is_read_once_for_all_its_networks() {
    var (w, _) = NewWorld();
    var slow = Place(w, "mixed", new BlockPos(0, 0, 0));
    var other = Place(w, "mixed", new BlockPos(5, 0, 0));

    w.Tick(1);

    Assert.Equal([1f], slow.Steps);
    Assert.Equal([1f], other.Steps);
  }
}
