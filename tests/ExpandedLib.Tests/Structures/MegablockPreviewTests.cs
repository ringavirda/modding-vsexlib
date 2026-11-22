using System.Linq;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The cells the megablock preview shows for a held block item: the footprint at the aimed cell and
/// turn, split into the cells that are clear and the cells the room check refuses.</summary>
public class MegablockPreviewTests {
  private const string Footprint =
    """[{ "x": 1, "y": 0, "z": 0 }, { "x": 0, "y": 1, "z": 0 }]""";

  private static readonly BlockPos Origin = ExOrientableRig.Pos;

  private static string[] Of(
    System.Collections.Generic.IEnumerable<BlockPos> cells
  ) =>
    [
      .. cells
        .Select(p => $"{p.X - Origin.X},{p.Y - Origin.Y},{p.Z - Origin.Z}")
        .Order(),
    ];

  // Fails when the footprint is read for the held variant instead of the variant the player's look picks:
  // looking west still reserves the cell to the east of a north-facing block.
  [Fact]
  public void The_footprint_is_the_one_of_the_turn_the_player_look_picks() {
    var rig = ExOrientableRig.Megablock(Footprint);

    var (north, _) = rig.PreviewLooking("north");
    var (west, _) = rig.PreviewLooking("west");

    Assert.Equal(["0,0,0", "0,1,0", "1,0,0"], Of(north));
    Assert.Equal(["0,0,-1", "0,0,0", "0,1,0"], Of(west));
  }

  // Fails when a cell holding a block the filler replaces is reported as refusing.
  [Fact]
  public void A_replaceable_cell_is_clear_and_a_solid_one_is_refused() {
    var rig = ExOrientableRig.Megablock(Footprint);
    rig.World.Place(
      Origin.AddCopy(1, 0, 0),
      TestBlocks.Configure(
        new Block { Replaceable = 6000 },
        "game:tallgrass",
        92
      )
    );
    rig.Occupy(Origin.UpCopy());

    var (clear, refuse) = rig.PreviewLooking("north");

    Assert.Equal(["0,0,0", "1,0,0"], Of(clear));
    Assert.Equal(["0,1,0"], Of(refuse));
  }

  // Fails when the aimed cell is used as the principal although a solid block stands there: the placement
  // goes across the aimed face.
  [Fact]
  public void A_solid_aimed_block_puts_the_footprint_across_the_face() {
    var rig = ExOrientableRig.Megablock(Footprint);
    rig.Occupy(Origin);

    var (clear, refuse) = rig.PreviewLooking("north");

    Assert.Equal(["0,1,0", "0,2,0", "1,1,0"], Of(clear));
    Assert.Empty(refuse);
  }

  [Fact]
  public void A_block_that_is_not_a_megablock_shows_nothing() {
    var rig = ExOrientableRig.WithVariants("exlib:plain", "side", ["n"]);

    var (clear, refuse) = rig.PreviewLooking("north");

    Assert.Empty(clear);
    Assert.Empty(refuse);
  }

  private sealed class NoRoom : BlockFilledMegastructure {
    protected override bool HasRoom(IWorldAccessor world, BlockPos pos) =>
      false;
  }

  // Fails when a room check that refuses with no cell to blame leaves the footprint green.
  [Fact]
  public void A_room_check_that_refuses_without_a_cell_turns_the_whole_footprint_red() {
    var rig = ExOrientableRig.WithVariants(
      "exlib:noroom",
      "side",
      ["n"],
      newBlock: () =>
        new NoRoom {
          Attributes = new Vintagestory.API.Datastructures.JsonObject(
            Newtonsoft.Json.Linq.JToken.Parse(
              $$"""{"fillerOffsets":{{Footprint}}}"""
            )
          ),
        }
    );
    rig.World.Register(
      TestBlocks.Configure(
        new BlockStructureFiller(),
        StructureFillers.FillerCode.ToString(),
        90
      )
    );

    var (clear, refuse) = rig.PreviewLooking("north");

    Assert.Empty(clear);
    Assert.Equal(["0,0,0", "0,1,0", "1,0,0"], Of(refuse));
  }
}
