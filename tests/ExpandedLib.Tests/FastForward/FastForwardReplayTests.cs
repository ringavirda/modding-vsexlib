using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.FastForwarding;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A production machine that records each call and runs an optional body.</summary>
internal sealed class ReplayMachine : BlockEntityProductionMachine {
  public Action<ReplayMachine>? Work;
  public bool CanRun = true;
  public FastForwardMode? Mode;
  public int ProductionTicks;
  public int IdleTicks;
  public readonly List<double> Hours = [];

  protected override bool CanRunProduction => CanRun;

  protected override FastForwardMode FastForwardMode =>
    Mode ?? base.FastForwardMode;

  protected override void OnProductionTick(float dt) {
    ProductionTicks++;
    Hours.Add(TickGameHours);
    Work?.Invoke(this);
  }

  protected override void OnIdleProductionTick(float dt) => IdleTicks++;

  public double Stamp => ProcessOf(this).LastTickHours;

  private static BEBehaviorProductionMachine ProcessOf(BlockEntity be) =>
    be.Behaviors.OfType<BEBehaviorProductionMachine>().Single();
}

/// <summary>A machine that overrides the retired away catch-up members.</summary>
internal sealed class RetiredMembersMachine : BlockEntityProductionMachine {
  public int ProductionTicks;

  protected override bool CanRunProduction => true;

#pragma warning disable CS0618, CS0672
  protected override int MaxAwayCatchupSteps => 100;

  protected override float AwayCatchupStepSeconds => 0.01f;
#pragma warning restore CS0618, CS0672

  protected override void OnProductionTick(float dt) => ProductionTicks++;
}

/// <summary>The replay of the game time a group's real ticks did not cover: what steps, in what form, and where it stops.</summary>
[Collection(ExSoundsCollection.Name)]
public class FastForwardReplayTests {
  #region Fixtures

  private const double OneMachineSecond = 1.0 / GameTime.MachineSecondsPerHour;

  private static readonly BlockPos Cell = new(1, 0, 0);

  private sealed class LineNode : BlockNetworkNode {
    public bool OpenUp { get; init; }

    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.EAST
      || face == BlockFacing.WEST
      || (OpenUp && face == BlockFacing.UP);

    public static LineNode Of(bool openUp) =>
      TestBlocks.Configure(
        new LineNode { OpenUp = openUp },
        $"test:replayline-{(openUp ? "open" : "shut")}",
        openUp ? 2 : 1
      );
  }

  private static TestWorld PipeWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    for (int x = 0; x < 3; x++) {
      var pos = new BlockPos(x, 0, 0);
      w.Place(pos, LineNode.Of(x == 1));
      w.AddNode(pos, "pipe");
    }
    return w;
  }

  private static PipeNetwork Run(TestWorld w) =>
    Assert.IsType<PipeNetwork>(w.NetworkAt(Cell));

  private static FastForwardDriver Driver(TestWorld w) =>
    w.Networks.FastForward;

  private static ReplayMachine Place(
    TestWorld w,
    int x,
    Action<ReplayMachine>? work = null,
    Block? block = null
  ) {
    var machine = new ReplayMachine { Work = work };
    w.Place(
      new BlockPos(x, 5, 0),
      block ?? TestBlocks.Configure(new Block(), "test:replaymachine", 97),
      machine
    );
    w.Initialize(machine);
    return machine;
  }

  private static void Produce(TestWorld w, float litres) =>
    Run(w).TryProduceGas(litres, 20f, "Steam", w.Accessor);

  private static void Consume(TestWorld w, float litres) {
    if (Run(w).State!.Pressure > 0.1f)
      Run(w).TryConsumeGas(litres, w.Accessor);
  }

  private static (
    TestWorld World,
    ReplayMachine Producer,
    ReplayMachine Consumer
  ) Plant() {
    TestWorld w = PipeWorld();
    ReplayMachine producer = null!,
      consumer = null!;
    producer = Place(w, 0, _ => Produce(w, 6f));
    consumer = Place(w, 2, _ => Consume(w, 2f));
    w.FireBlockEntityTicks();
    w.Tick();
    return (w, producer, consumer);
  }

  #endregion

  #region Equivalence

  // Fails when the replay skips the networks' OnTick, so the run neither leaks through its vent nor settles.
  [Fact]
  public void Replay_equals_live() {
    var (live, liveProducer, liveConsumer) = Plant();
    for (int i = 0; i < 120; i++) {
      if (i < 119)
        live.FireBlockEntityTicks();
      for (int call = 0; call < 5; call++) {
        live.AdvanceHours(OneMachineSecond / 5);
        live.Networks.ListenerTick(live.Accessor, 0.2f);
      }
    }

    var (jumped, producer, consumer) = Plant();
    jumped.AdvanceHours(1);
    jumped.Tick();

    Assert.InRange(
      producer.ProductionTicks,
      liveProducer.ProductionTicks - 1,
      liveProducer.ProductionTicks + 1
    );
    Assert.InRange(
      consumer.ProductionTicks,
      liveConsumer.ProductionTicks - 1,
      liveConsumer.ProductionTicks + 1
    );
    Assert.Equal(Run(live).State!.Volume, Run(jumped).State!.Volume, 2);
    Assert.True(Run(jumped).State!.Volume < 3 * ExlibValues.LitresPerPipe);
  }

  #endregion

  #region The hold

  // Fails when the hold check is removed, so a live tick runs beside the replay of the same time.
  [Fact]
  public void A_held_machine_skips_its_live_tick() {
    var w = new TestWorld();
    ReplayMachine machine = Place(w, 0);
    Driver(w).MaxStepsPerCall = 1;
    w.AdvanceHours(10 * OneMachineSecond);

    for (int i = 0; i < 3; i++) {
      w.Networks.ListenerTick(w.Accessor, 0.2f);
      w.FireBlockEntityTicks();
    }

    Assert.Equal(3, machine.ProductionTicks);
  }

  #endregion

  #region The step

  // Fails when a replay step calls OnProductionTick directly, so a machine that is not ready still produces.
  [Fact]
  public void A_replay_step_passes_the_production_gate() {
    var w = new TestWorld();
    ReplayMachine machine = Place(w, 0);
    machine.CanRun = false;
    w.AdvanceHours(5 * OneMachineSecond);

    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(0, machine.ProductionTicks);
    Assert.Equal(4, machine.IdleTicks);
  }

  // Fails when a Replay machine is handed the span since its stamp in place of the call's dt over 120.
  [Fact]
  public void A_replayed_machine_sees_its_dt_as_hours() {
    var w = new TestWorld();
    ReplayMachine machine = Place(w, 0);
    w.AdvanceHours(5 * OneMachineSecond);

    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(4, machine.Hours.Count);
    Assert.All(machine.Hours, h => Assert.Equal(OneMachineSecond, h, 6));
  }

  #endregion

  #region Modes

  // Fails when the mode is ignored, so an Off machine is replayed.
  [Fact]
  public void An_off_machine_is_never_replayed() {
    var w = new TestWorld();
    var block = TestBlocks.Configure(new Block(), "test:offmachine", 96);
    block.Attributes = new JsonObject(
      JToken.Parse("{\"fastForward\":\"off\"}")
    );
    ReplayMachine machine = Place(w, 0, block: block);
    w.AdvanceHours(1);

    w.Tick();

    Assert.Equal(0, machine.ProductionTicks);
    w.FireBlockEntityTicks();
    Assert.Equal(1.0, Assert.Single(machine.Hours), 6);
  }

  #endregion

  #region The budget and the cap

  // Fails when the cap is removed, so a gap past 12 game hours is stepped to its end.
  [Fact]
  public void A_gap_past_the_cap_steps_twelve_hours_then_jumps() {
    var w = new TestWorld();
    ReplayMachine machine = Place(w, 0);
    w.AdvanceHours(20);

    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(1440, machine.ProductionTicks);
    Assert.Equal(w.Calendar.TotalHours, machine.Stamp, 6);
  }

  // Fails when the rest of the gap is dropped as the budget runs out.
  [Fact]
  public void A_budgeted_replay_resumes_next_call() {
    var w = new TestWorld();
    ReplayMachine machine = Place(w, 0);
    Driver(w).MaxStepsPerCall = 10;
    w.AdvanceHours(100 * OneMachineSecond);

    w.Networks.ListenerTick(w.Accessor, 0.2f);
    Assert.Equal(10, machine.ProductionTicks);

    for (int i = 0; i < 20; i++)
      w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.InRange(
      w.Calendar.TotalHours - machine.Stamp,
      0.0,
      OneMachineSecond
    );
    Assert.InRange(machine.ProductionTicks, 90, 100);
  }

  #endregion

  #region Sound and particles

  // Fails when the replay gate is removed from the sound and particle helpers.
  [Fact]
  public void No_sound_or_particle_in_a_replay() {
    var w = new TestWorld();
    ReplayMachine machine = Place(
      w,
      0,
      m => {
        ExSounds.Play(w.Api, m.Pos, ExSounds.Latch);
        ExParticles.Spawn(
          w.World,
          ExParticles.Smoke,
          new Vec3d(),
          new Vec3d(),
          new Vec3f(),
          new Vec3f(),
          1,
          1,
          1f,
          0f,
          0.1f,
          0.1f
        );
      }
    );

    w.FireBlockEntityTicks();
    int live = Effects(w);
    w.AdvanceHours(10 * OneMachineSecond);
    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(2, live);
    Assert.True(machine.ProductionTicks > 5);
    Assert.Equal(live, Effects(w));
  }

  private static int Effects(TestWorld w) =>
    w
      .World.ReceivedCalls()
      .Count(c => c.GetMethodInfo().Name is "PlaySoundAt" or "SpawnParticles");

  #endregion

  #region Retired members

  // Fails when a retired forwarder or SecondsBetween feeds a replay again.
  [Fact]
  public void The_retired_members_do_nothing() {
    var w = new TestWorld();
    ReplayMachine plain = Place(w, 0);
    var retired = new RetiredMembersMachine();
    w.Place(
      new BlockPos(4, 5, 0),
      TestBlocks.Configure(new Block(), "test:retiredmachine", 95),
      retired
    );
    w.Initialize(retired);
    w.AdvanceHours(1);

    w.Tick();

    Assert.Equal(plain.ProductionTicks, retired.ProductionTicks);
#pragma warning disable CS0618
    Assert.Equal(0.0, GameTime.SecondsBetween(0.0, 1.0));
#pragma warning restore CS0618
  }

  #endregion

  #region Topology

  // Fails when the replay runs on past the epoch rise.
  [Fact]
  public void A_topology_change_ends_the_group_replay() {
    TestWorld w = PipeWorld();
    int replayed = 0;
    bool cut = false;
    ReplayMachine machine = Place(
      w,
      1,
      _ => {
        w.Networks.GetNetworkAt(Cell);
        if (!FastForward.Replaying)
          return;
        replayed++;
        if (replayed == 5 && !cut) {
          cut = true;
          w.RemoveNode(Cell);
        }
      }
    );
    w.FireBlockEntityTicks();
    w.AdvanceHours(60 * OneMachineSecond);

    w.Networks.ListenerTick(w.Accessor, 0.2f);
    Assert.Equal(5, replayed);

    w.Networks.ListenerTick(w.Accessor, 0.2f);
    Assert.Equal(59, replayed);
    Assert.Equal(60, machine.ProductionTicks);
  }

  #endregion
}
