using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.FastForwarding;
using ExpandedLib.Machines;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>An Off multiblock machine with a configurable step and cap, recording each call.</summary>
internal sealed class HostedMegablock : BlockEntityMultiblockMachine {
  public double StepHours;
  public double CapHours = 24;
  public readonly List<(float Dt, double Hours)> Calls = [];

  protected override FastForwardMode FastForwardMode => FastForwardMode.Off;

  protected override double GameStepHours => StepHours;

  protected override double MaxGameHoursPerTick => CapHours;

  protected override void UpdateStructureRotation() => SetStructureAngle(0);

  protected override void OnProductionTick(float dt) =>
    Calls.Add((dt, TickGameHours));

  protected override string GetIncompleteMessage(int missingCount) =>
    $"missing {missingCount}";

  protected override string GetCompleteMessage() => "complete";
}

/// <summary>The one production host both machine base classes hand their answers through.</summary>
public class ProductionHostTests {
  private static (TestWorld world, CatchupMachine machine) Production(
    double stepHours
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

  private static (TestWorld world, HostedMegablock machine) Multiblock(
    double stepHours,
    double capHours
  ) {
    var world = new TestWorld();
    var m = new HostedMegablock { StepHours = stepHours, CapHours = capHours };
    world.Place(
      new BlockPos(0, 10, 0),
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      m
    );
    world.Attach(m);
    ExBlockDef def = ExBlockDef
      .Create("exlib", "testmega")
      .Multiblock(b =>
        b.Number("exlib:testmega*", 1)
          .Number("exlib:testbrick*", 2)
          .At(0, 0, 0, 1)
      );
    StructureRig.Around(world, m, def).Complete();
    m.DriveProductionTick(1f);
    m.Calls.Clear();
    return (world, m);
  }

  [Fact]
  public void A_production_machine_hands_its_step_and_cap_to_the_host() {
    var (world, m) = Production(0.25);

    world.AdvanceHours(100);
    m.Drive(1f);

    Assert.Equal(96, m.Calls.Count);
    Assert.Equal(24.0, m.Calls.Sum(c => c.Hours), 4);
  }

  [Fact]
  public void A_multiblock_machine_hands_its_step_and_cap_to_the_host() {
    var (world, m) = Multiblock(0.25, 2);

    world.AdvanceHours(100);
    m.DriveProductionTick(1f);

    Assert.Equal(8, m.Calls.Count);
    Assert.Equal(2.0, m.Calls.Sum(c => c.Hours), 4);
  }

  [Fact]
  public void Both_owners_give_the_real_dt_to_the_first_step_only() {
    var (pw, p) = Production(0.5);
    var (mw, m) = Multiblock(0.5, 24);

    pw.AdvanceHours(1);
    p.Drive(1f);
    mw.AdvanceHours(1);
    m.DriveProductionTick(1f);

    Assert.Equal([1f, 0f], p.Calls.Select(c => c.Dt));
    Assert.Equal([1f, 0f], m.Calls.Select(c => c.Dt));
  }
}
