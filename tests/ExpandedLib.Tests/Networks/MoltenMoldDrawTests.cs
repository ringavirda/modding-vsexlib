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
/// Covers <see cref="IMoltenCell.IsOpenMold"/> on <see cref="MoltenNetwork"/> at the default radius of
/// 3 hops. Runs lie along +X from x = 0 unless stated; a wall holds nothing, so it carries hops and
/// trades no metal.
/// </summary>
public class MoltenMoldDrawTests {
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

  /// <summary>Places <paramref name="cells"/> at x = 0, 1, 2, ... as one run.</summary>
  private static Cell[] Run(TestWorld w, params Cell[] cells) {
    for (int x = 0; x < cells.Length; x++)
      Put(w, new BlockPos(x, 0, 0), Straight, cells[x]);
    Assert.Same(w.NetworkAt(cells[0].Pos), w.NetworkAt(cells[^1].Pos));
    return cells;
  }

  /// <summary>A drain fitting that holds nothing.</summary>
  private static Cell Mold(bool open = true, MoltenFlowRules? rules = null) =>
    new() {
      IsOpenMold = open,
      AcceptsSubMinimumFlow = true,
      FlowRules = rules,
    };

  private static Cell Wall(MoltenFlowRules? rules = null) =>
    new() { FlowRules = rules };

  /// <summary>A cell holding <paramref name="amount"/> units of iron in 100.</summary>
  private static Cell Holding(int amount, MoltenFlowRules? rules = null) =>
    new() {
      MaxUnitCapacity = 100,
      CellAmount = amount,
      CellMetalType = amount > 0 ? Iron : "",
      FlowRules = rules,
    };

  /// <summary>A mold at x = 0, walls up to <paramref name="hops"/> - 2, then an empty cell and one
  /// holding 40, <paramref name="hops"/> from the mold; returns the pair.</summary>
  private static (Cell Near, Cell Far) PairAt(
    TestWorld w,
    int hops,
    Cell mold,
    Cell? between = null
  ) {
    var cells = new Cell[hops + 1];
    cells[0] = mold;
    for (int x = 1; x < hops - 1; x++)
      cells[x] = x == 1 && between != null ? between : Wall();
    cells[hops - 1] = Holding(0);
    cells[hops] = Holding(40);
    Run(w, cells);
    return (cells[hops - 1], cells[hops]);
  }

  #endregion

  #region Radius

  // Fails when the radius is ignored (the cell 4 hops out draws its 40), or when it is off by one
  // (the cell 3 hops out levels by half).
  [Theory]
  [InlineData(3, 40, 0)]
  [InlineData(4, 20, 20)]
  public void A_cell_three_hops_from_an_open_mold_draws_and_one_four_hops_out_levels(
    int hops,
    int near,
    int far
  ) {
    var w = NewWorld();
    var pair = PairAt(w, hops, Mold());

    w.Tick();

    Assert.Equal((near, far), (pair.Near.CellAmount, pair.Far.CellAmount));
  }

  // Fails when the draw roots at every drain fitting instead of at open molds.
  [Fact]
  public void A_closed_mold_draws_nothing() {
    var w = NewWorld();
    var pair = PairAt(w, 3, Mold(open: false));

    w.Tick();

    Assert.Equal((20, 20), (pair.Near.CellAmount, pair.Far.CellAmount));
  }

  // Fails when the root check lets a sealed or solidified mold draw, or when the walk crosses a
  // sealed or solidified cell: either way the 40 is drawn whole.
  [Theory]
  [InlineData("mold-sealed")]
  [InlineData("mold-solid")]
  [InlineData("between-sealed")]
  [InlineData("between-solid")]
  public void A_sealed_or_solidified_mold_or_cell_between_stops_the_draw(
    string blocked
  ) {
    var w = NewWorld();
    var mold = new Cell {
      IsOpenMold = true,
      AcceptsSubMinimumFlow = true,
      Sealed = blocked == "mold-sealed",
      Solidified = blocked == "mold-solid",
    };
    var between = new Cell {
      Sealed = blocked == "between-sealed",
      Solidified = blocked == "between-solid",
    };
    var pair = PairAt(w, 3, mold, between);

    w.Tick();

    Assert.Equal((20, 20), (pair.Near.CellAmount, pair.Far.CellAmount));
  }

  #endregion

  #region Vertical hops

  // Fails when the walk hops down from a mold to the cell below it, which cannot feed it.
  [Fact]
  public void A_mold_above_a_run_draws_nothing_from_it() {
    var w = NewWorld();
    var foot = new BlockPos(0, 0, 0);
    Put(w, foot.UpCopy(), CanalNode.Make(2, BlockFacing.DOWN), Mold());
    Cell near = Put(
      w,
      foot,
      CanalNode.Make(3, BlockFacing.UP, BlockFacing.EAST),
      Holding(0)
    );
    Cell far = Put(w, foot.EastCopy(), Straight, Holding(40));
    Assert.Same(w.NetworkAt(foot.UpCopy()), w.NetworkAt(foot.EastCopy()));

    w.Tick();

    Assert.Equal((20, 20), (near.CellAmount, far.CellAmount));
  }

  // Fails when the walk ignores a horizontal-only connection up from the mold (the true case
  // draws), or when it never hops up (the false case levels).
  [Theory]
  [InlineData(true, 20, 20)]
  [InlineData(false, 40, 0)]
  public void A_run_above_a_mold_draws_unless_the_riser_is_horizontal_only(
    bool horizontalOnly,
    int nearAmount,
    int farAmount
  ) {
    var w = NewWorld();
    var rules = Canal with { HorizontalOnly = horizontalOnly };
    var moldPos = new BlockPos(0, 0, 0);
    var top = moldPos.UpCopy();
    Put(w, moldPos, CanalNode.Make(4, BlockFacing.UP), Mold(rules: rules));
    Cell near = Put(
      w,
      top,
      CanalNode.Make(5, BlockFacing.DOWN, BlockFacing.EAST),
      Holding(0, rules)
    );
    Cell far = Put(w, top.EastCopy(), Straight, Holding(40, rules));
    Assert.Same(w.NetworkAt(moldPos), w.NetworkAt(top.EastCopy()));

    w.Tick();

    Assert.Equal((nearAmount, farAmount), (near.CellAmount, far.CellAmount));
  }

  #endregion

  #region The level and the floor

  // Fails when the draw skips the level check: the far cell hands its 10 to the fuller one.
  [Fact]
  public void A_draw_never_moves_metal_toward_a_fuller_cell() {
    var w = NewWorld();
    var cells = Run(w, Mold(), Holding(40), Holding(10));

    w.Tick();

    Assert.Equal(new[] { 40, 10 }, cells.Skip(1).Select(c => c.CellAmount));
  }

  // Fails when the draw applies the connection's gap floor: the 9-unit gap under a floor of 10 holds.
  [Fact]
  public void A_draw_closes_a_gap_below_the_floor() {
    var w = NewWorld();
    var floored = Canal with { MinFlowGap = 10 };
    var cells = Run(
      w,
      Mold(rules: floored),
      Wall(floored),
      Holding(0, floored),
      Holding(9, floored)
    );

    w.Tick();

    Assert.Equal((9, 0), (cells[2].CellAmount, cells[3].CellAmount));
  }

  #endregion

  #region Settling

  /// <summary>A flow source at x = 0 conveys outward, so without the draw the nearer cell would hand
  /// its 40 on to the farther one and the draw would hand it back the next tick.</summary>
  // Fails when a connection inside the radius falls back to the network's rules while its nearer
  // cell is the fuller: the 40 swaps ends every tick.
  [Fact]
  public void Metal_beside_an_open_mold_does_not_slosh_between_two_ticks() {
    var w = NewWorld();
    var start = new Cell { IsFlowSource = true, FlowRules = Canal };
    var cells = Run(
      w,
      start,
      Mold(rules: Canal),
      Holding(40, Canal),
      Holding(0, Canal)
    );

    for (int t = 0; t < 2; t++) {
      w.Tick();
      Assert.Equal((40, 0), (cells[2].CellAmount, cells[3].CellAmount));
    }
  }

  /// <summary>The values are the network's levelling without a draw: a conveying run behind a flow
  /// source, ending in a drain fitting whose mold is closed.</summary>
  // Fails when the draw roots at every drain fitting: the run piles toward the closed mold instead.
  [Fact]
  public void An_idle_run_with_no_open_mold_settles_and_stops_changing() {
    var w = NewWorld();
    var cells = Run(
      w,
      new Cell {
        IsFlowSource = true,
        MaxUnitCapacity = 100,
        FlowRules = Canal,
      },
      Holding(70, Canal),
      Holding(0, Canal),
      Holding(35, Canal),
      Holding(5, Canal),
      Holding(0, Canal),
      new Cell {
        AcceptsSubMinimumFlow = true,
        MaxUnitCapacity = 30,
        FlowRules = Canal,
      }
    );
    w.Tick(60);
    int[] settled = [.. cells.Select(c => c.CellAmount)];

    for (int t = 0; t < 2; t++) {
      w.Tick();
      Assert.Equal(settled, cells.Select(c => c.CellAmount));
    }
    Assert.Equal("13,14,15,16,17,17,18", string.Join(",", settled));
  }

  #endregion

  #region Delivery

  /// <summary>An open mold 4 hops behind the flow source: the source and the 19-cell run ahead of it
  /// lie outside the radius, so the run conveys as it does with no mold.</summary>
  // Fails when the radius is ignored: the source lies inside it and hands nothing to the run, which
  // lies farther from the mold.
  [Fact]
  public void A_long_run_with_no_open_mold_within_the_radius_delivers_as_without_one() {
    var w = NewWorld();
    var cells = new Cell[24];
    cells[0] = Mold(rules: Canal);
    for (int x = 1; x < 4; x++)
      cells[x] = Wall(Canal);
    cells[4] = new Cell {
      IsFlowSource = true,
      MaxUnitCapacity = 2000,
      CellAmount = 2000,
      CellMetalType = Iron,
      FlowRules = Canal,
    };
    for (int x = 5; x < 24; x++)
      cells[x] = Holding(0, Canal);
    Run(w, cells);

    w.Tick(18);
    Assert.Equal(0, cells[23].CellAmount);

    w.Tick();
    Assert.Equal(
      Enumerable.Repeat(100, 20).Prepend(0),
      cells.Skip(3).Select(c => c.CellAmount)
    );
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
        $"test:molddrawnode-{id}",
        id
      );
  }

  /// <summary>A molten cell holding whole units at a fixed 1500 C.</summary>
  private sealed class Cell : BlockEntity, IMoltenCell {
    public int CellAmount { get; set; }

    public string CellMetalType { get; set; } = "";

    public float CellTemperature => 1500f;

    public int MaxUnitCapacity { get; init; }

    public bool Sealed { get; init; }

    public bool Solidified { get; init; }

    public bool IsFlowSource { get; init; }

    public bool AcceptsSubMinimumFlow { get; init; }

    public MoltenFlowRules? FlowRules { get; init; }

    public bool IsOpenMold { get; init; }

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
