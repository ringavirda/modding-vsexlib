using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A wrench on a filler cell: vanilla's wrench asks the clicked block for
/// <see cref="IWrenchOrientable"/>, and the filler answers with its principal's rotation; a
/// firestarter or torch asks for <see cref="IIgnitable"/> and gets the principal's.</summary>
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

  // Fails when the filler answers no ignition, or hands the principal the filler's cell.
  [Fact]
  public void A_firestarter_on_a_filler_lights_the_principal_at_its_own_cell() {
    var burner = new Burner();
    TestWorld world = NewWorld(burner);

    IIgnitable? ignitable = world
      .GetBlock(EastCell)
      .GetInterface<IIgnitable>(world.World, EastCell);
    EnumIgniteState state = ignitable!.OnTryIgniteBlock(null!, EastCell, 2f);
    EnumHandling handling = EnumHandling.PassThrough;
    ignitable.OnTryIgniteBlockOver(null!, EastCell, 2f, ref handling);

    Assert.Equal(EnumIgniteState.IgniteNow, state);
    Assert.Equal(PrincipalPos, burner.TriedAt);
    Assert.Equal(PrincipalPos, burner.OverAt);
  }

  // Fails when a filler of a principal that is not ignitable answers ignition anyway.
  [Fact]
  public void A_filler_of_a_principal_that_is_not_ignitable_answers_no_ignition() {
    TestWorld world = NewWorld(new Block());

    Assert.Null(
      world.GetBlock(EastCell).GetInterface<IIgnitable>(world.World, EastCell)
    );
  }

  // Fails when a filler that names no principal, or whose principal is gone, answers ignition.
  [Fact]
  public void An_orphaned_filler_answers_no_ignition() {
    TestWorld world = NewWorld(new Burner());
    BlockPos unlinked = EastCell.EastCopy();
    world.Place(unlinked, world.Filler, new BlockEntityStructureFiller());
    world.Accessor.SetBlock(0, PrincipalPos);

    Assert.Null(
      world.GetBlock(unlinked).GetInterface<IIgnitable>(world.World, unlinked)
    );
    Assert.Null(
      world.GetBlock(EastCell).GetInterface<IIgnitable>(world.World, EastCell)
    );
  }

#if GAME_GE_1_22
  // Fails when the filler answers no bellows receiver, or hands the principal another cell or face
  // than the one the bellows blew on.
  [Fact]
  public void A_bellows_on_a_filler_blows_the_principal_with_the_filler_cell_and_face()
  {
    var furnace = new Furnace();
    TestWorld world = NewWorld(furnace);

    world
      .GetBlock(EastCell)
      .GetInterface<IBellowsAirReceiver>(world.World, EastCell)!
      .BlowAirInto(world.World, EastCell, 0.2f, BlockFacing.WEST);

    Assert.Equal(EastCell, furnace.BlownAt);
    Assert.Equal(BlockFacing.WEST, furnace.BlownFrom);
  }

  // Fails when a filler of a principal that is no bellows receiver answers one anyway.
  [Fact]
  public void A_filler_of_a_principal_that_is_no_receiver_answers_no_bellows()
  {
    TestWorld world = NewWorld(new Block());

    Assert.Null(
      world
        .GetBlock(EastCell)
        .GetInterface<IBellowsAirReceiver>(world.World, EastCell)
    );
  }

  // Fails when a filler that names no principal, or whose principal is gone, answers a bellows.
  [Fact]
  public void An_orphaned_filler_answers_no_bellows()
  {
    TestWorld world = NewWorld(new Furnace());
    BlockPos unlinked = EastCell.EastCopy();
    world.Place(unlinked, world.Filler, new BlockEntityStructureFiller());
    world.Accessor.SetBlock(0, PrincipalPos);

    Assert.Null(
      world
        .GetBlock(unlinked)
        .GetInterface<IBellowsAirReceiver>(world.World, unlinked)
    );
    Assert.Null(
      world
        .GetBlock(EastCell)
        .GetInterface<IBellowsAirReceiver>(world.World, EastCell)
    );
  }

  private sealed class Furnace : Block, IBellowsAirReceiver
  {
    public BlockPos? BlownAt;
    public BlockFacing? BlownFrom;

    public void BlowAirInto(
      IWorldAccessor world,
      BlockPos pos,
      float amount,
      BlockFacing direction
    )
    {
      BlownAt = pos.Copy();
      BlownFrom = direction;
    }
  }
#endif

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

  private sealed class Burner : Block, IIgnitable {
    public BlockPos? TriedAt;
    public BlockPos? OverAt;

    public EnumIgniteState OnTryIgniteBlock(
      EntityAgent byEntity,
      BlockPos pos,
      float secondsIgniting
    ) {
      TriedAt = pos.Copy();
      return EnumIgniteState.IgniteNow;
    }

    public void OnTryIgniteBlockOver(
      EntityAgent byEntity,
      BlockPos pos,
      float secondsIgniting,
      ref EnumHandling handling
    ) => OverAt = pos.Copy();

    public EnumIgniteState OnTryIgniteStack(
      EntityAgent byEntity,
      BlockPos pos,
      ItemSlot slot,
      float secondsIgniting
    ) => EnumIgniteState.NotIgnitable;
  }

  private sealed class Turnable : Block, IWrenchOrientable {
    public BlockPos? TurnedAt;
    public int TurnedBy;

    public void Rotate(EntityAgent byEntity, BlockSelection blockSel, int dir) {
      TurnedAt = blockSel.Position.Copy();
      TurnedBy = dir;
    }
  }
}
