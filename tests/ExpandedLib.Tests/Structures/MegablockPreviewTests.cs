using System.Linq;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
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

  // Fails when Footprint reads the turn from ExOrientable alone or PlacedBlock's default returns this: the
  // preview's cells differ from the placement's at e, s and w.
  [Theory]
  [InlineData("north", false)]
  [InlineData("east", false)]
  [InlineData("south", false)]
  [InlineData("west", false)]
  [InlineData("north", true)]
  [InlineData("east", true)]
  [InlineData("south", true)]
  [InlineData("west", true)]
  public void The_preview_fills_the_cells_placement_fills(
    string heading,
    bool network
  ) {
    var rig = network
      ? ExOrientableRig.NetworkMegablock(Footprint)
      : ExOrientableRig.Megablock(Footprint);

    var (clear, refuse) = rig.PreviewLooking(heading);
    Assert.Empty(refuse);
    Assert.True(rig.PlaceLooking(heading));

    Assert.Equal(Of(clear), rig.StandingCells());
  }

  // Fails when AimKey leaves the placed variant out: looking another way turns the footprint of a network-mode
  // block, and the preview would stay on the old cells.
  [Fact]
  public void The_aim_key_changes_with_the_placed_turn() {
    var rig = ExOrientableRig.NetworkMegablock(Footprint);

    string[] keys =
    [
      .. new[] { "north", "east", "south", "west" }.Select(h =>
        rig.AimKeyLooking(h)
      ),
    ];

    Assert.Equal(4, keys.Distinct().Count());
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

  // Fails when the aimed face is left out of the key: the footprint across the face moves with it, so the
  // preview would stay on the old cells while the player aims at the same block.
  [Fact]
  public void The_aim_key_changes_with_the_aimed_face() {
    var rig = ExOrientableRig.Megablock(Footprint);

    Assert.NotEqual(
      rig.AimKeyLooking("north", "up"),
      rig.AimKeyLooking("north", "east")
    );
  }

  // Fails when the player's turn token is left out of the key: looking another way turns the footprint.
  [Fact]
  public void The_aim_key_changes_with_the_turn_the_look_picks() {
    var rig = ExOrientableRig.Megablock(Footprint);

    Assert.NotEqual(rig.AimKeyLooking("north"), rig.AimKeyLooking("west"));
  }

  // Fails when Attach returns before registering the held tick for a block that already lists the behaviour.
  [Fact]
  public void A_block_that_lists_the_behaviour_still_gets_the_held_tick() {
    var world = new TestWorld();
    var block = new BlockFilledMegastructure();
    var listed = new CollectibleBehaviorMegablockPreview(block);
    block.CollectibleBehaviors = [listed];

    CollectibleBehaviorMegablockPreview.Attach(block, world.ClientApi);

    world
      .ClientApi.Event.Received(1)
      .RegisterGameTickListener(Arg.Any<System.Action<float>>(), 50, 0);
    Assert.Same(listed, Assert.Single(block.CollectibleBehaviors));
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
