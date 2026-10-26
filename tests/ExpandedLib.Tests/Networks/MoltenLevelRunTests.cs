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
/// Covers a level run on <see cref="MoltenNetwork"/>: each cell hands onward all but
/// <c>MoltenMinFlowAmount</c> units to the cell farther from the start, at most
/// <c>MoltenFlowRate</c> a joint, so a pour crosses a run of any length and leaves a skin; a run
/// with fall drains clean; cells at one distance, and a cell farther back, level by half. Cells
/// carry no flow rules. Runs lie along +X from the flow source at the origin.
/// </summary>
public class MoltenLevelRunTests {
  #region Fixtures

  private const string Iron = "game:ingot-iron";

  private static readonly CanalNode Straight = CanalNode.Make(
    1,
    BlockFacing.EAST,
    BlockFacing.WEST
  );

  /// <summary>A run of <paramref name="falls"/>.Length cells, the first a flow source, each of
  /// capacity 100 and the fall given.</summary>
  private static (TestWorld World, Cell[] Cells) Run(params int[] falls) {
    var w = new TestWorld();
    w.RegisterNetwork("molten", sys => new MoltenNetwork(sys));
    var cells = new Cell[falls.Length];
    for (int x = 0; x < cells.Length; x++) {
      cells[x] = new Cell {
        IsFlowSource = x == 0,
        MaxUnitCapacity = 100,
        Fall = falls[x],
      };
      var pos = new BlockPos(x, 0, 0);
      w.Place(pos, Straight, cells[x]);
      w.Attach(cells[x]);
      w.AddNode(pos, "molten");
    }
    Assert.Same(
      w.NetworkAt(new BlockPos(0, 0, 0)),
      w.NetworkAt(new BlockPos(cells.Length - 1, 0, 0))
    );
    return (w, cells);
  }

  /// <summary>Pours <paramref name="amount"/> units into the first cell, then ticks once.</summary>
  private static void PourAndTick(TestWorld w, Cell[] cells, int amount) {
    cells[0].PushMetalRaw(amount, Iron, 1500f, null!);
    w.Tick();
  }

  /// <summary>Takes up to 50 units from the last cell, as a flask's gate does.</summary>
  private static int Pull(Cell[] cells) =>
    cells[^1].DrainMetal(Math.Min(50, cells[^1].CellAmount));

  #endregion

  #region Level run

  // Fails when a level cell hands onward half the difference: a long run carries a few units a second.
  [Fact]
  public void A_level_run_of_twenty_cells_fed_fifty_a_second_delivers_fifty_at_its_end() {
    var (w, cells) = Run(new int[20]);
    for (int t = 0; t < 120; t++) {
      PourAndTick(w, cells, 50);
      Pull(cells);
    }
    for (int t = 0; t < 10; t++) {
      PourAndTick(w, cells, 50);
      Assert.Equal(50, Pull(cells));
    }
  }

  // Fails when the skin is dropped: the cells empty to nothing.
  [Fact]
  public void A_level_run_keeps_ten_units_a_cell_once_the_pour_stops() {
    var (w, cells) = Run(new int[20]);
    for (int t = 0; t < 10; t++) {
      PourAndTick(w, cells, 50);
      Pull(cells);
    }
    for (int t = 0; t < 100; t++) {
      w.Tick();
      Pull(cells);
    }
    Assert.All(cells.Take(19), c => Assert.Equal(10, c.CellAmount));
    Assert.Equal(0, cells[^1].CellAmount);
  }

  // Fails when a fall is treated as level: the cells keep a skin.
  [Fact]
  public void A_run_with_fall_keeps_no_metal_once_the_pour_stops() {
    var (w, cells) = Run(0, 1, 2, 3, 4, 5);
    for (int t = 0; t < 4; t++) {
      PourAndTick(w, cells, 50);
      Pull(cells);
    }
    for (int t = 0; t < 20; t++) {
      w.Tick();
      Pull(cells);
    }
    Assert.All(cells, c => Assert.Equal(0, c.CellAmount));
  }

  // Fails when the hand-onward step is also applied toward the start: metal swaps ends every tick.
  [Fact]
  public void An_idle_level_run_settles_without_swapping_its_charge() {
    var (w, cells) = Run(new int[5]);
    cells[0].PushMetalRaw(100, Iron, 1500f, null!);
    for (int t = 0; t < 60; t++)
      w.Tick();
    var settled = cells.Select(c => c.CellAmount).ToArray();
    for (int t = 0; t < 6; t++) {
      w.Tick();
      Assert.Equal(settled, cells.Select(c => c.CellAmount));
    }
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
