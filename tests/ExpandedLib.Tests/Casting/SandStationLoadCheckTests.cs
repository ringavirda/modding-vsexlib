using System;
using System.Linq;
using ExpandedLib.Industry;
using ExpandedLib.Industry.Casting;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests.Casting;

/// <summary>The load check of sand station blocks: a station the catalogue lacks and a block with no molten cell are
/// logged at load. Shares the process-wide <see cref="CastingStationRegistry.Shared"/>.</summary>
[Collection("WorldState")]
public class SandStationLoadCheckTests : IDisposable {
  public void Dispose() => CastingStationRegistry.ResetForWorld();

  private static IClassRegistryAPI Classes() {
    var classes = Substitute.For<IClassRegistryAPI>();
    classes
      .GetBlockEntity("sandstation")
      .Returns(typeof(BlockEntitySandStation));
    classes.GetBlockEntity("plain").Returns(typeof(BlockEntity));
    classes
      .GetBlockEntityBehaviorClass("moltencell")
      .Returns(typeof(BEBehaviorMoltenCell));
    return classes;
  }

  private sealed class FootprintBlock : Block, IFillerHost {
    public JsonObject? FillerOffsets =>
      new(JToken.Parse("""[{ "x": 0, "y": 0, "z": -4 }]"""));
  }

  private static Block StationBlock(
    string code,
    string? station,
    bool cell = true,
    string entity = "sandstation",
    string? launderFaces = null,
    Block? on = null
  ) {
    var block = TestBlocks.Configure(on ?? new Block(), code, 200);
    block.EntityClass = entity;
    block.Attributes = new JsonObject(
      JToken.Parse(
        station == null
          ? "{}"
          : $$"""{ "sandStation": { "station": "{{station}}"{{(
            launderFaces == null ? "" : $", \"launderFaces\": {launderFaces}"
          )}} } }"""
      )
    );
    block.BlockEntityBehaviors = cell
      ? [new BlockEntityBehaviorType { Name = "moltencell" }]
      : [];
    return block;
  }

  private sealed class AdjacentFootprintBlock : Block, IFillerHost {
    public JsonObject? FillerOffsets =>
      new(JToken.Parse("""[{ "x": 0, "y": 0, "z": -1 }]"""));
  }

  private static void Register() {
    CastingStationRegistry.ResetForWorld();
    CastingStationLoader.Load(
      [
        (
          "test:config/castingstations/check.json",
          """
          { "schema": 1, "stations": [
            { "code": "boxflask", "moulds": ["open"],
              "sand": { "base": "test:a", "half": "test:b", "item": "game:sand-*" } } ] }
          """
        ),
      ],
      CastingStationRegistry.Shared
    );
  }

  // Fails when the registry lookup is skipped: the unregistered station passes unlogged.
  [Fact]
  public void A_block_naming_an_unregistered_station_is_one_finding() {
    Register();

    string finding = Assert.Single(
      SandStationCheck.Run([StationBlock("test:s", "nosuch")], Classes())
    );

    Assert.Contains("test:s", finding);
    Assert.Contains("nosuch", finding);
  }

  // Fails when a registered station is judged unregistered or the check logs a block that is whole.
  [Fact]
  public void A_valid_station_block_gives_no_finding() {
    Register();

    Assert.Empty(
      SandStationCheck.Run([StationBlock("test:s", "boxflask")], Classes())
    );
  }

  // Fails when the missing station attribute is not reported.
  [Fact]
  public void A_block_naming_no_station_is_one_finding() {
    Register();

    string finding = Assert.Single(
      SandStationCheck.Run([StationBlock("test:s", null)], Classes())
    );

    Assert.Contains("names no station", finding);
  }

  // Fails when the molten cell test is removed.
  [Fact]
  public void A_block_without_a_molten_cell_is_one_finding() {
    Register();

    string finding = Assert.Single(
      SandStationCheck.Run(
        [StationBlock("test:s", "boxflask", cell: false)],
        Classes()
      )
    );

    Assert.Contains("BEBehaviorMoltenCell", finding);
  }

  // Fails when a face on a cell outside the footprint passes the check.
  [Fact]
  public void A_launder_face_outside_the_footprint_is_one_finding() {
    Register();

    string finding = Assert.Single(
      SandStationCheck.Run(
        [
          StationBlock(
            "test:s",
            "boxflask",
            launderFaces: """[{ "z": -3, "turns": 2 }]""",
            on: new FootprintBlock()
          ),
        ],
        Classes()
      )
    );

    Assert.Contains("test:s", finding);
    Assert.Contains("footprint", finding);
  }

  // Fails when a face on a declared footprint cell is refused.
  [Fact]
  public void A_launder_face_on_a_footprint_cell_gives_no_finding() {
    Register();

    Assert.Empty(
      SandStationCheck.Run(
        [
          StationBlock(
            "test:s",
            "boxflask",
            launderFaces: """[{ "z": -4 }]""",
            on: new FootprintBlock()
          ),
        ],
        Classes()
      )
    );
  }

  // Fails when a face looking at a footprint cell passes the check: the south facing of the cell north of the principal lands on the principal.
  [Fact]
  public void A_launder_face_facing_into_the_footprint_is_one_finding() {
    Register();

    string finding = Assert.Single(
      SandStationCheck.Run(
        [
          StationBlock(
            "test:s",
            "boxflask",
            launderFaces: """[{ "z": -1, "turns": 2 }]""",
            on: new AdjacentFootprintBlock()
          ),
        ],
        Classes()
      )
    );

    Assert.Contains("own footprint", finding);
  }

  // Fails when a face declared twice, or on the principal's own face, passes the check.
  [Theory]
  [InlineData("""[{ "z": -4, "turns": 1 }, { "z": -4, "turns": 1 }]""")]
  [InlineData("""[{ "turns": 4 }]""")]
  public void A_launder_face_declared_twice_is_one_finding(string faces) {
    Register();

    string finding = Assert.Single(
      SandStationCheck.Run(
        [
          StationBlock(
            "test:s",
            "boxflask",
            launderFaces: faces,
            on: new FootprintBlock()
          ),
        ],
        Classes()
      )
    );

    Assert.Contains("declared twice", finding);
  }

  // Fails when the entity class test is removed: a block on another entity class is judged as a station.
  [Fact]
  public void A_block_on_another_entity_class_is_not_judged() {
    Register();

    Assert.Empty(
      SandStationCheck.Run(
        [StationBlock("test:s", "nosuch", cell: false, entity: "plain")],
        Classes()
      )
    );
  }

  // Fails when AssetsFinalize does not run the check, or logs it other than once per block.
  [Fact]
  public void The_module_logs_the_finding_at_load() {
    Register();
    var world = new TestWorld();
    Block block = StationBlock("test:s", "nosuch");
    world.Register(block);
    IClassRegistryAPI classes = Classes();
    world.Api.ClassRegistry.Returns(classes);

    world.Log.Expect(EnumLogType.Error, "invalid sand station block test:s");

    new IndustryModule().AssetsFinalize(world.Api);

    Assert.Single(
      world.Log.Errors,
      e => e.Contains("invalid sand station block test:s")
    );
  }
}
