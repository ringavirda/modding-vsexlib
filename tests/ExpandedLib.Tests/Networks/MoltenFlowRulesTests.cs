using System;
using System.Linq;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers <see cref="IMoltenCell.FlowRules"/> on <see cref="MoltenNetwork"/>: a connection whose
/// two cells return rules conveys, floors and caps by them, and one where either returns none keeps
/// the network's defaults. Runs lie along +X from a flow source at the origin unless stated.
/// </summary>
public class MoltenFlowRulesTests {
  #region Fixtures

  private const string Iron = "game:ingot-iron";

  private static readonly MoltenFlowRules Canal = new(
    FlowRate: 100,
    MinFlowGap: 1,
    Conveys: true,
    HorizontalOnly: true
  );

  private static readonly CanalNode Straight = CanalNode.Make(
    1,
    BlockFacing.EAST,
    BlockFacing.WEST
  );

  private static readonly CanalNode Riser = CanalNode.Make(
    2,
    BlockFacing.UP,
    BlockFacing.DOWN
  );

  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("molten", sys => new MoltenNetwork(sys));
    return w;
  }

  private static Cell Put(TestWorld w, BlockPos pos, Block node, Cell cell) {
    w.Place(pos, node, cell);
    w.Attach(cell);
    w.AddNode(pos, "molten");
    return cell;
  }

  /// <summary>A flow source holding <paramref name="amount"/> units of iron.</summary>
  private static Cell Source(
    int amount,
    int capacity,
    MoltenFlowRules? rules
  ) =>
    new() {
      IsFlowSource = true,
      MaxUnitCapacity = capacity,
      CellAmount = amount,
      CellMetalType = Iron,
      FlowRules = rules,
    };

  private static Cell Empty(
    int capacity,
    MoltenFlowRules? rules,
    bool drain = false
  ) =>
    new() {
      MaxUnitCapacity = capacity,
      FlowRules = rules,
      AcceptsSubMinimumFlow = drain,
    };

  /// <summary>A flow source at the origin and one empty cell east of it, both in one run.</summary>
  private static (Cell Start, Cell Next) Pair(
    TestWorld w,
    int amount,
    MoltenFlowRules? startRules,
    MoltenFlowRules? nextRules
  ) {
    var origin = new BlockPos(0, 0, 0);
    Cell start = Put(w, origin, Straight, Source(amount, 1000, startRules));
    Cell next = Put(w, origin.EastCopy(), Straight, Empty(1000, nextRules));
    Assert.Same(w.NetworkAt(origin), w.NetworkAt(origin.EastCopy()));
    return (start, next);
  }

  #endregion

  #region Conveying

  /// <summary>smex's FlowEdge drives the farthest edge first and hands a receiver farther out the
  /// whole difference, so each tick every full cell passes its 100 on and the start refills cell 1:
  /// after tick t cells 1..t are full, and cell 9 fills on tick 9.</summary>
  // Fails when conveying is ignored: every step halves and the far cell trails the run.
  [Fact]
  public void A_ten_cell_run_fills_its_far_cell_on_the_ninth_tick() {
    var w = NewWorld();
    var cells = new Cell[10];
    cells[0] = Put(w, new BlockPos(0, 0, 0), Straight, Source(1000, 1000, Canal));
    for (int x = 1; x < 10; x++)
      cells[x] = Put(w, new BlockPos(x, 0, 0), Straight, Empty(100, Canal));
    Assert.Same(w.NetworkAt(cells[0].Pos), w.NetworkAt(cells[9].Pos));

    w.Tick(8);
    Assert.Equal(
      new[] { 200, 100, 100, 100, 100, 100, 100, 100, 100, 0 },
      cells.Select(c => c.CellAmount)
    );

    w.Tick();
    Assert.Equal(
      new[] { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100 },
      cells.Select(c => c.CellAmount)
    );
  }

  // Fails when a connection conveys if either cell conveys, or when a cell's Conveys is ignored.
  [Theory]
  [InlineData(false, true)]
  [InlineData(true, false)]
  public void A_connection_with_a_cell_that_does_not_convey_levels_by_half(
    bool startConveys,
    bool nextConveys
  ) {
    var w = NewWorld();
    var (start, next) = Pair(
      w,
      60,
      Canal with { Conveys = startConveys },
      Canal with { Conveys = nextConveys }
    );

    w.Tick();

    Assert.Equal(30, start.CellAmount);
    Assert.Equal(30, next.CellAmount);
  }

  #endregion

  #region Vertical faces

  // Fails when horizontal-only is ignored, or when a connection is horizontal-only only if both
  // cells are.
  [Theory]
  [InlineData(true, true)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  public void A_stacked_pair_with_a_horizontal_only_cell_moves_nothing_vertically(
    bool upperHorizontalOnly,
    bool lowerHorizontalOnly
  ) {
    var w = NewWorld();
    var lowerPos = new BlockPos(0, 0, 0);
    Cell lower = Put(
      w,
      lowerPos,
      Riser,
      Empty(100, Canal with { HorizontalOnly = lowerHorizontalOnly })
    );
    Cell upper = Put(
      w,
      lowerPos.UpCopy(),
      Riser,
      Source(60, 100, Canal with { HorizontalOnly = upperHorizontalOnly })
    );
    Assert.Same(w.NetworkAt(lowerPos), w.NetworkAt(lowerPos.UpCopy()));

    w.Tick();

    Assert.Equal(60, upper.CellAmount);
    Assert.Equal(0, lower.CellAmount);
  }

  // Fails when a connection is horizontal-only whatever its cells return.
  [Fact]
  public void A_stacked_pair_neither_horizontal_only_runs_its_whole_difference_downhill() {
    var w = NewWorld();
    var lowerPos = new BlockPos(0, 0, 0);
    var sideways = Canal with { HorizontalOnly = false };
    Cell lower = Put(w, lowerPos, Riser, Empty(100, sideways));
    Cell upper = Put(w, lowerPos.UpCopy(), Riser, Source(60, 100, sideways));

    w.Tick();

    Assert.Equal(0, upper.CellAmount);
    Assert.Equal(60, lower.CellAmount);
  }

  #endregion

  #region Floor

  // Fails when the floor is ignored (the plain pair conveys its 9), or when it also binds a drain.
  [Fact]
  public void With_gap_10_a_9_unit_gap_holds_and_a_9_unit_remainder_drains() {
    var w = NewWorld();
    var floored = Canal with { MinFlowGap = 10 };
    var (start, next) = Pair(w, 9, floored, floored);
    var drainPos = new BlockPos(10, 0, 0);
    Cell tapStart = Put(w, drainPos, Straight, Source(9, 100, floored));
    Cell tap = Put(
      w,
      drainPos.EastCopy(),
      Straight,
      Empty(100, floored, drain: true)
    );

    w.Tick();

    Assert.Equal(9, start.CellAmount);
    Assert.Equal(0, next.CellAmount);
    Assert.Equal(0, tapStart.CellAmount);
    Assert.Equal(9, tap.CellAmount);
  }

  // Fails when a connection takes the floor of its driving cell alone.
  [Theory]
  [InlineData(10, 1)]
  [InlineData(1, 10)]
  public void A_connection_takes_the_larger_gap(int startGap, int nextGap) {
    var w = NewWorld();
    var (start, next) = Pair(
      w,
      9,
      Canal with { MinFlowGap = startGap },
      Canal with { MinFlowGap = nextGap }
    );

    w.Tick();

    Assert.Equal(9, start.CellAmount);
    Assert.Equal(0, next.CellAmount);
  }

  #endregion

  #region Rate

  // Fails when the rate is ignored: the edge moves ExlibValues.MoltenFlowRate, 50.
  [Fact]
  public void An_edge_moves_100_per_tick() {
    var w = NewWorld();
    var (start, next) = Pair(w, 1000, Canal, Canal);

    w.Tick();

    Assert.Equal(900, start.CellAmount);
    Assert.Equal(100, next.CellAmount);
  }

  // Fails when a connection takes the rate of its driving cell alone.
  [Theory]
  [InlineData(100, 30)]
  [InlineData(30, 100)]
  public void A_connection_takes_the_smaller_rate(int startRate, int nextRate) {
    var w = NewWorld();
    var (_, next) = Pair(
      w,
      1000,
      Canal with { FlowRate = startRate },
      Canal with { FlowRate = nextRate }
    );

    w.Tick();

    Assert.Equal(30, next.CellAmount);
  }

  #endregion

  #region No rules

  // Fails when one cell's rules govern a connection the other cell returns none for.
  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  public void A_connection_with_a_cell_returning_no_rules_levels_by_half(
    bool startRules,
    bool nextRules
  ) {
    var w = NewWorld();
    var (start, next) = Pair(
      w,
      60,
      startRules ? Canal : null,
      nextRules ? Canal : null
    );

    w.Tick();

    Assert.Equal(30, start.CellAmount);
    Assert.Equal(30, next.CellAmount);
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

    public MoltenFlowRules? FlowRules { get; init; }

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
