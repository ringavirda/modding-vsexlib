using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A wrench on a filler cell: vanilla's wrench asks the clicked block for
/// <see cref="IWrenchOrientable"/>, and the filler answers with its principal's rotation.</summary>
public class FillerWrenchTests {
  private static readonly BlockPos PrincipalPos = new(4, 4, 4);
  private static readonly BlockPos EastCell = new(5, 4, 4);

  // Fails when the filler answers the wrench with nothing, or hands the principal the filler's
  // cell.
  [Fact]
  public void A_wrench_on_a_filler_turns_the_principal_at_its_own_cell() {
    var turnable = new Turnable();
    TestWorld world = NewWorld(turnable);

    Wrench(world, EastCell)!.Rotate(null!, Selection(EastCell), 1);

    Assert.Equal(PrincipalPos, turnable.TurnedAt);
    Assert.Equal(1, turnable.TurnedBy);
  }

  // Fails when a filler whose principal does not orient answers the wrench anyway.
  [Fact]
  public void A_filler_of_a_principal_that_does_not_orient_answers_no_wrench() {
    TestWorld world = NewWorld(new Block());

    Assert.Null(Wrench(world, EastCell));
  }

  // Fails when a filler that names no principal, or whose principal is gone, answers the wrench.
  [Fact]
  public void An_orphaned_filler_answers_no_wrench() {
    TestWorld world = NewWorld(new Turnable());
    BlockPos unlinked = EastCell.EastCopy();
    world.Place(unlinked, world.Filler, new BlockEntityStructureFiller());
    world.Accessor.SetBlock(0, PrincipalPos);

    Assert.Null(Wrench(world, unlinked));
    Assert.Null(Wrench(world, EastCell));
  }

  // Fails when the filler answers the wrench without a position to find its principal from.
  [Fact]
  public void A_filler_asked_without_a_position_answers_no_wrench() {
    TestWorld world = NewWorld(new Turnable());

    Assert.Null(
      world
        .GetBlock(EastCell)
        .GetInterface<IWrenchOrientable>(world.World, null!)
    );
  }

  // Fails when the filler answers another interface with its principal's wrench.
  [Fact]
  public void A_filler_answers_other_interfaces_as_itself() {
    TestWorld world = NewWorld(new Turnable());
    Block filler = world.GetBlock(EastCell);

    Assert.Same(
      filler,
      filler.GetInterface<BlockStructureFiller>(world.World, EastCell)
    );
  }

  private static TestWorld NewWorld(Block principal) {
    var world = new TestWorld();
    TestBlocks.Configure(principal, "test:principal", 71);
    world.Place(PrincipalPos, principal);
    world.PlaceFiller(EastCell, principal: PrincipalPos);
    return world;
  }

  private static IWrenchOrientable? Wrench(TestWorld world, BlockPos cell) =>
    world.GetBlock(cell).GetInterface<IWrenchOrientable>(world.World, cell);

  private static BlockSelection Selection(BlockPos cell) =>
    new() { Position = cell.Copy(), Face = BlockFacing.UP };

  private sealed class Turnable : Block, IWrenchOrientable {
    public BlockPos? TurnedAt;
    public int TurnedBy;

    public void Rotate(EntityAgent byEntity, BlockSelection blockSel, int dir) {
      TurnedAt = blockSel.Position.Copy();
      TurnedBy = dir;
    }
  }
}
