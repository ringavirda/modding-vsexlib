using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Machines;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A multiblock that is form alone: it completes and publishes readiness but runs no
/// production process.</summary>
internal sealed class TestFormMegablock : BlockEntityMultiblockStructure {
  public int CompletedCount;
  public int LostCount;

  protected override void UpdateStructureRotation() => SetStructureAngle(0);

  protected override void OnStructureCompleted() => CompletedCount++;

  protected override void OnStructureLost() => LostCount++;

  protected override string GetIncompleteMessage(int missingCount) =>
    $"missing {missingCount}";

  protected override string GetCompleteMessage() => "complete";
}

/// <summary>Form and process are separate axes on a multiblock: a multiblock that does not derive from
/// <see cref="BlockEntityMultiblockMachine"/> carries no tick while remaining a full member of every
/// completion transition.</summary>
public class MultiblockProcessTests {
  private static ExBlockDef Def() =>
    ExBlockDef
      .Create("exlib", "testmega")
      .Multiblock(m =>
        m.Number("exlib:testmega*", 1)
          .Number("exlib:testbrick*", 2)
          .At(0, 0, 0, 1)
          .At(1, 0, 0, 2)
          .At(-1, 0, 0, 2)
          .At(0, 0, 1, 2)
          .At(0, 0, -1, 2)
      );

  private static TestWorld Complete(BlockEntityMultiblockStructure machine) {
    var world = new TestWorld();
    world.Place(
      new BlockPos(0, 10, 0),
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      machine
    );
    world.Attach(machine);
    StructureRig.Around(world, machine, Def()).Complete();
    return world;
  }

  /// <summary>Breaks one cell out of the footprint and lets the monitor tick notice.</summary>
  private static void Breach(TestWorld world) {
    world.Accessor.SetBlock(0, new BlockPos(0, 10, -1));
    world.AdvanceBlockEntityTime(3000);
  }

  #region Taking the process on, or not

  [Fact]
  public void A_multiblock_that_takes_on_no_process_carries_no_tick() {
    var machine = new TestFormMegablock();
    Complete(machine);

    Assert.True(machine.StructureComplete);
    Assert.Equal(1, machine.CompletedCount);

    Assert.Empty(ProductionProcess.ProcessesOn(machine));
    Assert.Empty(machine.Behaviors.OfType<BEBehaviorProductionMachine>());
  }

  [Fact]
  public void A_multiblock_that_takes_the_process_on_ticks_once_complete() {
    var machine = new TestMegablock { Angle = 0 };
    TestWorld world = Complete(machine);

    Assert.Single(ProductionProcess.ProcessesOn(machine));

    machine.ProductionTicks = 0;
    world.AdvanceBlockEntityTime(1000);
    Assert.True(machine.ProductionTicks > 0);
  }

  [Fact]
  public void A_multiblock_that_loads_already_complete_ticks_without_a_transition() {
    var machine = new TestMegablock { Angle = 0 };
    TestWorld world = Complete(machine);

    var tree = new TreeAttribute();
    machine.ToTreeAttributes(tree);
    machine.OnBlockUnloaded();

    Assert.True(tree.GetBool("structureComplete"));

    var reloaded = new TestMegablock { Angle = 0 };
    world.Place(new BlockPos(0, 10, 0), machine.Block, reloaded);
    reloaded.FromTreeAttributes(tree, world.World);
    world.Initialize(reloaded);
    Assert.Equal(0, reloaded.ProductionTicks);

    world.FireBlockEntityTicks();

    Assert.True(reloaded.ProductionTicks > 0);
    Assert.Equal(0, reloaded.CompletedCount);
  }

  [Fact]
  public void A_multiblock_saves_the_stamp_its_process_catches_up_from() {
    var machine = new TestMegablock { Angle = 0 };
    TestWorld world = Complete(machine);
    world.AdvanceHours(3);
    world.AdvanceBlockEntityTime(1000);

    Assert.True(machine.ProductionTicks > 0);

    var tree = new TreeAttribute();
    machine.ToTreeAttributes(tree);
    Assert.Equal(3.0, tree.GetDouble("pm_lastHours"), 3);

    var reloaded = new TestMegablock { Angle = 0 };
    reloaded.FromTreeAttributes(tree, world.World);
    Assert.Equal(
      3.0,
      ProductionProcess.ProcessesOn(reloaded).Single().LastTickHours,
      3
    );
  }

  #endregion

  #region The form still drives every transition

  [Fact]
  public void A_form_only_multiblock_publishes_its_pattern_as_readiness() {
    var machine = new TestFormMegablock();
    TestWorld world = Complete(machine);

    Assert.Single(ProductionReadiness.PublishersOn(machine));
    Assert.True(ProductionReadiness.IsReady(machine));

    Breach(world);
    Assert.False(ProductionReadiness.IsReady(machine));
  }

  [Fact]
  public void Losing_the_pattern_leaves_a_form_only_multiblock_intact() {
    var machine = new TestFormMegablock();
    TestWorld world = Complete(machine);

    Assert.True(ProductionReadiness.StopsProductionWhenNotReady(machine));
    Breach(world);

    Assert.False(machine.StructureComplete);
    Assert.Equal(1, machine.LostCount);
  }

  [Fact]
  public void Teardown_of_a_form_only_multiblock_stops_a_process_it_never_had() {
    var machine = new TestFormMegablock();
    Complete(machine);

    machine.OnBlockRemoved();
    machine.OnBlockUnloaded();

    Assert.Empty(ProductionProcess.ProcessesOn(machine));
  }

  #endregion
}
