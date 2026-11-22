using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Looking through a filler cell to its principal: the projection gesture's anchor and
/// <see cref="BlockEntityStructureFiller.PrincipalEntityAt{T}"/>.</summary>
public class PrincipalEntityAtTests {
  private static readonly BlockPos PrincipalPos = new(12000, 10, 0);
  private static readonly BlockPos FillerPos = new(12001, 10, 0);

  /// <summary>A principal <see cref="TestMegablock"/> and a filler cell east of it linked to it.</summary>
  private static (TestWorld World, TestMegablock Machine) Stand() {
    var world = new TestWorld();
    var machine = new TestMegablock();
    world.Place(
      PrincipalPos,
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      machine
    );
    world.Place(
      FillerPos,
      TestBlocks.Configure(new Block(), "exlib:structurefiller", 2),
      new BlockEntityStructureFiller { Principal = PrincipalPos.Copy() }
    );
    return (world, machine);
  }

  // Fails when ResolveIncompleteAnchor stops following the filler's Principal.
  [Fact]
  public void Projection_FromFillerReachesAnchor() {
    var (world, machine) = Stand();

    Assert.Same(
      machine,
      BlockBehaviorMultiblockStructure.ResolveIncompleteAnchor(
        world.World,
        FillerPos
      )
    );
  }

  // Fails when PrincipalEntityAt returns the filler's own entity cast to T (null) instead of the principal's.
  [Fact]
  public void PrincipalEntityAt_FillerGivesPrincipal() {
    var (world, machine) = Stand();

    Assert.Same(
      machine,
      BlockEntityStructureFiller.PrincipalEntityAt<TestMegablock>(
        world.World,
        FillerPos
      )
    );
  }

  // Fails when PrincipalEntityAt always takes the principal path, answering null for a plain cell.
  [Fact]
  public void PrincipalEntityAt_PlainCellGivesItsOwn() {
    var (world, machine) = Stand();

    Assert.Same(
      machine,
      BlockEntityStructureFiller.PrincipalEntityAt<TestMegablock>(
        world.World,
        PrincipalPos
      )
    );
    Assert.IsType<BlockEntityStructureFiller>(
      BlockEntityStructureFiller.PrincipalEntityAt<BlockEntityStructureFiller>(
        world.World,
        FillerPos
      )
    );
  }

  // Fails when the type test on the principal's entity is dropped (an unchecked cast throws or returns the wrong type).
  [Fact]
  public void PrincipalEntityAt_UnrelatedGivesNull() {
    var (world, _) = Stand();

    Assert.Null(
      BlockEntityStructureFiller.PrincipalEntityAt<TestPlainBlockEntity>(
        world.World,
        FillerPos
      )
    );
  }
}
