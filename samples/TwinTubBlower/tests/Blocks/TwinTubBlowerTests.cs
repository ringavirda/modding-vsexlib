using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using TwinTubBlower.BlockEntities;
using TwinTubBlower.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace TwinTubBlower.Tests;

/// <summary>
/// The twin-tub blower: a mechanically driven bellows that produces into the pipe main it stands in.
/// Covers the speed response, the production path and the footprint hosting the drive axle.
/// </summary>
public class TwinTubBlowerTests {
  #region Speed response

  [Theory]
  [InlineData(0f, 0f)]
  [InlineData(0.5f, 0f)] // at the minimum the bellows barely move
  [InlineData(1.0f, 0.5f)] // halfway between min and max
  [InlineData(1.5f, 1f)] // rated speed
  [InlineData(4f, 1f)] // over-driven: capped, never more than rated
  public void Output_scales_linearly_between_the_min_and_max_axle_speed(
    float speed,
    float expected
  ) {
    Assert.Equal(expected, BlockEntityTwinTubMPBlower.SpeedFraction(speed), 3);
  }

  [Fact]
  public void A_retuned_speed_band_moves_the_response_with_it() {
    float minOriginal = TwinTubBlowerValues.TwinTubBlowerMinSpeed;
    try {
      TwinTubBlowerValues.Edit(c => c.TwinTubBlowerMinSpeed = 1.0f);
      // 1.0 is half output under the default band and zero once the minimum is raised to 1.0.
      Assert.Equal(0f, BlockEntityTwinTubMPBlower.SpeedFraction(1.0f), 3);
    } finally {
      TwinTubBlowerValues.Edit(c => c.TwinTubBlowerMinSpeed = minOriginal);
    }
  }

  #endregion

  #region Production

  /// <summary>
  /// A blower standing as a node in its own single-cell pipe main. <c>ProduceAir</c> is driven with an
  /// axle speed directly: the live tick reads speed from a hosted MP filler port, which needs a filler
  /// block entity the headless world does not build. Constructed by default, as a working blower is;
  /// pass <paramref name="constructed"/> false for the premise of an unfinished one.
  /// </summary>
  private static (
    TestWorld world,
    PipeNetwork net,
    BlockEntityTwinTubMPBlower blower
  ) Rig(bool constructed = true) {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

    var blowerBlock = TestBlocks.Configure(
      new BlockTwinTubMPBlower(),
      "twintubblower:blower-twintubblower-n",
      120,
      ("type", "twintubblower"),
      ("orientation", "n")
    );
    blowerBlock.SetNetworkTypeForTest("twintubblower");
    blowerBlock.ApplyOrientationForTest("n");

    var blower = new BlockEntityTwinTubMPBlower();
    var pos = new BlockPos(0, 0, 0);
    world.Place(pos, blowerBlock, blower);
    world.Attach(blower);
    world.AddNode(pos, "pipe");
    ReflectionHelpers.SetProperty(
      blower,
      nameof(blower.NetworkSystem),
      world.Networks
    );
    if (constructed)
      RccFake.Complete(blower);

    return (world, (PipeNetwork)world.NetworkAt(pos)!, blower);
  }

  /// <summary>
  /// A blower placed at <paramref name="orientation"/>, wired into its own single-node pipe network.
  /// Unlike <see cref="Rig"/>, this leaves the axle and production paths alone - the outlet facts
  /// below only ever ask the block itself (<see cref="BlockTwinTubMPBlower.OutletCell"/>,
  /// <see cref="BlockTwinTubMPBlower.OutletFace"/>,
  /// <see cref="BlockTwinTubMPBlower.HasConnectorAt(BlockFacing)"/>) and the network graph.
  /// </summary>
  private static (
    TestWorld world,
    BlockPos principal,
    BlockTwinTubMPBlower block
  ) RigOriented(string orientation, int id) {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

    var block = TestBlocks.Configure(
      new BlockTwinTubMPBlower(),
      $"twintubblower:blower-twintubblower-{orientation}",
      id,
      ("type", "twintubblower"),
      ("orientation", orientation)
    );
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityTwinTubMPBlower();
    world.Place(pos, block, be);
    world.Attach(be);
    world.AddNode(pos, "pipe");

    return (world, pos, block);
  }

  /// <summary>A plain <see cref="BlockPipe"/> segment, real rather than the synthetic test node
  /// used elsewhere, with a single connector on <paramref name="facing"/>.</summary>
  private static BlockPipe PipeSegment(BlockFacing facing, int id) {
    var pipe = TestBlocks.Configure(
      new BlockPipe(),
      $"twintubblower:test-pipe-{id}",
      id,
      ("type", "straight"),
      ("orientation", facing.Code[0].ToString())
    );
    pipe.SetNetworkTypeForTest("pipe");
    pipe.ApplyOrientationForTest(facing.Code[0].ToString());
    return pipe;
  }

  [Fact]
  public void A_driven_blower_puts_air_into_its_own_network() {
    var (_, net, blower) = Rig();
    Assert.Equal(0f, net.State?.Volume ?? 0f);

    float produced = blower.ProduceAir(
      TwinTubBlowerValues.TwinTubBlowerMaxSpeed,
      1f
    );

    Assert.True(produced > 0f, "a driven blower should produce air");
    Assert.Equal("Air", net.State!.MediumType);
    Assert.True(net.State!.Volume > 0f);
  }

  [Fact]
  public void An_undriven_blower_leaves_the_main_alone() {
    var (world, net, blower) = Rig();
    net.TryProduceGas(20f, 20f, "Air", world.Accessor, maxOutputPressure: 2f);
    float before = net.State!.Volume;

    Assert.Equal(0f, blower.ProduceAir(0f, 1f));

    Assert.Equal(before, net.State!.Volume, 3);
  }

  [Fact]
  public void An_unconstructed_blower_produces_no_air() {
    var (_, net, blower) = Rig(constructed: false);

    float produced = blower.ProduceAir(
      TwinTubBlowerValues.TwinTubBlowerMaxSpeed,
      1f
    );

    Assert.Equal(0f, produced);
    Assert.Equal(0f, net.State?.Volume ?? 0f);
  }

  [Fact]
  public void A_half_speed_axle_delivers_half_the_air_of_a_rated_one() {
    var (_, fastNet, fast) = Rig();
    var (_, slowNet, slow) = Rig();
    float mid =
      (
        TwinTubBlowerValues.TwinTubBlowerMinSpeed
        + TwinTubBlowerValues.TwinTubBlowerMaxSpeed
      ) / 2f;

    float fullOutput = fast.ProduceAir(
      TwinTubBlowerValues.TwinTubBlowerMaxSpeed,
      1f
    );
    float halfOutput = slow.ProduceAir(mid, 1f);

    Assert.True(fullOutput > 0f && halfOutput > 0f);
    Assert.Equal(fullOutput / 2f, halfOutput, 1);
  }

  [Fact]
  public void The_blower_never_pushes_its_line_past_the_pressure_ceiling() {
    var (_, net, blower) = Rig();

    // Blow far longer than the single cell can hold, so the ceiling - not the volume - is what stops it.
    for (int i = 0; i < 60; i++)
      blower.ProduceAir(TwinTubBlowerValues.TwinTubBlowerMaxSpeed, 1f);

    Assert.True(
      net.State!.Pressure
        <= TwinTubBlowerValues.TwinTubBlowerMaxPressure + 0.001f,
      $"line reached {net.State!.Pressure} atm, ceiling is {TwinTubBlowerValues.TwinTubBlowerMaxPressure}"
    );
  }

  #endregion

  #region Footprint

  [Fact]
  public void The_footprint_hosts_a_mechanical_port_on_its_upper_rear_cell() {
    ExBlockDef def = BlockTwinTubMPBlower.Definitions("twintubblower").Single();
    var offsets = (JArray)def.ToJson()["attributes"]!["fillerOffsets"]!;

    // 1x2x3 minus the principal = 5 filler cells.
    Assert.Equal(5, offsets.Count);

    JToken port = offsets.Single(o =>
      (int)o["y"]! == 1 && (int)o["z"]! == 0 && (int)o["x"]! == 0
    );
    var behaviors = (JArray)port["behaviors"]!;
    Assert.Equal(
      "exlib.BEBehaviorMPFillerPort",
      (string)behaviors[0]!["code"]!
    );
    Assert.Equal("west", (string)behaviors[0]!["face"]!);
    Assert.True((bool)port["allowAttach"]!);
  }

  /// <summary>
  /// The -Z run: the blower's own connector faces north, and the two fillers ahead of it must
  /// forward that coupling rather than dead-end it, or nothing placed further out ever reaches the
  /// blower's network.
  /// </summary>
  [Fact]
  public void A_pipe_two_cells_out_joins_the_blowers_own_network() {
    var (world, _, _) = Rig();
    var principal = new BlockPos(0, 0, 0);

    world.PlaceFillerNode(
      new BlockPos(0, 0, -1),
      "pipe",
      "ns",
      principal: principal
    );
    world.PlaceFillerNode(
      new BlockPos(0, 0, -2),
      "pipe",
      "ns",
      principal: principal
    );
    world.PlaceNode(new BlockPos(0, 0, -3), "pipe", "ns");

    var net = world.NetworkAt(principal);
    Assert.NotNull(net);
    Assert.Equal(4, net!.Nodes.Count);
    Assert.Same(net, world.NetworkAt(new BlockPos(0, 0, -3)));
  }

  /// <summary>
  /// A real <see cref="BlockPipe"/> - not the synthetic test node above - standing against
  /// <see cref="BlockTwinTubMPBlower.OutletCell"/>'s <see cref="BlockTwinTubMPBlower.OutletFace"/>
  /// joins the blower's own network, at every placed orientation.
  /// </summary>
  [Theory]
  [InlineData("n", 200)]
  [InlineData("e", 210)]
  [InlineData("s", 220)]
  [InlineData("w", 230)]
  public void A_pipe_against_the_outlet_cells_outward_face_joins_the_blowers_own_network(
    string orientation,
    int id
  ) {
    var (world, principal, block) = RigOriented(orientation, id);
    BlockFacing outletFace = block.OutletFace;
    string through = $"{outletFace.Code[0]}{outletFace.Opposite.Code[0]}";

    world.PlaceFillerNode(
      principal.AddCopy(outletFace),
      "pipe",
      through,
      principal: principal
    );
    BlockPos outletCell = block.OutletCell(principal);
    world.PlaceFillerNode(outletCell, "pipe", through, principal: principal);

    BlockPos pipePos = outletCell.AddCopy(outletFace);
    var pipeBe = new BlockEntityPipe();
    world.Place(pipePos, PipeSegment(outletFace.Opposite, id + 1), pipeBe);
    world.Attach(pipeBe);
    world.AddNode(pipePos, "pipe");

    var net = world.NetworkAt(principal);
    Assert.NotNull(net);
    Assert.Equal(4, net!.Nodes.Count);
    Assert.Same(net, world.NetworkAt(pipePos));
  }

  /// <summary>
  /// A pipe standing directly against the principal, on any face but the outlet, never joins: the
  /// network only reaches this blower two cells out, through the outlet filler. Before the block's
  /// own <see cref="BlockTwinTubMPBlower.HasConnectorAt(BlockFacing)"/> was narrowed to
  /// <see cref="BlockTwinTubMPBlower.OutletFace"/>, the base class read the placed "orientation"
  /// variant letter as a connector code, which agreed with the outlet face for n/s but named the
  /// opposite face for e/w - so a pipe against the principal's east face joined an "e"-placed blower
  /// and one against west joined a "w"-placed one.
  /// </summary>
  [Theory]
  [InlineData("n", 300)]
  [InlineData("e", 310)]
  [InlineData("s", 320)]
  [InlineData("w", 330)]
  public void A_pipe_against_the_principal_itself_never_joins(
    string orientation,
    int baseId
  ) {
    int id = baseId;
    foreach (BlockFacing face in BlockFacing.HORIZONTALS) {
      var (world, principal, block) = RigOriented(orientation, id++);
      if (face == block.OutletFace)
        continue;

      BlockPos pipePos = principal.AddCopy(face);
      var pipeBe = new BlockEntityPipe();
      world.Place(pipePos, PipeSegment(face.Opposite, id++), pipeBe);
      world.Attach(pipeBe);
      world.AddNode(pipePos, "pipe");

      var net = world.NetworkAt(principal);
      Assert.NotNull(net);
      Assert.Equal(1, net!.Nodes.Count); // the principal alone - the pipe formed its own, separate network
      Assert.NotSame(net, world.NetworkAt(pipePos));
    }
  }

  [Theory]
  [InlineData("n", 0)]
  [InlineData("e", 270)]
  [InlineData("s", 180)]
  [InlineData("w", 90)]
  public void The_structure_angle_follows_the_orientation_variant(
    string orientation,
    int expected
  ) {
    var block = TestBlocks.Configure(
      new BlockTwinTubMPBlower(),
      $"twintubblower:blower-twintubblower-{orientation}",
      121,
      ("type", "twintubblower"),
      ("orientation", orientation)
    );
    Assert.Equal(expected, block.StructureAngle);
  }

  /// <summary>
  /// The body extends away from the player, not toward them: the pass-through fillers sit at
  /// principal + 1 and +2 cells along the facing the player was given at placement, the outlet
  /// faces that same direction, and the MP port cell - fixed above the principal regardless of
  /// orientation - carries its connector on the player's left hand. Pins the east/west repair:
  /// before it, <see cref="BlockTwinTubMPBlower.StructureAngle"/> used e 90 / w 270 and turned
  /// those two placements the wrong way.
  /// </summary>
  [Theory]
  [InlineData("n", 400, 0, 0, -1, 0, 0, -2, "n", "w")]
  [InlineData("e", 410, 1, 0, 0, 2, 0, 0, "e", "n")]
  [InlineData("s", 420, 0, 0, 1, 0, 0, 2, "s", "e")]
  [InlineData("w", 430, -1, 0, 0, -2, 0, 0, "w", "s")]
  public void The_body_extends_away_from_the_player_and_the_port_sits_on_their_left_hand(
    string orientation,
    int id,
    int nearX,
    int nearY,
    int nearZ,
    int farX,
    int farY,
    int farZ,
    string outletSide,
    string portSide
  ) {
    var (_, principal, block) = RigOriented(orientation, id);
    // Footprint read off the shipped def, as the burden maker's placement suite does.
    block.Attributes = new JsonObject(
      BlockTwinTubMPBlower.Definitions("twintubblower").First().ToJson()[
        "attributes"
      ]!
    );

    List<FillerCell> cells = StructureFillers.FootprintCells(
      block,
      principal,
      block.StructureAngle
    );

    Assert.Contains(
      cells,
      c => c.Pos.Equals(principal.AddCopy(nearX, nearY, nearZ))
    );
    Assert.Contains(
      cells,
      c => c.Pos.Equals(principal.AddCopy(farX, farY, farZ))
    );

    Assert.Equal(
      outletSide,
      ExOrientation.TokenOf(block.OutletFace, asLetter: true)
    );

    FillerCell port = cells.Single(c =>
      c.Pos.Equals(principal.AddCopy(0, 1, 0))
    );
    BlockFacing portFace = port.Behaviors!.Single().ConnectorFace!;
    Assert.Equal(portSide, ExOrientation.TokenOf(portFace, asLetter: true));
  }

  #endregion

  #region Orientation lock

  [Fact]
  public void The_axle_speed_round_trips_through_the_tree_attributes() {
    var (_, _, blower) = Rig();
    ReflectionHelpers.SetField(blower, "_lastSpeed", 3.5f);
    var tree = new TreeAttribute();

    blower.ToTreeAttributes(tree);
    var loaded = new BlockEntityTwinTubMPBlower();
    loaded.FromTreeAttributes(tree, blower.Api.World);

    Assert.Equal(3.5f, ReflectionHelpers.GetField(loaded, "_lastSpeed"));
  }

  [Fact]
  public void The_blower_never_offers_the_wrench_rotate_hint() {
    var (world, _, blower) = Rig();
    var block = (BlockTwinTubMPBlower)blower.Block;
    var selection = new BlockSelection {
      Position = blower.Pos.Copy(),
      Face = BlockFacing.UP,
    };

    WorldInteraction[] help = block.GetPlacedBlockInteractionHelp(
      world.World,
      selection,
      world.Player().Player
    );

    Assert.DoesNotContain(
      help,
      w => w.ActionLangCode == "exlib:blockhelp-rotate"
    );
  }

  /// <summary>
  /// <c>RecalculateAndSyncOrientations</c> is the neighbour scan's swap path
  /// (<c>BlockNetworkNode.OnNeighbourBlockChange</c>); the blower's override answers nothing rather
  /// than re-picking a token off the surrounding topology.
  /// </summary>
  [Fact]
  public void A_neighbour_scan_leaves_an_s_blower_s() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var pos = new BlockPos(0, 0, 0);

    var blowerBlock = TestBlocks.Configure(
      new BlockTwinTubMPBlower(),
      "twintubblower:blower-twintubblower-s",
      123,
      ("type", "twintubblower"),
      ("orientation", "s")
    );
    blowerBlock.SetNetworkTypeForTest("twintubblower");
    blowerBlock.ApplyOrientationForTest("s");

    var blower = new BlockEntityTwinTubMPBlower();
    world.Place(pos, blowerBlock, blower);
    world.Attach(blower);

    ((BlockNetworkNode)world.GetBlock(pos)).RecalculateAndSyncOrientations(
      world.World,
      pos
    );

    Assert.Equal(
      "twintubblower:blower-twintubblower-s",
      world.GetBlock(pos).Code?.ToString()
    );
  }

  #endregion
}
