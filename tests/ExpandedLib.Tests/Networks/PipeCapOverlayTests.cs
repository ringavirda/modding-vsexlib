using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The cap overlays a pipe piece draws on its capped ends: which faces, in what pose, and the mesh key that follows them.</summary>
public class PipeCapOverlayTests {
  private static BlockEntityPipe Piece(
    TestWorld w,
    BlockFacing[] capped,
    string orientation = "nsweud"
  ) {
    var pos = new BlockPos(5, 0, 5);
    var block = TestBlocks.Configure(
      new BlockPipe(),
      $"test:pipe-cast-junction-{orientation}",
      97,
      ("type", "junction"),
      ("tier", "cast"),
      ("orientation", orientation)
    );
    block.SetNetworkTypeForTest("junction");
    block.ApplyOrientationForTest(orientation);
    var be = new BlockEntityPipe();
    var assembled = new BEBehaviorAssembled(be);
    be.Behaviors.Add(assembled);
    w.Place(pos, block, be);
    w.Attach(be);
    foreach (BlockFacing face in capped)
      assembled.SetCap(face, new ItemStack(w.RegisterItem("test:cap")), null);
    return be;
  }

  // Fails when an overlay is drawn on every end, capped or not.
  [Fact]
  public void CapOverlays_OnlyWhenCapped() {
    var w = new TestWorld();

    Assert.Empty(Piece(w, []).CapOverlays());
    BlockFacing[] drawn =
    [
      .. Piece(w, [BlockFacing.EAST, BlockFacing.UP])
        .CapOverlays()
        .Select(o => o.Face),
    ];
    Assert.Equal(2, drawn.Length);
    Assert.Contains(BlockFacing.EAST, drawn);
    Assert.Contains(BlockFacing.UP, drawn);
  }

  // Fails when a face takes another face's pose, such as U and D the north rotation.
  [Fact]
  public void CapOverlays_RotationPerFace() {
    var w = new TestWorld();
    var overlays = Piece(w, BlockFacing.ALLFACES).CapOverlays();

    (int X, int Y) Pose(BlockFacing face) {
      var o = overlays.Single(o => o.Face == face);
      return (o.RotateXDeg, o.RotateYDeg);
    }

    Assert.Equal((0, 0), Pose(BlockFacing.NORTH));
    Assert.Equal((0, 180), Pose(BlockFacing.SOUTH));
    Assert.Equal((0, 90), Pose(BlockFacing.WEST));
    Assert.Equal((0, 270), Pose(BlockFacing.EAST));
    Assert.Equal((90, 0), Pose(BlockFacing.UP));
    Assert.Equal((270, 0), Pose(BlockFacing.DOWN));
  }

  // Fails when the mesh key ignores the caps, so a capped piece reuses the plain piece's mesh.
  [Fact]
  public void Box_MeshKeyChangesOnCap() {
    var w = new TestWorld();

    int plain = Piece(w, []).CapKey;
    int east = Piece(w, [BlockFacing.EAST]).CapKey;
    int eastUp = Piece(w, [BlockFacing.EAST, BlockFacing.UP]).CapKey;

    Assert.Equal(0, plain);
    Assert.NotEqual(plain, east);
    Assert.NotEqual(east, eastUp);
  }
}
