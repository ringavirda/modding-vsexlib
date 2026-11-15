using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers <see cref="NetworkFaces.ClosesOpening"/>: a neighbour shuts an opening only by a whole
/// solid face toward it, and a structure cell never does.
/// </summary>
public class NetworkFacesTests {
  #region Fixtures

  private static readonly BlockPos Neighbour = new(0, 0, 0);

  private static bool Closes(Block block, BlockFacing faceTowardPipe) {
    var w = new TestWorld();
    w.Place(Neighbour, block);
    return NetworkFaces.ClosesOpening(w.Accessor, Neighbour, faceTowardPipe);
  }

  private static Block Solid(Block block, string code, int id) {
    TestBlocks.Configure(block, code, id);
    block.SideSolid = new SmallBoolArray(0x3F);
    return block;
  }

  private sealed class HostBlock : Block, IFillerHost {
    public JsonObject? FillerOffsets => null;
  }

  private sealed class ChiselledBlock : Block {
    public override bool SideIsSolid(
      IBlockAccessor blockAccess,
      BlockPos pos,
      int faceIndex
    ) => false;
  }

  #endregion

  // Fails when the predicate answers false for a solid face.
  [Fact]
  public void NetworkFaces_SolidFaceCloses() {
    Assert.True(Closes(Solid(new Block(), "test:full", 1), BlockFacing.DOWN));
  }

  // Fails when the type exclusion is dropped, so a structure cell, solid on every face, shuts an opening.
  [Fact]
  public void NetworkFaces_StructureCellNeverCloses() {
    Assert.False(
      Closes(
        Solid(new BlockStructureFiller(), "test:filler", 1),
        BlockFacing.UP
      )
    );
    Assert.False(
      Closes(
        Solid(new BlockFilledMegastructure(), "test:principal", 2),
        BlockFacing.UP
      )
    );
  }

  // Fails when the exclusion is narrowed to BlockFilledMegastructure, so a host of fillers that derives from another block shuts an opening.
  [Fact]
  public void NetworkFaces_FillerHostNeverCloses() {
    Assert.False(
      Closes(Solid(new HostBlock(), "test:host", 1), BlockFacing.UP)
    );
  }

  // Fails when the face index asked is the pipe's own, not the neighbour's face toward it.
  [Fact]
  public void NetworkFaces_AsksTheFaceTowardThePipe() {
    var w = new TestWorld();
    BlockPos pipe = new(0, 1, 0);
    BlockPos below = pipe.DownCopy();
    BlockPos beside = pipe.EastCopy();
    var topOnly = TestBlocks.Configure(new Block(), "test:topsolid", 1);
    topOnly.SideSolid = new SmallBoolArray(1 << BlockFacing.UP.Index);
    w.Place(below, topOnly);
    w.Place(beside, topOnly);

    Assert.True(NetworkFaces.ClosesOpening(w.Accessor, below, BlockFacing.UP));
    Assert.False(
      NetworkFaces.ClosesOpening(w.Accessor, beside, BlockFacing.WEST)
    );
  }

  // Fails when SideSolid[] is read in place of SideIsSolid, so a chiselled block shuts a face it does not cover.
  [Fact]
  public void NetworkFaces_ChiselledAnswersPerFace() {
    Assert.False(
      Closes(Solid(new ChiselledBlock(), "test:chiselled", 1), BlockFacing.UP)
    );
  }
}
