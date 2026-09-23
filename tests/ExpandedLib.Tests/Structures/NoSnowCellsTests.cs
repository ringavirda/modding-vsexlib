using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="NoSnowCells"/> as a multiblock structure fills it from its
/// <see cref="CellRoles.NoSnow"/> cells, and the registry's own bookkeeping. Each test stands at its
/// own anchor: the registry is process-wide.</summary>
public class NoSnowCellsTests {
  /// <summary>The floor <c>f</c> under the open cell <c>a</c>, beside a plain wall <c>#</c> of the same code.</summary>
  internal static ExBlockDef FloorDef() =>
    ExBlockDef
      .Create("exlib", "testmega")
      .MultiblockLayout(s =>
        s.Origin(0, 0)
          .Legend('C', "exlib:testmega*")
          .Legend('#', "exlib:testbrick*")
          .Legend('f', "exlib:testbrick*")
          .Legend('a', "game:air")
          .Role('f', CellRoles.NoSnow)
          .Layer(0, "C # f")
          .Layer(1, ". . a")
      );

  /// <summary>A formed structure at <paramref name="anchor"/> built from <see cref="FloorDef"/>.</summary>
  internal static (TestWorld World, TestMegablock Machine) Formed(
    BlockPos anchor,
    int angle = 0
  ) {
    var world = new TestWorld();
    var machine = new TestMegablock { Angle = angle };
    world.Place(
      anchor,
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      machine
    );
    world.Attach(machine);
    StructureRig.Around(world, machine, FloorDef(), angle).Complete();
    return (world, machine);
  }

  private static BlockPos At(BlockPos anchor, int x, int y, int z, int angle) {
    Vec3i r = ExOrientation.RotateOffset(new Vec3i(x, y, z), angle);
    return anchor.AddCopy(r.X, r.Y, r.Z);
  }

  // Fails when SetStructureAngle never calls NoSnowCells.Mark.
  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void A_formed_structure_marks_the_floor_cell_and_neither_its_air_nor_its_wall(
    int angle
  ) {
    var anchor = new BlockPos(1000 + angle, 10, 0);
    Formed(anchor, angle);

    Assert.True(NoSnowCells.IsMarked(At(anchor, 2, 0, 0, angle)));
    Assert.False(NoSnowCells.IsMarked(At(anchor, 2, 1, 0, angle)));
    Assert.False(NoSnowCells.IsMarked(At(anchor, 1, 0, 0, angle)));
  }

  // Fails when OnBlockRemoved does not unmark.
  [Fact]
  public void Breaking_the_core_drops_its_marks() {
    var anchor = new BlockPos(2000, 10, 0);
    var (world, _) = Formed(anchor);
    BlockPos floor = anchor.AddCopy(2, 0, 0);
    Assert.True(NoSnowCells.IsMarked(floor));

    world.Accessor.BreakBlock(anchor, null);

    Assert.False(NoSnowCells.IsMarked(floor));
  }

  // Fails when OnBlockUnloaded does not unmark.
  [Fact]
  public void Unloading_the_core_drops_its_marks() {
    var anchor = new BlockPos(3000, 10, 0);
    var (world, _) = Formed(anchor);
    BlockPos floor = anchor.AddCopy(2, 0, 0);
    Assert.True(NoSnowCells.IsMarked(floor));

    world.Unload(anchor);

    Assert.False(NoSnowCells.IsMarked(floor));
  }

  // Fails when Mark adds to an owner's cells instead of replacing them.
  [Fact]
  public void Turning_the_structure_moves_its_marks() {
    var anchor = new BlockPos(4000, 10, 0);
    var (world, machine) = Formed(anchor);

    machine.Angle = 90;
    world.AdvanceBlockEntityTime(3000);

    Assert.False(NoSnowCells.IsMarked(anchor.AddCopy(2, 0, 0)));
    Assert.True(NoSnowCells.IsMarked(At(anchor, 2, 0, 0, 90)));
  }

  // Fails when SetStructureAngle marks on the client too.
  [Fact]
  public void A_client_side_structure_marks_nothing() {
    var anchor = new BlockPos(5000, 10, 0);
    var world = new TestWorld();
    var machine = new TestMegablock();
    world.Place(
      anchor,
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      machine
    );
    StructureRig.Around(world, machine, FloorDef());
    machine.Api = world.ClientApi;

    Assert.Single(machine.CellsWithRole(CellRoles.NoSnow));
    Assert.False(NoSnowCells.IsMarked(anchor.AddCopy(2, 0, 0)));
  }

  // Fails when releasing one owner removes a cell another owner still marks.
  [Fact]
  public void A_cell_two_owners_mark_stays_marked_until_both_let_go() {
    var cell = new BlockPos(6000, 10, 0);
    object first = new();
    object second = new();
    NoSnowCells.Mark(first, [cell]);
    NoSnowCells.Mark(second, [cell.Copy()]);

    NoSnowCells.Unmark(first);
    Assert.True(NoSnowCells.IsMarked(cell));

    NoSnowCells.Unmark(second);
    Assert.False(NoSnowCells.IsMarked(cell));
  }

  // Fails when the key drops the dimension (Y instead of InternalY).
  [Fact]
  public void A_mark_holds_in_its_own_dimension_only() {
    object owner = new();
    NoSnowCells.Mark(owner, [new BlockPos(7000, 10, 0, 1)]);
    try {
      Assert.True(NoSnowCells.IsMarked(new BlockPos(7000, 10, 0, 1)));
      Assert.False(NoSnowCells.IsMarked(new BlockPos(7000, 10, 0, 0)));
    } finally {
      NoSnowCells.Unmark(owner);
    }
  }
}
