using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A concrete mega-block for driving the multiblock lifecycle headlessly.</summary>
internal sealed class TestMegablock : BlockEntityMultiblockMachine {
  /// <summary>The rotation the machine reports, standing in for reading a <c>side</c> variant.</summary>
  public int Angle;
  public int ProductionTicks;
  public int CompletedCount;
  public int LostCount;

  protected override void UpdateStructureRotation() => SetStructureAngle(Angle);

  protected override void OnProductionTick(float dt) => ProductionTicks++;

  protected override void OnStructureCompleted() => CompletedCount++;

  protected override void OnStructureLost() => LostCount++;

  protected override string GetIncompleteMessage(int missingCount) =>
    $"missing {missingCount}";

  protected override string GetCompleteMessage() => "complete";
}

/// <summary>Covers <see cref="StructureRig"/>, the harness primitive that stands up a machine's
/// authored footprint; the machine completes itself, with no direct assignment to
/// <c>StructureComplete</c>.</summary>
public class StructureRigTests {
  #region Nested alternations

  /// <summary>Pins a layout glyph with a nested alternation inside <c>@( )</c>.</summary>
  [Fact]
  public void A_glyph_with_a_nested_alternation_is_still_satisfied() {
    var world = new TestWorld();
    var machine = new TestMegablock { Angle = 0 };
    world.Place(
      new BlockPos(0, 10, 0),
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      machine
    );
    world.Attach(machine);

    ExBlockDef def = ExBlockDef
      .Create("exlib", "testnested")
      .Multiblock(m =>
        m.Number("exlib:testmega*", 1)
          .Number("@(brickcourse-.*-(black|red|tan)|claybricks-good-fire)", 2)
          .At(0, 0, 0, 1)
          .At(1, 0, 0, 2)
      );

    var rig = StructureRig.Around(world, machine, def);
    rig.Raise();

    Assert.Equal(0, rig.Missing);
  }

  #endregion

  #region Fixture

  // A principal, a ring of solid cells, and a shaft cell satisfied by air.
  private static ExBlockDef Def() =>
    ExBlockDef
      .Create("exlib", "testmega")
      .Multiblock(m =>
        m.Number("exlib:testmega*", 1)
          .Number("exlib:testbrick*", 2)
          .Number("@(air|coalpile)", 3)
          .At(0, 0, 0, 1)
          .At(1, 0, 0, 2)
          .At(-1, 0, 0, 2)
          .At(0, 0, 1, 2)
          .At(0, 0, -1, 2)
          .At(0, 1, 0, 3)
      );

  /// <summary>A footprint with one oriented part in it, authored through the layout
  /// builder.</summary>
  private static ExBlockDef OrientedDef() =>
    ExBlockDef
      .Create("exlib", "testmega")
      .MultiblockLayout(s =>
        s.Origin(0, 0)
          .Legend('C', "exlib:testmega*")
          .Legend('D', "exlib:testdoor-n")
          .Layer(
            0,
            """
            C D
            """
          )
      );

  private static (TestWorld world, TestMegablock machine) Stand(int angle = 0) {
    var world = new TestWorld();
    var machine = new TestMegablock { Angle = angle };
    world.Place(
      new BlockPos(0, 10, 0),
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      machine
    );
    world.Attach(machine);
    return (world, machine);
  }

  #endregion

  #region Raising the footprint

  [Fact]
  public void Raise_satisfies_every_cell_of_the_layout() {
    var (world, machine) = Stand();
    var rig = StructureRig.Around(world, machine, Def());

    // Four brick cells; the anchor and the air-satisfied shaft cell are not counted as missing.
    Assert.Equal(4, rig.Missing);
    rig.Raise();
    Assert.Equal(0, rig.Missing);
  }

  [Fact]
  public void An_air_satisfied_cell_is_left_empty_rather_than_filled() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, Def()).Raise();

    // The shaft wants "@(air|coalpile)"; it stays open, not filled.
    Assert.Equal(
      "game:air",
      world.GetBlock(new BlockPos(0, 11, 0)).Code.ToString()
    );
  }

  [Fact]
  public void A_cell_the_test_already_occupied_is_left_alone() {
    var (world, machine) = Stand();
    var rig = StructureRig.Around(world, machine, Def());

    // A functional block already standing in one of the footprint cells.
    var functional = TestBlocks.Configure(
      new Block(),
      "exlib:testbrick-real",
      77
    );
    rig.Occupy(new BlockPos(1, 10, 0), functional);
    rig.Raise();

    Assert.Same(functional, world.GetBlock(new BlockPos(1, 10, 0)));
    Assert.Equal(0, rig.Missing);
  }

  [Fact]
  public void Raise_is_idempotent() {
    var (world, machine) = Stand();
    var rig = StructureRig.Around(world, machine, Def()).Raise();
    Block first = world.GetBlock(new BlockPos(1, 10, 0));

    rig.Raise();

    Assert.Same(first, world.GetBlock(new BlockPos(1, 10, 0)));
    Assert.Equal(0, rig.Missing);
  }

  #endregion

  #region Completion is an outcome, not an assignment

  [Fact]
  public void The_machine_completes_itself_once_the_footprint_stands() {
    var (world, machine) = Stand();
    var rig = StructureRig.Around(world, machine, Def()).Raise();
    Assert.False(machine.StructureComplete);

    world.Initialize(machine);
    Assert.True(rig.AwaitCompletion());

    Assert.True(machine.StructureComplete);
    Assert.Equal(1, machine.CompletedCount);
  }

  [Fact]
  public void An_unbuilt_footprint_never_completes() {
    var (world, machine) = Stand();
    var rig = StructureRig.Around(world, machine, Def()); // deliberately not raised

    world.Initialize(machine);

    Assert.False(rig.AwaitCompletion());
    Assert.False(machine.StructureComplete);
    Assert.Equal(0, machine.CompletedCount);
  }

  [Fact]
  public void Breaking_one_cell_takes_the_machine_back_out_of_completion() {
    var (world, machine) = Stand();
    var rig = StructureRig.Around(world, machine, Def()).Complete();

    world.Accessor.SetBlock(0, new BlockPos(0, 10, -1));
    Assert.Equal(1, rig.Missing);
    world.AdvanceBlockEntityTime(3000);

    Assert.False(machine.StructureComplete);
    Assert.Equal(1, machine.LostCount);
  }

  [Fact]
  public void Production_does_not_run_until_the_structure_is_complete() {
    var (world, machine) = Stand();
    var rig = StructureRig.Around(world, machine, Def());

    // Live, initialized, ticking - but unbuilt.
    world.Initialize(machine);
    world.AdvanceBlockEntityTime(6000);
    Assert.Equal(0, machine.ProductionTicks);

    rig.Raise();
    Assert.True(rig.AwaitCompletion());
    world.AdvanceBlockEntityTime(3000);

    Assert.True(machine.ProductionTicks > 0);
  }

  #endregion

  #region Rotation

  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void A_structure_raised_at_any_angle_completes(int angle) {
    var (world, machine) = Stand(angle);

    // Throws with a per-cell breakdown if the rotated cells land where the machine cannot see them.
    StructureRig.Around(world, machine, Def(), angle).Complete();

    Assert.True(machine.StructureComplete);
  }

  [Fact]
  public void Cell_maps_a_local_offset_through_the_rigs_rotation() {
    var (world, machine) = Stand(90);
    var rig = StructureRig.Around(world, machine, Def(), 90);

    // The rig's cell table is vanilla's rotated layout.
    BlockPos mapped = rig.Cell(1, 0, 0);
    Assert.Contains(rig.Cells, c => c.Pos.Equals(mapped));
    Assert.NotEqual(new BlockPos(1, 10, 0), mapped); // it genuinely moved
  }

  #endregion

  #region Oriented parts

  // The rig's half of MultiblockFacings.

  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void A_structure_with_an_oriented_part_completes_at_any_angle(
    int angle
  ) {
    var (world, machine) = Stand(angle);

    // Throws with a per-cell breakdown if the rig satisfied its own idea of the cell but not the machine's.
    StructureRig.Around(world, machine, OrientedDef(), angle).Complete();

    Assert.True(machine.StructureComplete);
  }

  [Fact]
  public void A_rotated_oriented_cell_wants_the_rotated_facing_not_the_authored_one() {
    var (world, machine) = Stand(90);
    var rig = StructureRig.Around(world, machine, OrientedDef(), 90);

    // The layout authors a north-facing door; at 90 degrees the cell wants a west-facing one.
    rig.Occupy(
      rig.Cell(1, 0, 0),
      TestBlocks.Configure(new Block(), "exlib:testdoor-n", 91)
    );
    rig.Raise();

    Assert.Equal(1, rig.Missing);
    // MultiblockFacings rotates in the token's own spelling; a quarter turn asks for `-w`.
    Assert.Contains("wants 'exlib:testdoor-w'", rig.MissingReport);
    Assert.Contains("has 'exlib:testdoor-n'", rig.MissingReport);
  }

  [Fact]
  public void A_layout_with_no_oriented_part_keeps_its_authored_glyphs() {
    var (world, machine) = Stand(90);
    var rig = StructureRig.Around(world, machine, Def(), 90);

    // A shaft glyph is domainless; the rig reads the authored form directly, not an AssetLocation round trip.
    Assert.Contains(rig.Cells, c => c.Wanted == "@(air|coalpile)");
    Assert.Contains(rig.Cells, c => c.Wanted == "exlib:testbrick*");
  }

  #endregion

  #region Rotation, continued

  [Fact]
  public void A_structure_raised_at_the_wrong_angle_does_not_complete() {
    // The machine faces north; the rig lays the footprint a quarter-turn out.
    var world = new TestWorld();
    var machine = new TestMegablock { Angle = 0 };
    world.Place(
      new BlockPos(0, 10, 0),
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      machine
    );
    world.Attach(machine);

    ExBlockDef asymmetric = ExBlockDef
      .Create("exlib", "testmega")
      .Multiblock(m =>
        m.Number("exlib:testmega*", 1)
          .Number("exlib:testbrick*", 2)
          .At(0, 0, 0, 1)
          .At(2, 0, 0, 2)
      );

    StructureRig.Around(world, machine, asymmetric, 90).Raise();
    world.Initialize(machine);

    Assert.False(machine.StructureComplete);
  }

  #endregion

  #region Test seams (DriveMonitorTick, ApplyStructureRotation)

  [Fact]
  public void DriveMonitorTick_completes_the_structure_without_a_registered_listener() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, Def()).Raise();
    world.Initialize(machine);
    Assert.False(machine.StructureComplete);

    machine.DriveMonitorTick();

    Assert.True(machine.StructureComplete);
    Assert.Equal(1, machine.CompletedCount);
  }

  [Fact]
  public void ApplyStructureRotation_loads_the_structure_DriveMonitorTick_then_sees() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, Def()).Raise(); // deliberately not Initialize()d

    machine.ApplyStructureRotation();
    machine.DriveMonitorTick();

    Assert.True(machine.StructureComplete);
  }

  #endregion

  #region Guards

  [Fact]
  public void A_definition_without_a_multiblock_layout_is_rejected() {
    var (world, machine) = Stand();
    var ex = Assert.Throws<System.InvalidOperationException>(() =>
      StructureRig.Around(world, machine, ExBlockDef.Create("exlib", "plain"))
    );
    Assert.Contains("multiblockStructure", ex.Message);
  }

  [Fact]
  public void Complete_reports_which_cells_are_unsatisfied() {
    var (world, machine) = Stand();
    var rig = StructureRig.Around(world, machine, Def());

    // Occupies one cell with the wrong block.
    rig.Occupy(
      new BlockPos(1, 10, 0),
      TestBlocks.Configure(new Block(), "exlib:wrongblock", 88)
    );

    var ex = Assert.Throws<System.InvalidOperationException>(() =>
      rig.Complete()
    );
    Assert.Contains("exlib:testbrick*", ex.Message);
    Assert.Contains("exlib:wrongblock", ex.Message);
  }

  #endregion
}
