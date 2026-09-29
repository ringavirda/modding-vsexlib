using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
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
    Block core = TestBlocks.Configure(new Block(), "exlib:testmega-n", 1);
    // Breaking runs the block's own hooks: they reach the entity through EntityClass and spawn
    // particles through the block's api.
    core.EntityClass = "TestMegablock";
    ReflectionHelpers.SetField(core, "api", world.Api);
    world.Place(anchor, core, machine);
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

  // Fails when OnBlockUnloaded unmarks.
  [Fact]
  public void Unloading_the_core_keeps_its_marks() {
    var anchor = new BlockPos(3000, 10, 0);
    var (world, _) = Formed(anchor);
    BlockPos floor = anchor.AddCopy(2, 0, 0);

    world.Unload(anchor);

    Assert.True(NoSnowCells.IsMarked(floor));
    NoSnowCells.Unmark(anchor);
  }

  // Fails when Initialize and SetStructureAngle mark nothing.
  [Fact]
  public void A_reloaded_core_marks_its_floor_again() {
    var anchor = new BlockPos(3100, 10, 0);
    var (world, _) = Formed(anchor);
    BlockPos floor = anchor.AddCopy(2, 0, 0);
    NoSnowCells.Unmark(anchor);

    world.Reload(anchor);

    Assert.True(NoSnowCells.IsMarked(floor));
    world.Accessor.BreakBlock(anchor, null);
    Assert.False(NoSnowCells.IsMarked(floor));
  }

  // Fails when OnBlockRemoved unmarks on the client too.
  [Fact]
  public void A_client_side_core_removed_leaves_the_servers_marks() {
    var anchor = new BlockPos(3200, 10, 0);
    var (world, machine) = Formed(anchor);
    machine.Api = world.ClientApi;

    machine.OnBlockRemoved();

    Assert.True(NoSnowCells.IsMarked(anchor.AddCopy(2, 0, 0)));
    NoSnowCells.Unmark(anchor);
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

  // Fails when SetStructureAngle asks CellsWithRole before the block entity has a position.
  [Fact]
  public void A_structure_turned_before_it_has_a_position_marks_nothing() {
    var world = new TestWorld();
    var machine = new TestMegablock {
      Block = TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
    };
    machine.Block.Attributes = new JsonObject(
      MultiblockCellRolesFixtures.Attributes(FloorDef())
    );
    world.Attach(machine);

    StructureTestHooks.ApplyStructureRotation(machine);

    machine.Pos = new BlockPos(5500, 10, 0);
    Assert.Single(machine.CellsWithRole(CellRoles.NoSnow));
    Assert.False(NoSnowCells.IsMarked(machine.Pos.AddCopy(2, 0, 0)));
  }

  // Fails when releasing one owner removes a cell another owner still marks.
  [Fact]
  public void A_cell_two_owners_mark_stays_marked_until_both_let_go() {
    var cell = new BlockPos(6000, 10, 0);
    var first = new BlockPos(6001, 10, 0);
    var second = new BlockPos(6002, 10, 0);
    NoSnowCells.Mark(first, [cell]);
    NoSnowCells.Mark(second, [cell.Copy()]);

    NoSnowCells.Unmark(first);
    Assert.True(NoSnowCells.IsMarked(cell));

    NoSnowCells.Unmark(second);
    Assert.False(NoSnowCells.IsMarked(cell));
  }

  // Fails when a mark at an owner's position adds to the cells an earlier mark there left.
  [Fact]
  public void A_second_mark_at_one_position_replaces_the_first() {
    var owner = new BlockPos(6100, 10, 0);
    var old = new BlockPos(6101, 10, 0);
    var now = new BlockPos(6102, 10, 0);
    NoSnowCells.Mark(owner, [old]);

    NoSnowCells.Mark(owner.Copy(), [now]);

    Assert.False(NoSnowCells.IsMarked(old));
    Assert.True(NoSnowCells.IsMarked(now));
    NoSnowCells.Unmark(owner);
  }

  // Fails when Mark or Unmark stops checking its owner for null.
  [Fact]
  public void A_null_owner_is_refused() {
    Assert.Throws<System.ArgumentNullException>(() =>
      NoSnowCells.Mark(null!, [])
    );
    Assert.Throws<System.ArgumentNullException>(() =>
      NoSnowCells.Unmark(null!)
    );
  }

  // Fails when the key drops the dimension (Y instead of InternalY).
  [Fact]
  public void A_mark_holds_in_its_own_dimension_only() {
    var owner = new BlockPos(7001, 10, 0, 1);
    NoSnowCells.Mark(owner, [new BlockPos(7000, 10, 0, 1)]);
    try {
      Assert.True(NoSnowCells.IsMarked(new BlockPos(7000, 10, 0, 1)));
      Assert.False(NoSnowCells.IsMarked(new BlockPos(7000, 10, 0, 0)));
    } finally {
      NoSnowCells.Unmark(owner);
    }
  }
}
