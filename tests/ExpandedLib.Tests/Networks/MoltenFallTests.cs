using System;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers <see cref="IMoltenCell.Fall"/> on <see cref="MoltenNetwork"/>: a horizontal connection
/// between two cells of different fall is directed and conveys all of the upper cell's metal, and a
/// level pair still levels. Cells carry no flow rules, so 50 units a tick applies.
/// </summary>
public class MoltenFallTests {
  #region Fixtures

  private const string Iron = "game:ingot-iron";

  private static readonly CanalNode Straight = CanalNode.Make(
    1,
    BlockFacing.EAST,
    BlockFacing.WEST
  );

  private static readonly CanalNode Down = CanalNode.Make(2, BlockFacing.DOWN);

  private static readonly CanalNode Up = CanalNode.Make(3, BlockFacing.UP);

  private static (Cell Start, Cell Next) Pair(
    int startAmount,
    int startFall,
    int nextAmount,
    int nextFall
  ) {
    var w = new TestWorld();
    w.RegisterNetwork("molten", sys => new MoltenNetwork(sys));
    var origin = new BlockPos(0, 0, 0);
    var start = new Cell {
      IsFlowSource = true,
      MaxUnitCapacity = 1000,
      CellAmount = startAmount,
      CellMetalType = Iron,
      Fall = startFall,
    };
    var next = new Cell {
      MaxUnitCapacity = 1000,
      CellAmount = nextAmount,
      CellMetalType = nextAmount > 0 ? Iron : "",
      Fall = nextFall,
    };
    foreach (
      var (pos, cell) in new[] { (origin, start), (origin.EastCopy(), next) }
    ) {
      w.Place(pos, Straight, cell);
      w.Attach(cell);
      w.AddNode(pos, "molten");
    }
    Assert.Same(w.NetworkAt(origin), w.NetworkAt(origin.EastCopy()));
    w.Tick();
    return (start, next);
  }

  private static (Cell Upper, Cell Lower) Stack(
    int upperAmount,
    int upperFall,
    int lowerAmount
  ) {
    var w = new TestWorld();
    w.RegisterNetwork("molten", sys => new MoltenNetwork(sys));
    var low = new BlockPos(0, 0, 0);
    var high = low.UpCopy();
    var upper = new Cell {
      IsFlowSource = true,
      MaxUnitCapacity = 1000,
      CellAmount = upperAmount,
      CellMetalType = Iron,
      Fall = upperFall,
    };
    var lower = new Cell {
      MaxUnitCapacity = 1000,
      CellAmount = lowerAmount,
      CellMetalType = lowerAmount > 0 ? Iron : "",
    };
    foreach (
      var (pos, cell, node) in new[] { (high, upper, Down), (low, lower, Up) }
    ) {
      w.Place(pos, node, cell);
      w.Attach(cell);
      w.AddNode(pos, "molten");
    }
    Assert.Same(w.NetworkAt(low), w.NetworkAt(high));
    w.Tick();
    return (upper, lower);
  }

  #endregion

  #region Directed edges

  // Fails when the vertical edge moves only the difference: 10 units cross and the upper keeps 30.
  [Fact]
  public void A_drop_with_a_fall_hands_over_its_whole_charge_onto_a_backed_up_cell() {
    var (upper, lower) = Stack(40, 16, 30);
    Assert.Equal(0, upper.CellAmount);
    Assert.Equal(70, lower.CellAmount);
  }

  // Fails when every vertical edge takes the whole charge: the level upper cell empties.
  [Fact]
  public void A_level_cell_over_a_fuller_cell_still_gives_only_the_difference() {
    var (upper, lower) = Stack(40, 0, 30);
    Assert.Equal(30, upper.CellAmount);
    Assert.Equal(40, lower.CellAmount);
  }

  // Fails when the edge equalises: the pair settles at 20 and 20.
  [Fact]
  public void A_two_cell_fall_conveys_all_of_it_downhill() {
    var (start, next) = Pair(40, 2, 0, 5);
    Assert.Equal(0, start.CellAmount);
    Assert.Equal(40, next.CellAmount);
  }

  // Fails when the edge is built only between two sloped cells, or takes the receiver's metal as a
  // reason to level: the pair settles at 50 and 50 or the level cell keeps its 40.
  [Fact]
  public void A_level_cell_pours_onto_a_fall_that_holds_more() {
    var (start, next) = Pair(40, 0, 60, 3);
    Assert.Equal(0, start.CellAmount);
    Assert.Equal(100, next.CellAmount);
  }

  // Fails when the edge moves more than the rate: the whole 120 crosses.
  [Fact]
  public void A_fall_moves_no_more_than_the_flow_rate_in_a_tick() {
    var (start, next) = Pair(120, 0, 0, 3);
    Assert.Equal(70, start.CellAmount);
    Assert.Equal(50, next.CellAmount);
  }

  // Fails when the larger fall may give: the slope hands its 40 up to the level cell.
  [Fact]
  public void Metal_on_a_fall_does_not_climb_to_a_level_cell() {
    var (start, next) = Pair(40, 3, 0, 0);
    Assert.Equal(40, start.CellAmount);
    Assert.Equal(0, next.CellAmount);
  }

  // Fails when level cells take the directed move: the whole 40 crosses instead of half.
  [Fact]
  public void A_level_pair_still_levels() {
    var (start, next) = Pair(40, 0, 0, 0);
    Assert.Equal(20, start.CellAmount);
    Assert.Equal(20, next.CellAmount);
  }

  #endregion

  #region Stand-ins

  /// <summary>A molten node block with connectors on <see cref="Faces"/> only.</summary>
  private sealed class CanalNode : BlockNetworkNode {
    public BlockFacing[] Faces { get; init; } = [];

    public override string NetworkType => "molten";

    public override bool HasConnectorAt(BlockFacing face) =>
      Array.IndexOf(Faces, face) >= 0;

    public static CanalNode Make(int id, params BlockFacing[] faces) =>
      TestBlocks.Configure(
        new CanalNode { Faces = faces },
        $"test:canalnode-{id}",
        id
      );
  }

  /// <summary>A molten cell holding whole units at a fixed 1500 C, never sealed or
  /// solidified.</summary>
  private sealed class Cell : BlockEntity, IMoltenCell {
    public int CellAmount { get; set; }

    public string CellMetalType { get; set; } = "";

    public float CellTemperature => 1500f;

    public int MaxUnitCapacity { get; init; }

    public bool Sealed => false;

    public bool Solidified => false;

    public bool IsFlowSource { get; init; }

    public bool AcceptsSubMinimumFlow { get; init; }

    public int Fall { get; init; }

    public void EnsureMetalStack(IWorldAccessor world) { }

    public int PushMetalRaw(
      int amount,
      string metalType,
      float temperature,
      IWorldAccessor world
    ) {
      if (CellAmount > 0 && metalType != CellMetalType)
        return 0;
      int accepted = Math.Min(amount, MaxUnitCapacity - CellAmount);
      CellAmount += accepted;
      CellMetalType = metalType;
      return accepted;
    }

    public int DrainMetal(int amount) {
      int drained = Math.Min(amount, CellAmount);
      CellAmount -= drained;
      if (CellAmount == 0)
        CellMetalType = "";
      return drained;
    }

    public void UpdateThermal(IWorldAccessor world) { }
  }

  #endregion
}
