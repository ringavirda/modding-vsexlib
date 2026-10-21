using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A production machine that opts into away-catch-up with a configurable step cap.</summary>
internal sealed class CatchupMachine : BlockEntityProductionMachine {
  public int ProductionTicks;
  public int Steps;
  public double StepHours;
  public readonly List<(float Dt, double Hours)> Calls = [];

  protected override bool CanRunProduction => true;

  protected override void OnProductionTick(float dt) {
    ProductionTicks++;
    Calls.Add((dt, TickGameHours));
  }

  protected override int MaxAwayCatchupSteps => Steps;

  protected override double GameStepHours => StepHours;

  public void Drive(float dt) => DriveProductionTick(dt);

  public void Stop() => StopProductionTick();

  public void StartTicking() => StartProductionTick();
}

/// <summary>Game-time away-catch-up: a machine with <c>MaxAwayCatchupSteps &gt; 0</c> replays the
/// game hours it spent unloaded as bounded sub-ticks on reload. Off by default.</summary>
public class GameTimeCatchupTests {
  #region GameTime helper

  [Theory]
  [InlineData(0.0, 1.0, 3600.0)] // one hour -> 3600 game-seconds
  [InlineData(2.0, 2.5, 1800.0)] // half an hour
  [InlineData(5.0, 4.0, 0.0)] // clock ran backwards -> clamped to 0
  public void SecondsBetween_is_the_nonnegative_hour_gap_in_seconds(
    double from,
    double to,
    double expected
  ) {
    Assert.Equal(expected, GameTime.SecondsBetween(from, to), 3);
  }

  [Fact]
  public void CatchUp_replays_a_short_gap_in_full() {
    int steps = 0;
    float total = 0f;
    int ran = GameTime.CatchUp(
      3.6,
      1f,
      100,
      dt => {
        steps++;
        total += dt;
      }
    );

    Assert.Equal(4, ran); // 1 + 1 + 1 + 0.6
    Assert.Equal(4, steps);
    Assert.Equal(3.6f, total, 3); // conserves the elapsed time
  }

  [Fact]
  public void CatchUp_caps_a_long_gap_at_the_step_budget() {
    int ran = GameTime.CatchUp(3600.0, 1f, 10, _ => { });
    Assert.Equal(10, ran); // an hour would be 3600 steps; capped at 10
  }

  [Theory]
  [InlineData(0)] // no step budget
  [InlineData(5)] // a step budget, but no elapsed time
  public void CatchUp_does_nothing_without_elapsed_time_or_budget(int maxSteps) {
    Assert.Equal(0, GameTime.CatchUp(0.0, 1f, maxSteps, _ => { }));
  }

  #endregion

  #region Machine integration (reload)

  private static Block MachineBlock() =>
    TestBlocks.Configure(new Block(), "test:catchupmachine", 99);

  private static CatchupMachine Reload(int steps, double awayHours) {
    var world = new TestWorld();
    Block block = MachineBlock();
    var m1 = new CatchupMachine {
      Pos = new BlockPos(0, 0, 0),
      Block = block,
      Steps = steps,
    };
    world.Attach(m1);
    m1.StartTicking();
    world.FireBlockEntityTicks(); // simulated once at hour 0
    var tree = new TreeAttribute();
    m1.ToTreeAttributes(tree);
    m1.OnBlockUnloaded(); // stop m1's tick before the reload

    world.AdvanceHours(awayHours);

    var m2 = new CatchupMachine {
      Pos = new BlockPos(0, 0, 0),
      Block = block,
      Steps = steps,
    };
    m2.FromTreeAttributes(tree, world.World);
    world.Attach(m2);
    m2.StartTicking();
    world.FireBlockEntityTicks(); // first tick after reload: catch up, then the normal tick
    return m2;
  }

  [Fact]
  public void A_short_absence_catches_up_proportionally() {
    // 3.6 game-seconds away -> 4 catch-up sub-ticks, then 1 normal tick.
    CatchupMachine m = Reload(steps: 100, awayHours: 0.001);
    Assert.Equal(5, m.ProductionTicks);
  }

  [Fact]
  public void A_long_absence_is_capped_at_the_step_budget() {
    // An hour away is 3600 sub-ticks; capped at 10, plus the 1 normal tick.
    CatchupMachine m = Reload(steps: 10, awayHours: 1.0);
    Assert.Equal(11, m.ProductionTicks);
  }

  [Fact]
  public void Away_catchup_is_off_by_default() {
    // Steps = 0 (the base default): the machine just resumes, no replay.
    CatchupMachine m = Reload(steps: 0, awayHours: 1.0);
    Assert.Equal(1, m.ProductionTicks);
  }

  [Fact]
  public void A_fresh_machine_never_catches_up() {
    var world = new TestWorld();
    var m = new CatchupMachine { Pos = new BlockPos(0, 0, 0), Steps = 10 };
    world.Attach(m);
    m.StartTicking();

    world.AdvanceHours(5); // time passes, but the machine was never simulated before
    world.FireBlockEntityTicks();

    Assert.Equal(1, m.ProductionTicks); // just the one normal tick
  }

  #endregion

  #region The tick's span

  private static (TestWorld world, CatchupMachine machine) Stamped(
    double stepHours = 0
  ) {
    var world = new TestWorld();
    var m = new CatchupMachine {
      Pos = new BlockPos(0, 0, 0),
      StepHours = stepHours,
    };
    world.Attach(m);
    m.Drive(1f);
    m.Calls.Clear();
    return (world, m);
  }

  [Fact]
  public void A_tick_hands_on_the_calendar_hours_since_the_last_tick() {
    var (world, m) = Stamped();

    world.AdvanceHours(0.5);
    m.Drive(1f);

    Assert.Equal(0.5, Assert.Single(m.Calls).Hours, 6);
  }

  [Fact]
  public void A_span_longer_than_the_step_runs_as_steps() {
    var (world, m) = Stamped(stepHours: 0.25);

    world.AdvanceHours(1);
    m.Drive(1f);

    Assert.Equal(4, m.Calls.Count);
    Assert.Equal(1.0, m.Calls.Sum(c => c.Hours), 6);
  }

  [Fact]
  public void Only_the_first_step_carries_the_real_dt() {
    var (world, m) = Stamped(stepHours: 0.25);

    world.AdvanceHours(1);
    m.Drive(1f);

    Assert.Equal(1f, m.Calls[0].Dt);
    Assert.All(m.Calls.Skip(1), c => Assert.Equal(0f, c.Dt));
    Assert.Equal(1f, m.Calls.Sum(c => c.Dt));
  }

  [Fact]
  public void A_span_past_the_cap_is_cut_to_the_cap() {
    var (world, m) = Stamped(stepHours: 0.25);

    world.AdvanceHours(100);
    m.Drive(1f);
    Assert.Equal(24.0, m.Calls.Sum(c => c.Hours), 4);

    m.Calls.Clear();
    world.AdvanceHours(0.5);
    m.Drive(1f);
    Assert.Equal(0.5, m.Calls.Sum(c => c.Hours), 4);
  }

  [Fact]
  public void A_machine_without_a_step_gets_one_call() {
    var (world, m) = Stamped();

    world.AdvanceHours(3);
    m.Drive(1f);

    Assert.Equal(3.0, Assert.Single(m.Calls).Hours, 6);
  }

  [Fact]
  public void A_span_of_zero_still_runs_one_call() {
    var (_, m) = Stamped(stepHours: 0.25);

    m.Drive(0.7f);

    var call = Assert.Single(m.Calls);
    Assert.Equal(0.7f, call.Dt);
    Assert.Equal(0.0, call.Hours);
  }

  [Fact]
  public void A_start_on_a_running_machine_keeps_the_stamp() {
    var (world, m) = Stamped();
    m.StartTicking();
    m.Drive(1f);
    m.Calls.Clear();

    world.AdvanceHours(2);
    m.StartTicking();
    m.Drive(1f);

    Assert.Equal(2.0, Assert.Single(m.Calls).Hours, 6);
  }

  [Fact]
  public void A_restart_while_loaded_starts_a_fresh_span() {
    var (world, m) = Stamped();
    m.StartTicking();
    m.Drive(1f);
    m.Calls.Clear();

    m.Stop();
    world.AdvanceHours(5);
    m.StartTicking();
    m.Drive(1f);

    Assert.Equal(0.0, Assert.Single(m.Calls).Hours);
  }

  [Fact]
  public void Replayed_away_steps_carry_no_game_hours() {
    CatchupMachine m = Reload(steps: 100, awayHours: 0.001);

    Assert.Equal(5, m.Calls.Count);
    Assert.All(m.Calls.Take(4), c => Assert.Equal(0.0, c.Hours));
    Assert.Equal(0.001, m.Calls[4].Hours, 6);
  }

  [Fact]
  public void A_calendar_moved_back_gives_no_span() {
    var (world, m) = Stamped();
    world.AdvanceHours(5);
    m.Drive(1f);
    m.Calls.Clear();

    world.AdvanceHours(-3);
    m.Drive(1f);

    Assert.Equal(0.0, Assert.Single(m.Calls).Hours);
  }

  #endregion
}
