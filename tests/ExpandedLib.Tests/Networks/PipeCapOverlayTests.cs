using System.Linq;
using System.Reflection;
using ExpandedLib.Assembling;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The cap overlays a pipe piece draws on its capped ends: which faces, in what pose, and the mesh key that follows them.</summary>
public class PipeCapOverlayTests {
  private static BlockEntityPipe Piece(
    TestWorld w,
    BlockFacing[] capped,
    string orientation = "nsweud",
    string type = "junction"
  ) {
    var pos = new BlockPos(5, 0, 5);
    var block = TestBlocks.Configure(
      new BlockPipe(),
      $"test:pipe-cast-{type}-{orientation}",
      97,
      ("type", type),
      ("tier", "cast"),
      ("orientation", orientation)
    );
    block.SetNetworkTypeForTest(type);
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

    Assert.Empty(Piece(w, [], "ns", "straight").CapOverlays());
    Assert.Equal(
      BlockFacing.NORTH,
      Assert
        .Single(Piece(w, [BlockFacing.NORTH], "ns", "straight").CapOverlays())
        .Face
    );
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

  // Fails when the client does not redraw the piece once its caps change, or redraws it on every sync.
  [Fact]
  public void Box_RedrawsOnceWhenCapsChange() {
    var w = new TestWorld();
    var be = Piece(w, [BlockFacing.EAST]);
    w.Api.Side.Returns(EnumAppSide.Client);

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    be.FromTreeAttributes(tree, w.World);
    be.FromTreeAttributes(tree, w.World);

    w.Accessor.Received(1).MarkBlockDirty(be.Pos);
  }

  // Fails when the cap meshes are not kept per capped-face set: rebuilt for a set already seen, or shared across sets.
  [Fact]
  public void Box_CapMeshesBuiltOncePerFaceSet() {
    var w = new TestWorld();
    var pipe = (BlockPipe)Piece(w, []).Block;
    var capi = Substitute.For<ICoreClientAPI>();
    var asset = Substitute.For<IAsset>();
    asset.ToObject<Shape>().Returns(new Shape());
    capi.Assets.TryGet(Arg.Any<AssetLocation>(), Arg.Any<bool>())
      .Returns(asset);
    var tesselator = Substitute.For<ITesselatorAPI>();

    int Builds() =>
      tesselator
        .ReceivedCalls()
        .Count(c => c.GetMethodInfo().Name == "TesselateShape");

    Assert.Empty(pipe.CapMeshes(capi, tesselator, []));
    Assert.Equal(0, Builds());
    BlockFacing[] east = [BlockFacing.EAST];
    Assert.Equal(1, pipe.CapMeshes(capi, tesselator, east).Length);
    Assert.Equal(1, pipe.CapMeshes(capi, tesselator, east).Length);
    Assert.Equal(1, Builds());
    Assert.Equal(
      2,
      pipe.CapMeshes(
        capi,
        tesselator,
        [BlockFacing.EAST, BlockFacing.UP]
      ).Length
    );
    Assert.Equal(3, Builds());
  }
}
