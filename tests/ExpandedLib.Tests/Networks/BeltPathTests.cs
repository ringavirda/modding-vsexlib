using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The path of an open belt: the refusal codes in their order and the strap's collision with the blocks in the
/// cells it crosses (<see cref="BeltPath"/>).
/// </summary>
public class BeltPathTests {
  private static readonly Block Slab = TestBlocks.Configure(
    new Block { CollisionBoxes = [new Cuboidf(0f, 0f, 0.55f, 1f, 1f, 1f)] },
    "test:slab",
    71
  );

  private static readonly Block FarSlab = TestBlocks.Configure(
    new Block { CollisionBoxes = [new Cuboidf(0f, 0f, 0.7f, 1f, 1f, 1f)] },
    "test:farslab",
    72
  );

  private static readonly Block Grass = TestBlocks.Configure(
    new Block { CollisionBoxes = null },
    "test:grass",
    73
  );

  private static readonly Block Bare = TestBlocks.Configure(
    new Block { CollisionBoxes = [] },
    "test:bare",
    74
  );

  private static readonly Block Full = TestBlocks.Configure(
    new Block { CollisionBoxes = [new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f)] },
    "test:full",
    75
  );

  private static BeltPulley Pulley(
    int x,
    int y,
    int z,
    EnumAxis axis = EnumAxis.Z,
    int size = 1
  ) => new(new BlockPos(x, y, z, 0), axis, size);

  private static string? Check(
    TestWorld world,
    BeltPulley a,
    BeltPulley b,
    params BlockPos[] claimed
  ) => BeltPath.Check(world.Accessor, a, b, claimed);

  // The strap of this pair crosses cell (2, 1, 0) along its upper side.
  private static BeltPulley SlantA => Pulley(0, 0, 0);
  private static BeltPulley SlantB => Pulley(4, 1, 0);
  private static readonly BlockPos Crossed = new(2, 1, 0, 0);

  [Fact]
  public void Reach_5_accepted_5_1_refused() {
    var world = new TestWorld();

    Assert.Null(Check(world, Pulley(0, 0, 0), Pulley(5, 0, 0)));
    Assert.Equal(
      BeltPath.Reach,
      Check(world, Pulley(0, 0, 0), Pulley(1, 5, 0))
    );
  }

  [Fact]
  public void Diagonal_3_4_is_5() {
    var world = new TestWorld();

    Assert.Null(Check(world, Pulley(0, 0, 0), Pulley(3, 4, 0)));
  }

  [Fact]
  public void Off_plane_refused() {
    var world = new TestWorld();

    Assert.Equal(
      BeltPath.Plane,
      Check(world, Pulley(0, 0, 0), Pulley(3, 0, 1))
    );
  }

  [Fact]
  public void Axes_that_differ_are_refused() {
    var world = new TestWorld();

    Assert.Equal(
      BeltPath.NotParallel,
      Check(world, Pulley(0, 0, 0), Pulley(3, 0, 0, EnumAxis.X))
    );
  }

  [Fact]
  public void Rims_touching_refused() {
    var world = new TestWorld();

    Assert.Equal(
      BeltPath.Overlap,
      Check(world, Pulley(0, 0, 0), Pulley(1, 0, 0))
    );
    Assert.Equal(
      BeltPath.Overlap,
      Check(
        world,
        Pulley(0, 0, 0, size: 2),
        Pulley(2, 0, 0, size: 2)
      )
    );
    Assert.Null(
      Check(
        world,
        Pulley(0, 0, 0, size: 2),
        Pulley(3, 0, 0, size: 2)
      )
    );
  }

  [Fact]
  public void Strap_edge_blocked() {
    var world = new TestWorld();
    world.Place(Crossed, Slab);

    Assert.Equal(BeltPath.Blocked, Check(world, SlantA, SlantB));

    world.Place(Crossed, FarSlab);

    Assert.Null(Check(world, SlantA, SlantB));
  }

  [Fact]
  public void Straight_size_1_belt_claims_its_row() {
    var world = new TestWorld();
    BeltPulley a = Pulley(0, 0, 0);
    BeltPulley b = Pulley(4, 0, 0);

    world.Place(new BlockPos(2, 0, 0, 0), Full);
    Assert.Equal(BeltPath.Blocked, Check(world, a, b));

    world = new TestWorld();
    world.Place(new BlockPos(2, 1, 0, 0), Full);
    world.Place(new BlockPos(2, -1, 0, 0), Full);
    Assert.Null(Check(world, a, b));
  }

  [Fact]
  public void Size_2_belt_claims_the_rows_it_crosses() {
    var world = new TestWorld();
    BeltPulley a = Pulley(0, 0, 0, size: 2);
    BeltPulley b = Pulley(5, 0, 0, size: 2);

    foreach (int row in new[] { 1, -1 }) {
      world = new TestWorld();
      world.Place(new BlockPos(2, row, 0, 0), Full);
      Assert.Equal(BeltPath.Blocked, Check(world, a, b));
    }

    world = new TestWorld();
    world.Place(new BlockPos(2, 2, 0, 0), Full);
    world.Place(new BlockPos(2, -2, 0, 0), Full);
    Assert.Null(Check(world, a, b));
  }

  [Fact]
  public void Grass_does_not_block() {
    var world = new TestWorld();

    world.Place(Crossed, Grass);
    Assert.Null(Check(world, SlantA, SlantB));

    world.Place(Crossed, Bare);
    Assert.Null(Check(world, SlantA, SlantB));
  }

  [Fact]
  public void A_claimed_cell_does_not_block() {
    var world = new TestWorld();
    world.Place(Crossed, Slab);

    Assert.Null(Check(world, SlantA, SlantB, Crossed));
  }

  [Fact]
  public void The_pulley_cells_do_not_block() {
    var world = new TestWorld();
    world.Place(SlantA.Cell, Slab);
    world.Place(SlantB.Cell, Slab);

    Assert.Null(Check(world, SlantA, SlantB));
  }

  [Fact]
  public void Refusal_order() {
    var world = new TestWorld();
    world.Place(new BlockPos(1, 1, 0, 0), Slab);
    world.Place(new BlockPos(1, 3, 0, 0), Slab);

    Assert.Equal(
      BeltPath.NotParallel,
      Check(world, Pulley(0, 0, 0), Pulley(3, 0, 1, EnumAxis.X))
    );
    Assert.Equal(
      BeltPath.Plane,
      Check(world, Pulley(0, 0, 0), Pulley(9, 0, 1))
    );
    Assert.Equal(
      BeltPath.Reach,
      Check(world, Pulley(0, 0, 0), Pulley(2, 7, 0))
    );
    Assert.Equal(
      BeltPath.Overlap,
      Check(
        world,
        Pulley(0, 0, 0, size: 2),
        Pulley(1, 1, 0, size: 2)
      )
    );
    Assert.Equal(
      BeltPath.Blocked,
      BeltPath.Check(
        world.Accessor,
        Pulley(0, 0, 0),
        Pulley(2, 3, 0),
        [],
        _ => true
      )
    );
  }

  [Fact]
  public void The_runs_are_tangent_to_both_rims_and_the_cells_lie_in_the_plane() {
    BeltRun[] runs = BeltPath.Runs(
      Pulley(0, 0, 0, size: 2),
      Pulley(4, 0, 0)
    );

    Assert.Equal(2, runs.Length);
    foreach (BeltRun run in runs) {
      Assert.Equal(1.0, Dist(run.Start, 0.5, 0.5), 6);
      Assert.Equal(0.5, Dist(run.End, 4.5, 0.5), 6);
      Assert.Equal(0.5, run.Start.Z, 6);
      Assert.Equal(
        0.0,
        (run.End.X - run.Start.X) * (run.Start.X - 0.5)
          + (run.End.Y - run.Start.Y) * (run.Start.Y - 0.5),
        6
      );
      Assert.Equal(
        0.0,
        (run.End.X - run.Start.X) * (run.End.X - 4.5)
          + (run.End.Y - run.Start.Y) * (run.End.Y - 0.5),
        6
      );
    }
    foreach (
      BlockPos cell in BeltPath.CrossedCells(
        Pulley(0, 0, 0, size: 2),
        Pulley(4, 0, 0)
      )
    )
      Assert.Equal(0, cell.Z);
  }

  private static double Dist(Vec3d p, double cx, double cy) =>
    System.Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
}
