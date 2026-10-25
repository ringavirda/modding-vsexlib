using System;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers the connector test at both ends of a face in <see cref="MoltenNetwork"/>: a joint one end
/// has no connector for moves no metal and carries no mold draw, whichever end hides it.
/// </summary>
public class MoltenJointTests {
  #region Fixtures

  private const string Iron = "game:ingot-iron";

  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("molten", sys => new MoltenNetwork(sys));
    return w;
  }

  private static Cell Put(
    TestWorld w,
    BlockPos pos,
    int id,
    Cell cell,
    params BlockFacing[] faces
  ) {
    w.Place(pos, CanalNode.Make(id, faces), cell);
    w.Attach(cell);
    w.AddNode(pos, "molten");
    return cell;
  }

  private static Cell Holding(int amount) =>
    new() {
      MaxUnitCapacity = 100,
      CellAmount = amount,
      CellMetalType = amount > 0 ? Iron : "",
    };

  #endregion

  #region Flow

  // Fails when the flow pass tests only the driving cell's connector: the 40 crosses a joint the
  // other end has no connector for, from either side.
  [Theory]
  [InlineData(true, 40, 0)]
  [InlineData(true, 0, 40)]
  [InlineData(false, 40, 0)]
  [InlineData(false, 0, 40)]
  public void A_joint_with_no_connector_at_one_end_moves_no_metal_in_either_direction(
    bool hiddenAtFarEnd,
    int west,
    int east
  ) {
    var w = NewWorld();
    var a = Put(
      w,
      new BlockPos(0, 0, 0),
      1,
      Holding(west),
      hiddenAtFarEnd
        ? [BlockFacing.EAST, BlockFacing.SOUTH]
        : [BlockFacing.SOUTH]
    );
    var b = Put(
      w,
      new BlockPos(1, 0, 0),
      2,
      Holding(east),
      hiddenAtFarEnd
        ? [BlockFacing.SOUTH]
        : [BlockFacing.WEST, BlockFacing.SOUTH]
    );
    var c = Put(w, new BlockPos(1, 0, 1), 3, Holding(0), BlockFacing.NORTH, BlockFacing.WEST);
    var d = Put(w, new BlockPos(0, 0, 1), 4, Holding(0), BlockFacing.EAST, BlockFacing.NORTH);
    c.MaxUnitCapacity = 0;
    d.MaxUnitCapacity = 0;
    Assert.Same(w.NetworkAt(a.Pos), w.NetworkAt(b.Pos));

    w.Tick(5);

    Assert.Equal((west, east), (a.CellAmount, b.CellAmount));
  }

  #endregion

  #region Mold draw

  // Fails when the mold walk tests only the near end: R lies 1 hop from the mold across a joint R has no
  // connector for, so R and S levels instead of R handing its 40 to S, 3 hops round.
  [Fact]
  public void The_mold_walk_does_not_cross_a_joint_the_far_end_has_no_connector_for() {
    var w = NewWorld();
    var mold = Put(
      w,
      new BlockPos(0, 0, 0),
      1,
      new Cell {
        IsOpenMold = true,
        AcceptsSubMinimumFlow = true,
      },
      BlockFacing.EAST,
      BlockFacing.SOUTH
    );
    Put(w, new BlockPos(0, 0, 1), 2, Holding(0), BlockFacing.NORTH, BlockFacing.EAST);
    var s = Put(w, new BlockPos(1, 0, 1), 3, Holding(0), BlockFacing.WEST, BlockFacing.NORTH);
    var r = Put(w, new BlockPos(1, 0, 0), 4, Holding(40), BlockFacing.SOUTH);
    Assert.Same(w.NetworkAt(mold.Pos), w.NetworkAt(r.Pos));

    w.Tick();

    Assert.Equal((0, 40), (r.CellAmount, s.CellAmount));
  }

  #endregion

  #region Stand-ins

  private sealed class CanalNode : BlockNetworkNode {
    public BlockFacing[] Faces { get; init; } = [];

    public override string NetworkType => "molten";

    public override bool HasConnectorAt(BlockFacing face) =>
      Array.IndexOf(Faces, face) >= 0;

    public static CanalNode Make(int id, params BlockFacing[] faces) =>
      TestBlocks.Configure(
        new CanalNode { Faces = faces },
        $"test:moltenjointnode-{id}",
        id
      );
  }

  private sealed class Cell : BlockEntity, IMoltenCell {
    public int CellAmount { get; set; }

    public string CellMetalType { get; set; } = "";

    public float CellTemperature => 1500f;

    public int MaxUnitCapacity { get; set; }

    public bool Sealed => false;

    public bool Solidified => false;

    public bool IsFlowSource => false;

    public bool AcceptsSubMinimumFlow { get; init; }

    public MoltenFlowRules? FlowRules => null;

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
