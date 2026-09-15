using System;
using System.Linq;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A machine whose own teardown work runs on a chosen side of its <c>base</c> call, standing
/// in for the two orderings the tree uses.</summary>
internal sealed class OrderedTeardownMachine : BlockEntityProductionMachine {
  public bool BaseFirst;
  public Action? DuringTeardown;
  public int ProductionTicks;

  protected override bool CanRunProduction => true;

  protected override void OnProductionTick(float dt) => ProductionTicks++;

  public void StartTicking() => StartProductionTick();

  public override void OnBlockRemoved() {
    if (BaseFirst) {
      base.OnBlockRemoved();
      DuringTeardown?.Invoke();
    } else {
      DuringTeardown?.Invoke();
      base.OnBlockRemoved();
    }
  }

  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    DuringTeardown?.Invoke();
  }
}

/// <summary>The production process as a hosted behaviour: a machine carries one from construction,
/// and it remains the only writer of the saved timestamp. See
/// docs/design/mechanics/framework-composition.md.</summary>
public class ProductionProcessTests {
  private const string MachineCode = "test:processmachine";

  private static (TestWorld world, TestProductionMachine machine) Placed(
    bool autoStart = true
  ) {
    var world = new TestWorld();
    var machine = new TestProductionMachine { AutoStart = autoStart };
    world.Place(
      new BlockPos(0, 0, 0),
      TestBlocks.Configure(new Block(), MachineCode, 98),
      machine
    );
    return (world, machine);
  }

  private static BEBehaviorProductionMachine ProcessOf(BlockEntity be) =>
    be.Behaviors.OfType<BEBehaviorProductionMachine>().Single();

  #region Hosting

  [Fact]
  public void A_machine_carries_one_process_from_construction() {
    var machine = new TestProductionMachine();

    Assert.Null(machine.Api);
    Assert.Single(machine.Behaviors.OfType<BEBehaviorProductionMachine>());
  }

  [Fact]
  public void Initialize_registers_the_tick_through_the_process() {
    var (world, machine) = Placed();

    world.Initialize(machine);
    world.FireBlockEntityTicks();

    Assert.Equal(1, machine.ProductionTicks);
  }

  [Fact]
  public void A_machine_that_defers_its_start_registers_no_tick_on_load() {
    var (world, machine) = Placed(autoStart: false);

    world.Initialize(machine);
    world.FireBlockEntityTicks();
    Assert.Equal(0, machine.ProductionTicks);

    machine.StartTicking();
    world.FireBlockEntityTicks();
    Assert.Equal(1, machine.ProductionTicks);
  }

  #endregion

  #region The tick handle

  // A handle kept behind names a stale listener; the idempotence guard then refuses every later start.
  [Theory]
  [InlineData(true)] // removal
  [InlineData(false)] // chunk unload
  public void Teardown_frees_the_handle_so_the_machine_can_tick_again(
    bool removed
  ) {
    var (world, machine) = Placed();
    world.Initialize(machine);
    world.FireBlockEntityTicks();

    if (removed)
      machine.OnBlockRemoved();
    else
      machine.OnBlockUnloaded();

    world.FireBlockEntityTicks();
    Assert.Equal(1, machine.ProductionTicks);

    machine.StartTicking();
    world.FireBlockEntityTicks();
    Assert.Equal(2, machine.ProductionTicks);
  }

  // The process must leave the same result from either end of a host's own teardown.
  [Theory]
  [InlineData(true)] // base first: the host's own teardown runs with its listeners already gone
  [InlineData(false)] // base last: they are still live, which is the window a fire-out needs
  public void A_hosted_process_frees_its_handle_from_either_end_of_a_teardown(
    bool baseFirst
  ) {
    var world = new TestWorld();
    var machine = new OrderedTeardownMachine { BaseFirst = baseFirst };
    world.Place(
      new BlockPos(0, 0, 0),
      TestBlocks.Configure(new Block(), MachineCode, 97),
      machine
    );
    world.Initialize(machine);
    world.FireBlockEntityTicks();

    Assert.Equal(1, machine.ProductionTicks);

    int atOwnTeardown = -1;
    machine.DuringTeardown = () => {
      world.FireBlockEntityTicks();
      atOwnTeardown = machine.ProductionTicks;
    };
    machine.OnBlockRemoved();

    Assert.Equal(baseFirst ? 1 : 2, atOwnTeardown);

    world.FireBlockEntityTicks();
    Assert.Equal(atOwnTeardown, machine.ProductionTicks);

    machine.StartTicking();
    world.FireBlockEntityTicks();
    Assert.Equal(atOwnTeardown + 1, machine.ProductionTicks);
  }

  [Fact]
  public void A_machine_ticks_without_ever_being_initialized() {
    var (world, machine) = Placed();
    world.Attach(machine);

    // The process reads the block entity's api, not its own.
    Assert.Null(ProcessOf(machine).Api);
    machine.StartTicking();
    world.FireBlockEntityTicks();

    Assert.Equal(1, machine.ProductionTicks);
  }

  #endregion

  #region Persistence

  [Fact]
  public void The_machine_is_the_only_writer_of_the_saved_stamp() {
    var (world, machine) = Placed();
    world.Initialize(machine);
    world.AdvanceHours(3);
    world.FireBlockEntityTicks();

    // The process holds the stamp and writes none of it.
    var ofProcess = new TreeAttribute();
    ProcessOf(machine).ToTreeAttributes(ofProcess);
    Assert.False(ofProcess.HasAttribute("pm_lastHours"));

    var ofMachine = new TreeAttribute();
    machine.ToTreeAttributes(ofMachine);
    Assert.Equal(3.0, ofMachine.GetDouble("pm_lastHours"), 3);
  }

  [Fact]
  public void A_restored_stamp_reaches_the_process() {
    var (world, machine) = Placed();
    var tree = new TreeAttribute();
    tree.SetDouble("pm_lastHours", 7.0);

    machine.FromTreeAttributes(tree, world.World);

    Assert.Equal(7.0, ProcessOf(machine).LastTickHours, 3);
  }

  #endregion
}
