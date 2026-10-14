using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The <see cref="BlockFilledMegastructure"/> base reads the <c>fillerOffsets</c> attribute, whether it
/// came from JSON or a code-first def; a subclass narrows its cells through <c>ReservedCells</c> and
/// widens what counts as room through <c>HasRoom</c>.
/// </summary>
public class BlockFilledMegastructureTests {
  private sealed class FakeMega : BlockFilledMegastructure {
    public override int StructureAngle => 0;
  }

  private static FakeMega WithAttributes(string? json) =>
    new() {
      Attributes = json == null ? null! : new JsonObject(JToken.Parse(json)),
    };

  [Fact]
  public void FillerOffsets_reads_the_attribute_and_feeds_the_footprint_reader() {
    var block = WithAttributes(
      """{ "fillerOffsets": [{ "x": 1, "y": 0, "z": 2, "allowAttach": true }] }"""
    );

    var offsets = StructureFillers.ReadOffsets(block.FillerOffsets);
    var off = Assert.Single(offsets);
    Assert.Equal(new Vintagestory.API.MathTools.Vec3i(1, 0, 2), off.Offset);
    Assert.True(off.AllowAttach);
  }

  [Fact]
  public void FillerOffsets_of_a_block_without_the_attribute_reads_as_empty() {
    var block = WithAttributes("""{ "other": 1 }""");
    Assert.Empty(StructureFillers.ReadOffsets(block.FillerOffsets));
  }

  [Fact]
  public void FillerOffsets_is_null_safe_when_the_block_has_no_attributes() {
    var block = WithAttributes(null);
    Assert.Null(block.FillerOffsets);
    Assert.Empty(StructureFillers.ReadOffsets(block.FillerOffsets));
  }

  // Fails when IFillerHost.StructureAngle's default returns 0 for every block, or reads only the
  // side variant.
  [Theory]
  [InlineData("side", "e", 270)]
  [InlineData("side", "w", 90)]
  [InlineData("orientation", "s", 180)]
  [InlineData("type", "n", 0)]
  public void A_host_without_its_own_angle_turns_by_its_side_or_orientation(
    string group,
    string value,
    int expected
  ) {
    var block = new PlainHost();
    block.VariantStrict[group] = value;

    Assert.Equal(expected, ((IFillerHost)block).StructureAngle);
  }

  private sealed class PlainHost : Block, IFillerHost {
    public JsonObject? FillerOffsets => null;
  }

  #region Reserved cells and room

  private static readonly BlockPos At = new(10, 10, 10);
  private static readonly BlockPos Lower = At.UpCopy(1);
  private static readonly BlockPos Upper = At.UpCopy(2);

  private const string Column =
    """{ "fillerOffsets": [{ "x": 0, "y": 1, "z": 0 }, { "x": 0, "y": 2, "z": 0 }] }""";

  private static (TestWorld World, Block Filler) Stand(
    BlockFilledMegastructure mega
  ) {
    var world = new TestWorld();
    mega.Attributes = new JsonObject(JToken.Parse(Column));
    world.Register(
      TestBlocks.Configure(mega, "exlib:mega-n", 1, ("side", "n"))
    );
    Block filler = TestBlocks.Configure(
      new BlockStructureFiller(),
      StructureFillers.FillerCode.ToString(),
      2
    );
    world.Register(filler);
    return (world, filler);
  }

  private static Block Granite() =>
    TestBlocks.Configure(new Block(), "game:rock-granite", 3);

  private static bool CanPlace(TestWorld world, Block mega) {
    string failure = "";
    return mega.CanPlaceBlock(
      world.World,
      null!,
      new BlockSelection { Position = At.Copy(), Face = BlockFacing.UP },
      ref failure
    );
  }

  // Fails when ReadOffsets defaults allowAttach to true, FootprintCells drops the flag, or PlaceFillers
  // writes a constant to the block entity.
  [Fact]
  public void Only_the_cell_that_declares_allowAttach_accepts_attachments_once_placed() {
    var world = new TestWorld();
    var mega = new FakeMega {
      Attributes = new JsonObject(
        JToken.Parse(
          """{ "fillerOffsets": [{ "x": 1, "y": 0, "z": 0, "allowAttach": true }, { "x": 2, "y": 0, "z": 0 }] }"""
        )
      ),
    };
    world.Register(
      TestBlocks.Configure(mega, "exlib:mega-n", 1, ("side", "n"))
    );
    Block filler = TestBlocks.Configure(
      new BlockStructureFiller(),
      StructureFillers.FillerCode.ToString(),
      2
    );
    filler.SideSolid = new SmallBoolArray(0x3F);
    world.Register(filler);
    BlockPos opted = At.AddCopy(1, 0, 0);
    BlockPos plain = At.AddCopy(2, 0, 0);
    world.Place(opted, filler, new BlockEntityStructureFiller());
    world.Place(plain, filler, new BlockEntityStructureFiller());

    StructureFillers.PlaceFillers(
      world.World,
      At,
      StructureFillers.FootprintCells(mega, At, mega.StructureAngle)
    );

    Assert.True(
      filler.CanAttachBlockAt(world.Accessor, filler, opted, BlockFacing.UP)
    );
    Assert.False(
      filler.CanAttachBlockAt(world.Accessor, filler, plain, BlockFacing.UP)
    );
  }

  // Fails when CanPlaceBlock or OnBlockPlaced reads FootprintCells instead of ReservedCells.
  [Fact]
  public void A_cell_left_out_of_ReservedCells_is_neither_checked_nor_filled() {
    var mega = new SkipsLowerCell();
    var (world, filler) = Stand(mega);
    world.Place(Lower, Granite());

    Assert.True(CanPlace(world, mega));
    world.Accessor.SetBlock(mega.BlockId, At);
    mega.OnBlockPlaced(world.World, At);

    Assert.Equal("game:rock-granite", world.GetBlock(Lower).Code.ToString());
    Assert.Same(filler, world.GetBlock(Upper));
  }

  // Fails when OnBlockRemoved reads FootprintCells instead of ReservedCells.
  [Fact]
  public void A_cell_left_out_of_ReservedCells_keeps_its_filler_when_the_principal_goes() {
    var mega = new SkipsLowerCell();
    var (world, filler) = Stand(mega);
    world.Place(At, mega);
    world.Place(
      Lower,
      filler,
      new BlockEntityStructureFiller { Principal = At.Copy() }
    );
    world.Place(
      Upper,
      filler,
      new BlockEntityStructureFiller { Principal = At.Copy() }
    );

    mega.OnBlockRemoved(world.World, At);

    Assert.Same(filler, world.GetBlock(Lower));
    Assert.Same(world.Air, world.GetBlock(Upper));
  }

#if GAME_GE_1_22
  // Fails when the setup harness's footprint reads FootprintCells instead of ReservedCells.
  [Fact]
  public void The_setup_footprint_of_a_mega_block_is_its_ReservedCells()
  {
    var mega = new SkipsLowerCell();
    var (world, _) = Stand(mega);

    var cells = SetupStructureCheck.Footprint(world.World, mega, At, 0);

    Assert.Equal(Upper, Assert.Single(cells).Pos);
  }
#endif

  // Fails when CanPlaceBlock runs StructureFillers.CanPlace itself instead of asking HasRoom.
  [Fact]
  public void HasRoom_decides_the_footprint_refusal() {
    var mega = new CountsLowerCellAsRoom();
    var (world, _) = Stand(mega);
    world.Place(Lower, Granite());

    Assert.True(CanPlace(world, mega));

    world.Place(Upper, Granite());
    Assert.False(CanPlace(world, mega));
  }

  private sealed class SkipsLowerCell : BlockFilledMegastructure {
    protected override List<FillerCell> ReservedCells(
      IWorldAccessor world,
      BlockPos pos
    ) => [.. FootprintCells(pos).Where(c => !c.Pos.Equals(pos.UpCopy(1)))];
  }

  private sealed class CountsLowerCellAsRoom : BlockFilledMegastructure {
    protected override bool HasRoom(IWorldAccessor world, BlockPos pos) =>
      StructureFillers.CanPlace(
        world,
        ReservedCells(world, pos).Where(c => !c.Pos.Equals(pos.UpCopy(1)))
      );
  }

  #endregion
}
