using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Scene = ExpandedLib.Tests.HandAssemblyFixTests.Scene;

namespace ExpandedLib.Tests;

/// <summary>The cell in front of a capped end: no piece is offered or fixed there, whatever its arms, and the refusal is shown; taking the cap off opens it.</summary>
public class HandAssemblyCapInWayTests {
  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly BlockPos Ground = new(64, 15, 64, 0);
  private static readonly BlockPos North = Cell.AddCopy(BlockFacing.NORTH);

  private static Scene Capped() {
    var scene = Scene.Standing(["ns", "we"]);
    scene.World.RegisterNetwork("test", sys => new StubNetwork(sys));
    scene.CappedPipe(North, "ns", BlockFacing.SOUTH);
    scene.Bolts(10);
    return scene;
  }

  // Fails when Fix does not refuse a cell in front of a capped end, so an end-on piece, whose arm meets the cap, or a side-on piece, which has none toward it, is placed.
  [Theory]
  [InlineData("ns")]
  [InlineData("we")]
  public void Fix_RefusesTheCellInFrontOfACap(string token) {
    var scene = Capped();

    AssemblyResult? result = scene.Fix(token, Ground, BlockFacing.UP);

    Assert.Equal(HandAssemblyServer.CapInWay, result!.Code);
    scene.AssertNothingTaken(10);
  }

  // Fails when Of offers placements in the cell in front of a capped end, so the ghost and its rings draw there.
  [Fact]
  public void Of_OffersNothingInFrontOfACap() {
    var scene = Capped();

    Assert.Empty(scene.Offer(Ground, BlockFacing.UP));
  }

  // Fails when InTheWay answers for a cap on any face of the neighbour, not the face toward the cell.
  [Theory]
  [InlineData("ns")]
  [InlineData("we")]
  public void CapOnAnotherFace_LeavesTheCellOpen(string token) {
    var scene = Scene.Standing(["ns", "we"]);
    BEBehaviorAssembled record = scene.CappedPipe(
      North,
      "ns",
      BlockFacing.NORTH
    );
    scene.Bolts(10);

    Assert.NotEmpty(scene.Offer(Ground, BlockFacing.UP));
    Assert.Null(scene.Fix(token, Ground, BlockFacing.UP));
    Assert.NotNull(record.CapAt(BlockFacing.NORTH));
  }

  // Fails when the cap take-off leaves the cell closed.
  [Theory]
  [InlineData("ns")]
  [InlineData("we")]
  public void CapTakeOff_OpensTheCell(string token) {
    var scene = Capped();
    Block part = scene.Player.ActiveSlot.Itemstack!.Block;
    scene.Player.Hold(new ItemStack(scene.Wrench));
    Assert.Null(
      HandAssemblyServer.Cap(
        scene.World.World,
        scene.Player.Player,
        North,
        new Vec3d(0.5, 0.5, 1)
      )
    );
    scene.Player.Hold(new ItemStack(part));

    Assert.NotEmpty(scene.Offer(Ground, BlockFacing.UP));
    Assert.Null(scene.Fix(token, Ground, BlockFacing.UP));
  }

  // Fails when Refresh does not set the refusal, or a sneak press sends a packet or shows nothing.
  [Fact]
  public void Control_ShowsTheRefusalAndSendsNothing() {
    var scene = Capped();
    var driver = new HandAssemblyControlTests.Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();

    driver.Hold(0.1, sneak: true);

    Assert.Equal(HandAssemblyServer.CapInWay, driver.Control.Refusal);
    Assert.Empty(driver.Channel.Sent);
    Assert.Equal([HandAssemblyServer.CapInWay], driver.Refusals.Distinct());
  }
}
