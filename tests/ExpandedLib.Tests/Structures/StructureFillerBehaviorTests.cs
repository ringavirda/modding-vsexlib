using System.Collections.Generic;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Behaviour-capable fillers: a <c>fillerOffsets</c> cell can declare behaviours the invisible
/// filler hosts on the principal's behalf, such as a mechanical-power intake at the cell where an axle
/// couples.</summary>
public class StructureFillerBehaviorTests {
  #region Parsing

  [Fact]
  public void A_cell_without_behaviors_parses_as_null() {
    var off = Assert.Single(ReadOffsets("[{ \"x\": 0, \"y\": 0, \"z\": 0 }]"));
    Assert.Null(off.Behaviors);
  }

  [Fact]
  public void A_behavior_parses_its_code_and_face() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": ["
          + "{ \"code\": \"exlib.BEBehaviorMPFillerPort\", \"face\": \"west\" } ] }]"
      )
    );

    var b = Assert.Single(off.Behaviors!);
    Assert.Equal("exlib.BEBehaviorMPFillerPort", b.Code);
    Assert.Equal(BlockFacing.WEST, b.ConnectorFace);
    Assert.Null(b.Properties);
  }

  [Fact]
  public void A_behavior_without_a_face_has_a_null_connector() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": [ { \"code\": \"test.X\" } ] }]"
      )
    );
    Assert.Null(Assert.Single(off.Behaviors!).ConnectorFace);
  }

  [Fact]
  public void A_behavior_keeps_its_declared_properties() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": [ { \"code\": \"test.X\", "
          + "\"properties\": { \"resistance\": 0.75 } } ] }]"
      )
    );
    var b = Assert.Single(off.Behaviors!);
    Assert.Equal(0.75f, b.Properties!["resistance"].AsFloat(), 3);
  }

  [Fact]
  public void Multiple_behaviors_on_one_cell_all_parse() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": ["
          + "{ \"code\": \"a\", \"face\": \"west\" }, { \"code\": \"b\", \"face\": \"east\" } ] }]"
      )
    );
    Assert.Equal(2, off.Behaviors!.Length);
    Assert.Equal("a", off.Behaviors[0].Code);
    Assert.Equal(BlockFacing.EAST, off.Behaviors[1].ConnectorFace);
  }

  [Fact]
  public void A_behavior_missing_a_code_is_skipped() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": [ { \"face\": \"west\" } ] }]"
      )
    );
    Assert.Null(off.Behaviors);
  }

  [Fact]
  public void An_empty_behaviors_array_is_null() {
    var off = Assert.Single(
      ReadOffsets("[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": [] }]")
    );
    Assert.Null(off.Behaviors);
  }

  #endregion

  #region Face rotation

  [Theory]
  [InlineData(0, "west")]
  [InlineData(90, "south")]
  [InlineData(180, "east")]
  [InlineData(270, "north")]
  public void The_connector_face_rotates_with_the_structure_angle(
    int angle,
    string expected
  ) {
    var host = new Host(
      Offsets(
        "[{ \"x\": 1, \"y\": 0, \"z\": 0, \"behaviors\": ["
          + "{ \"code\": \"exlib.BEBehaviorMPFillerPort\", \"face\": \"west\" } ] }]"
      )
    );

    var cell = Assert.Single(
      StructureFillers.FootprintCells(host, new BlockPos(0, 0, 0), angle)
    );
    Assert.Equal(
      BlockFacing.FromCode(expected),
      Assert.Single(cell.Behaviors!).ConnectorFace
    );
  }

  [Fact]
  public void A_faceless_behavior_passes_through_rotation_unchanged() {
    var host = new Host(
      Offsets(
        "[{ \"x\": 1, \"y\": 0, \"z\": 0, \"behaviors\": [ { \"code\": \"test.X\" } ] }]"
      )
    );
    var cell = Assert.Single(
      StructureFillers.FootprintCells(host, new BlockPos(0, 0, 0), 90)
    );
    var b = Assert.Single(cell.Behaviors!);
    Assert.Equal("test.X", b.Code);
    Assert.Null(b.ConnectorFace);
  }

  #endregion

  #region Serialization

  [Fact]
  public void Hosted_behaviors_round_trip_through_the_save_tree() {
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(3, 4, 5),
      HostedBehaviors =
      [
        new FillerBehavior(
          "exlib.BEBehaviorMPFillerPort",
          BlockFacing.EAST,
          Props("{ \"resistance\": 0.75 }")
        ),
      ],
    };

    var restored = RoundTrip(be);

    var b = Assert.Single(restored.HostedBehaviors!);
    Assert.Equal("exlib.BEBehaviorMPFillerPort", b.Code);
    Assert.Equal(BlockFacing.EAST, b.ConnectorFace);
    Assert.Equal(0.75f, b.Properties!["resistance"].AsFloat(), 3);
  }

  [Fact]
  public void A_faceless_propertyless_behavior_round_trips_as_such() {
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(1, 1, 1),
      HostedBehaviors = [new FillerBehavior("test.X", null, null)],
    };

    var b = Assert.Single(RoundTrip(be).HostedBehaviors!);
    Assert.Equal("test.X", b.Code);
    Assert.Null(b.ConnectorFace);
    Assert.Null(b.Properties);
  }

  [Fact]
  public void A_filler_with_no_hosted_behaviors_stays_null_across_the_tree() {
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(1, 2, 3),
    };
    Assert.Null(RoundTrip(be).HostedBehaviors);
  }

  #endregion

  #region Runtime hosting (scenario)

  [Fact]
  public void A_placed_filler_creates_configures_and_initialises_its_behaviors() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(2, 3, 4);
    var principal = new BlockPos(2, 3, 1);
    var be = new BlockEntityStructureFiller {
      Principal = principal,
      HostedBehaviors =
      [
        new FillerBehavior(
          "test.Tracking",
          BlockFacing.EAST,
          Props("{ \"k\": 7 }")
        ),
      ],
    };
    world.Place(pos, filler, be);
    world
      .Api.ClassRegistry.CreateBlockEntityBehavior(
        Arg.Any<BlockEntity>(),
        "test.Tracking"
      )
      .Returns(ci => new TrackingHostedBehavior(ci.Arg<BlockEntity>()));

    world.Initialize(be);

    var hosted = be.GetBehavior<TrackingHostedBehavior>();
    Assert.NotNull(hosted);
    Assert.True(hosted!.Initialized);
    Assert.Equal(principal, hosted.Principal);
    Assert.Equal(BlockFacing.EAST, hosted.Face);
    Assert.Equal(7, hosted.Props!["k"].AsInt());
  }

  [Fact]
  public void An_unknown_behavior_class_is_skipped_without_throwing() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(0, 0, -2),
      HostedBehaviors = [new FillerBehavior("test.DoesNotExist", null, null)],
    };
    world.Place(pos, filler, be);

    world.Initialize(be);

    Assert.Null(be.GetBehavior<TrackingHostedBehavior>());
  }

  [Fact]
  public void Hosted_behaviors_arriving_via_a_later_sync_update_are_created() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(5, 6, 7);
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(5, 6, 4),
    };
    world.Place(pos, filler, be);
    world.Initialize(be);
    Assert.Null(be.GetBehavior<TrackingHostedBehavior>());

    world
      .Api.ClassRegistry.CreateBlockEntityBehavior(
        Arg.Any<BlockEntity>(),
        "test.Tracking"
      )
      .Returns(ci => new TrackingHostedBehavior(ci.Arg<BlockEntity>()));

    var update = new TreeAttribute();
    new BlockEntityStructureFiller {
      Pos = pos,
      Block = filler,
      Principal = new BlockPos(5, 6, 4),
      HostedBehaviors =
      [
        new FillerBehavior("test.Tracking", BlockFacing.WEST, null),
      ],
    }.ToTreeAttributes(update);

    be.FromTreeAttributes(update, world.World);

    var hosted = be.GetBehavior<TrackingHostedBehavior>();
    Assert.NotNull(hosted);
    Assert.True(hosted!.Initialized);
    Assert.Equal(BlockFacing.WEST, hosted.Face);
  }

  #endregion

  #region Mechanical-power connector glue

#if GAME_GE_1_22
  [Fact]
  public void A_one_sided_port_lets_no_power_out()
  {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityStructureFiller
    {
      Principal = new BlockPos(0, 0, -2),
    };
    world.Place(pos, filler, be);
    world.Initialize(be);
    var through = new BEBehaviorMPFillerPort(be);
    through.ConfigureFromFiller(be.Principal, BlockFacing.WEST, null);
    var oneSided = new BEBehaviorMPFillerPort(be);
    oneSided.ConfigureFromFiller(
      be.Principal,
      BlockFacing.WEST,
      JsonObject.FromJson("{ \"through\": false }")
    );
    var entry = new MechPowerPath(BlockFacing.WEST, 1f, pos, false);

    Assert.Equal(2, through.GetMechPowerExits(entry).Length);
    Assert.Empty(oneSided.GetMechPowerExits(entry));
  }
#endif

  [Fact]
  public void A_one_sided_port_accepts_an_axle_on_its_own_face_only() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(0, 0, -2),
    };
    world.Place(pos, filler, be);
    world.Initialize(be);
    be.HostedBehaviors =
    [
      new FillerBehavior(
        "exlib.BEBehaviorMPFillerPort",
        BlockFacing.WEST,
        JsonObject.FromJson("{ \"through\": false }")
      ),
    ];
    var port = new BEBehaviorMPFillerPort(be);
    port.ConfigureFromFiller(
      be.Principal,
      BlockFacing.WEST,
      JsonObject.FromJson("{ \"through\": false }")
    );
    be.Behaviors.Add(port);

    Assert.False(port.Through);
    Assert.True(HasMechConnector(filler, world, pos, BlockFacing.WEST));
    Assert.False(HasMechConnector(filler, world, pos, BlockFacing.EAST));
    Assert.False(HasMechConnector(filler, world, pos, BlockFacing.NORTH));
  }

  [Fact]
  public void A_hosting_cell_accepts_an_axle_on_either_end_of_its_port_axis() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(0, 0, -2),
    };
    world.Place(pos, filler, be);
    world.Initialize(be);
    be.HostedBehaviors =
    [
      new FillerBehavior(
        "exlib.BEBehaviorMPFillerPort",
        BlockFacing.WEST,
        null
      ),
    ];
    be.Behaviors.Add(new BEBehaviorMPFillerPort(be));

    Assert.True(HasMechConnector(filler, world, pos, BlockFacing.WEST));
    Assert.True(HasMechConnector(filler, world, pos, BlockFacing.EAST));
    Assert.False(HasMechConnector(filler, world, pos, BlockFacing.NORTH));
    Assert.False(HasMechConnector(filler, world, pos, BlockFacing.SOUTH));
  }

  [Fact]
  public void A_cell_with_no_mp_behavior_is_not_a_connector() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(0, 0, -2),
      HostedBehaviors =
      [
        new FillerBehavior(
          "exlib.BEBehaviorMPFillerPort",
          BlockFacing.WEST,
          null
        ),
      ],
    };
    world.Place(pos, filler, be);
    world.Initialize(be);

    Assert.False(HasMechConnector(filler, world, pos, BlockFacing.WEST));
    Assert.Null(filler.GetNetwork(world.World, pos));
  }

  #endregion

  #region MP filler-port behaviour

  [Fact]
  public void The_port_takes_its_face_and_resistance_from_configuration() {
    var (world, filler) = NewWorld();
    var be = new BlockEntityStructureFiller();
    world.Place(new BlockPos(0, 0, 0), filler, be);

    var port = new BEBehaviorMPFillerPort(be);
    port.ConfigureFromFiller(
      null,
      BlockFacing.EAST,
      Props("{ \"resistance\": 0.9 }")
    );

    Assert.Equal(BlockFacing.EAST, port.PortFacing);
    Assert.Equal(0.9f, port.GetResistance(), 3);
  }

  [Fact]
  public void The_port_defaults_to_north_and_the_default_resistance() {
    var (world, filler) = NewWorld();
    var be = new BlockEntityStructureFiller();
    world.Place(new BlockPos(0, 0, 0), filler, be);

    var port = new BEBehaviorMPFillerPort(be);

    Assert.Equal(BlockFacing.NORTH, port.PortFacing);
    Assert.Equal(
      BEBehaviorMPFillerPort.DefaultResistance,
      port.GetResistance(),
      3
    );
  }

  /// <summary>The axle sign is vanilla's for the port's axis, negative along a horizontal
  /// one.</summary>
  [Theory]
  [InlineData("west", -1, 0, 0)]
  [InlineData("east", -1, 0, 0)]
  [InlineData("north", 0, 0, -1)]
  [InlineData("south", 0, 0, -1)]
  public void The_axle_sign_is_vanillas_for_the_ports_axis(
    string face,
    int x,
    int y,
    int z
  ) {
    var (world, filler) = NewWorld();
    var be = new BlockEntityStructureFiller();
    world.Place(new BlockPos(0, 0, 0), filler, be);
    var port = new BEBehaviorMPFillerPort(be);
    port.ConfigureFromFiller(null, BlockFacing.FromCode(face), null);

    port.SetOrientations();

    Assert.Equal(new[] { x, y, z }, port.AxisSign);
  }

  #endregion

  #region Removal (Block.OnBlockRemoved clears the footprint)

  [Fact]
  public void Replacing_the_principal_without_breaking_it_still_clears_the_footprint() {
    // TestWorld's fake accessor does not route SetBlock through Block.OnBlockRemoved (see
    // TestWorld.DoSetBlock); this drives the Block-level hook directly.
    var w = NewWorldWithMegastructure(
      out BlockPos principal,
      out BlockPos[] footprint
    );
    var principalBlock = (BlockFilledMegastructure)w.GetBlock(principal);

    principalBlock.OnBlockRemoved(w.World, principal);

    // GetBlockId is unstubbed on TestWorld's fake accessor; the check reads the stubbed GetBlock instead.
    foreach (var cell in footprint)
      Assert.Equal(0, w.Accessor.GetBlock(cell).BlockId);
  }

  #endregion

  #region Helpers

  private static (TestWorld world, BlockStructureFiller filler) NewWorld() {
    var world = new TestWorld();
    var filler = TestBlocks.Configure(
      new BlockStructureFiller(),
      "exlib:structurefiller",
      70
    );
    world.Register(filler);
    return (world, filler);
  }

  /// <summary>A principal placed with a two-cell footprint (east and west of it), each cell a filler
  /// BE already linked to the principal.</summary>
  private static TestWorld NewWorldWithMegastructure(
    out BlockPos principal,
    out BlockPos[] footprint
  ) {
    var world = new TestWorld();
    // RemoveFillers (and PlaceFillers) are server-only.
    world.World.Side.Returns(EnumAppSide.Server);
    var filler = TestBlocks.Configure(
      new BlockStructureFiller(),
      "exlib:structurefiller",
      70
    );
    world.Register(filler);

    var mega = TestBlocks.Configure(
      new FakeMega {
        Attributes = new JsonObject(
          JToken.Parse(
            "{ \"fillerOffsets\": ["
              + "{ \"x\": 1, \"y\": 0, \"z\": 0 }, { \"x\": -1, \"y\": 0, \"z\": 0 } ] }"
          )
        ),
      },
      "test:mega",
      71
    );
    world.Register(mega);

    principal = new BlockPos(4, 4, 4);
    world.Place(principal, mega);

    var cells = StructureFillers.FootprintCells(
      mega,
      principal,
      mega.StructureAngle
    );
    var positions = new List<BlockPos>();
    foreach (var cell in cells) {
      world.Place(
        cell.Pos,
        filler,
        new BlockEntityStructureFiller { Principal = principal.Copy() }
      );
      positions.Add(cell.Pos);
    }
    footprint = [.. positions];
    return world;
  }

  private static bool HasMechConnector(
    BlockStructureFiller filler,
    TestWorld world,
    BlockPos pos,
    BlockFacing face
  ) =>
#if GAME_GE_1_22
    filler.HasMechPowerConnectorAt(world.World, pos, face, null!);
#else
    filler.HasMechPowerConnectorAt(world.World, pos, face);
#endif

  private static JsonObject Offsets(string json) => new(JArray.Parse(json));

  private static JsonObject Props(string json) => new(JToken.Parse(json));

  private static List<FillerOffset> ReadOffsets(string json) =>
    StructureFillers.ReadOffsets(Offsets(json));

  private static BlockEntityStructureFiller RoundTrip(
    BlockEntityStructureFiller be
  ) {
    var world = new TestWorld();
    var block = TestBlocks.Configure(
      new BlockStructureFiller(),
      "exlib:structurefiller",
      70
    );
    var pos = new BlockPos(1, 2, 3);
    be.Pos = pos;
    be.Block = block;

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    var restored = new BlockEntityStructureFiller { Pos = pos, Block = block };
    restored.FromTreeAttributes(tree, world.World);
    return restored;
  }

  private sealed class Host(JsonObject? offsets) : IFillerHost {
    public JsonObject? FillerOffsets { get; } = offsets;
  }

  /// <summary>A minimal concrete <see cref="BlockFilledMegastructure"/> for exercising its placement
  /// and removal lifecycle without any of the game-content leaves' extra behaviour.</summary>
  private sealed class FakeMega : BlockFilledMegastructure {
    public override int StructureAngle => 0;
  }

  /// <summary>A non-MP hosted behaviour that records how the filler configured and initialised it.</summary>
  private sealed class TrackingHostedBehavior(BlockEntity be)
    : BlockEntityBehavior(be),
      IFillerHostedBehavior {
    public BlockPos? Principal;
    public BlockFacing? Face;
    public JsonObject? Props;
    public bool Initialized;

    public void ConfigureFromFiller(
      BlockPos? principal,
      BlockFacing? connectorFace,
      JsonObject? properties
    ) {
      Principal = principal;
      Face = connectorFace;
      Props = properties;
    }

    public override void Initialize(ICoreAPI api, JsonObject properties) {
      base.Initialize(api, properties);
      Initialized = true;
    }
  }

  #endregion
}
