using BurdenMaker.Blocks;
using ExpandedLib.Helpers;
using Vintagestory.API.MathTools;
using Xunit;
using static BurdenMaker.Blocks.BlockBurdenmaker;

namespace BurdenMaker.Tests;

/// <summary>The burdenmaker's per-cell verb map, at every facing; the placement rotation must be
/// inverted before an offset means anything.</summary>
public class BurdenmakerCellTests {
  private static readonly BlockPos Principal = new(64, 110, 64);

  private static readonly int[] AllAngles = [0, 90, 180, 270];

  /// <summary>The design's per-cell table, keyed by the authored north-frame offset.</summary>
  public static TheoryData<int, int, int, int, BurdenmakerCell> AuthoredCells() {
    var data = new TheoryData<int, int, int, int, BurdenmakerCell>();
    (int X, int Y, int Z, BurdenmakerCell Class)[] cells =
    [
      // The hoppers sit over the front half only (z = -1); the wide hopper spans two cells.
      (-1, 1, -1, BurdenmakerCell.OreHopper),
      (0, 1, -1, BurdenmakerCell.OreHopper),
      (1, 1, -1, BurdenmakerCell.FluxHopper),
      // The principal is the gate: one sliding lid under both hoppers.
      (0, 0, 0, BurdenmakerCell.Gate),
      // Five basin cells: the nine-cell footprint less the three hopper cells and the principal.
      (-1, 0, -1, BurdenmakerCell.Bunker),
      (0, 0, -1, BurdenmakerCell.Bunker),
      (1, 0, -1, BurdenmakerCell.Bunker),
      (-1, 0, 0, BurdenmakerCell.Bunker),
      (1, 0, 0, BurdenmakerCell.Bunker),
    ];
    foreach (int angle in AllAngles)
      foreach (var (x, y, z, klass) in cells)
        data.Add(angle, x, y, z, klass);
    return data;
  }

  #region Every authored cell, at every facing

  [Theory]
  [MemberData(nameof(AuthoredCells))]
  public void Every_cell_keeps_its_verb_at_every_facing(
    int angle,
    int localX,
    int localY,
    int localZ,
    BurdenmakerCell expected
  ) {
    BlockPos world = ExOrientation.GlobalPos(
      Principal,
      localX,
      localY,
      localZ,
      angle
    );

    Assert.Equal(expected, BlockBurdenmaker.Classify(Principal, world, angle));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void A_cell_beyond_the_footprint_is_Outside(int angle) {
    // One step past the front row, one past the flanking column, one above the hopper course.
    foreach (
      (int x, int y, int z) in new[] { (0, 1, -2), (2, 0, 0), (0, 2, 0) }
    ) {
      BlockPos world = ExOrientation.GlobalPos(Principal, x, y, z, angle);
      Assert.Equal(
        BurdenmakerCell.Outside,
        BlockBurdenmaker.Classify(Principal, world, angle)
      );
    }
  }

  [Fact]
  public void The_back_half_at_hopper_height_is_Outside_not_a_hopper() {
    // The y = 1, z = 0 row is open; height alone does not make a cell a hopper.
    foreach (int x in new[] { -1, 0, 1 })
      Assert.Equal(
        BurdenmakerCell.Outside,
        BlockBurdenmaker.Classify(
          Principal,
          ExOrientation.GlobalPos(Principal, x, 1, 0, 0),
          0
        )
      );
  }

  #endregion

  #region The regression this task exists for

  [Fact]
  public void Ore_and_flux_hoppers_do_not_swap_when_the_block_faces_east() {
    const int East = 270;

    BlockPos flux = ExOrientation.GlobalPos(Principal, 1, 1, -1, East);
    BlockPos ore = ExOrientation.GlobalPos(Principal, -1, 1, -1, East);

    Assert.NotEqual(flux.X, ore.X + 2);
    Assert.NotEqual(flux, ore);

    Assert.Equal(
      BurdenmakerCell.FluxHopper,
      BlockBurdenmaker.Classify(Principal, flux, East)
    );
    Assert.Equal(
      BurdenmakerCell.OreHopper,
      BlockBurdenmaker.Classify(Principal, ore, East)
    );
  }

  [Fact]
  public void The_same_world_cell_means_different_things_at_different_facings() {
    // One fixed world cell, two facings, two verbs: the classification depends on `structureAngle`.
    BlockPos cell = Principal.AddCopy(1, 1, -1);

    Assert.Equal(
      BurdenmakerCell.FluxHopper,
      BlockBurdenmaker.Classify(Principal, cell, 0)
    );
    Assert.Equal(
      BurdenmakerCell.Outside,
      BlockBurdenmaker.Classify(Principal, cell, 90)
    );
  }

  #endregion
}
