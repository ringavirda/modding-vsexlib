using ExpandedLib.Industry;
using ExpandedLib.Industry.Creative;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The live <c>power</c> step over a <see cref="TestWorld"/>: an "ns" mpshaft member at plot cell
/// (0, 0, 0) and the creative mpenergy source blocktype with its entity.</summary>
public class PowerStepTests {
  private const string Power =
    """{ "power": { "at": [0, 0, 0], "network": "mpshaft", "speed": 10, "torque": 500 } }""";

  private static LiveFixture Fixture() {
    var fixture = new LiveFixture();
    IndustryModule.RegisterNetworkTypes(fixture.Test.Networks);
    fixture.Test.Register(
      TestNetworkBlock.Create("mpshaft", "ns", 901, "test:shaft")
    );
    Block source = TestBlocks.Configure(
      new BlockCreativeMpEnergy { EntityClass = "creativempenergy" },
      BlockCreativeMpEnergy.Code,
      902
    );
    fixture
      .Test.Register(source)
      .RegisterBlockEntityFactory(
        "creativempenergy",
        () => new BlockEntityCreativeMpEnergy()
      );
    fixture.Run(
      Steps(
        """[{ "place": { "id": "s", "code": "test:shaft", "at": [0, 0, 0] } }]"""
      )
    );
    return fixture;
  }

  private static System.Collections.Generic.IReadOnlyList<SetupStep> Steps(
    string steps
  ) => LiveFixture.Case(steps).Model.Steps;

  private static BlockEntityCreativeMpEnergy SourceAt(
    LiveFixture fixture,
    int x,
    int y,
    int z
  ) =>
    Assert.IsType<BlockEntityCreativeMpEnergy>(
      fixture.Test.GetBlockEntity(new BlockPos(100 + x, 50 + y, 100 + z))
    );

  /// <summary>Fails when the source goes on a face the shaft has no crossing on, or on an occupied one
  /// (the open-face walk skips the stone on the north end and takes the south).</summary>
  [Fact]
  public void The_source_goes_on_the_first_open_crossing_face_with_the_values_set() {
    LiveFixture fixture = Fixture();
    fixture.Run(
      Steps(
        """[{ "place": { "code": "game:stone", "at": [0, 0, -1] } }, """
          + Power
          + "]"
      )
    );
    BlockEntityCreativeMpEnergy source = SourceAt(fixture, 0, 0, 1);
    Assert.Equal(500f, source.Torque);
    Assert.Equal(10f, source.Speed);
  }

  /// <summary>Fails when a second power step places another source or leaves the first at its old values.</summary>
  [Fact]
  public void A_second_power_step_sets_the_source_beside_the_cell() {
    LiveFixture fixture = Fixture();
    fixture.Run(
      Steps(
        $$"""[{{Power}}, { "power": { "at": [0, 0, 0], "network": "mpshaft", "speed": 4, "torque": 0 } }]"""
      )
    );
    BlockEntityCreativeMpEnergy source = SourceAt(fixture, 0, 0, -1);
    Assert.Equal(0f, source.Torque);
    Assert.Equal(4f, source.Speed);
  }

  /// <summary>Fails when the asked face is ignored for the first open one.</summary>
  [Fact]
  public void An_asked_face_takes_the_source() {
    LiveFixture fixture = Fixture();
    fixture.Run(
      Steps(
        """[{ "power": { "at": [0, 0, 0], "face": "s", "network": "mpshaft", "speed": 10, "torque": 500 } }]"""
      )
    );
    Assert.Equal(500f, SourceAt(fixture, 0, 0, 1).Torque);
  }

  /// <summary>Fails when an asked face with no crossing (the "ns" shaft's up face), or an asked face on a
  /// cell off every run, is accepted and an unconnected source placed.</summary>
  [Theory]
  [InlineData("u", 0, "has no crossing on face up")]
  [InlineData("s", 3, "not a member of an mpshaft run")]
  public void An_asked_face_is_tested_like_a_found_one(
    string face,
    int x,
    string reason
  ) {
    LiveFixture fixture = Fixture();
    fixture.Test.Register(
      new Block { BlockId = 6, Code = new AssetLocation("game:rock") }
    );
    fixture.Run(
      Steps("""[{ "place": { "code": "game:rock", "at": [3, 0, 0] } }]""")
    );
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(
        Steps(
          $$"""[{ "power": { "at": [{{x}}, 0, 0], "face": "{{face}}", "network": "mpshaft", "speed": 10, "torque": 500 } }]"""
        )
      )
    );
    Assert.Equal(Suspect.Scenario, failure.Suspect);
    Assert.Contains(reason, failure.Message);
    Assert.False(
      fixture.Test.GetBlockEntity(new BlockPos(100 + x, 51, 100))
        is BlockEntityCreativeMpEnergy
    );
  }

  /// <summary>Fails when a power step on a cell that is on no mpshaft run is carried out.</summary>
  [Fact]
  public void A_cell_off_the_run_fails_the_step_and_blames_the_scenario() {
    LiveFixture fixture = Fixture();
    fixture.Test.Register(
      new Block { BlockId = 6, Code = new AssetLocation("game:rock") }
    );
    fixture.Run(
      Steps("""[{ "place": { "code": "game:rock", "at": [3, 0, 0] } }]""")
    );
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(
        Steps(
          """[{ "power": { "at": [3, 0, 0], "network": "mpshaft", "speed": 10, "torque": 500 } }]"""
        )
      )
    );
    Assert.Equal(Suspect.Scenario, failure.Suspect);
    Assert.Contains("not a member of an mpshaft run", failure.Message);
  }

  /// <summary>Fails when a network other than mpshaft, a negative torque, a zero speed or a face that
  /// names no side passes the file's load.</summary>
  [Theory]
  [InlineData("mechanical", 10, 500, "s", "only an \"mpshaft\" run")]
  [InlineData("mpshaft", 10, -1, "s", "\"torque\" is a number, 0 or more")]
  [InlineData("mpshaft", 0, 500, "s", "\"speed\" is above 0 rad/s")]
  [InlineData("mpshaft", 10, 500, "x", "\"x\" is not a face")]
  public void A_power_step_the_run_cannot_take_is_refused(
    string network,
    int speed,
    int torque,
    string face,
    string reason
  ) {
    SetupFileException refused = Assert.Throws<SetupFileException>(() =>
      Steps(
        $$"""[{ "power": { "at": [0, 0, 0], "face": "{{face}}", "network": "{{network}}", "speed": {{speed}}, "torque": {{torque}} } }]"""
      )
    );
    Assert.Contains(reason, refused.Message);
  }

  /// <summary>Fails when the torque does not fall linearly from the stall value at rest to nothing at the
  /// free speed, or does not do so for a run turning the other way.</summary>
  [Theory]
  [InlineData(0f, 500f, -50f)]
  [InlineData(5f, 250f, -50f)]
  [InlineData(-5f, 250f, -50f)]
  [InlineData(10f, 0f, 0f)]
  [InlineData(25f, 0f, 0f)]
  public void The_drive_falls_linearly_to_nothing_at_the_free_speed(
    float speed,
    float torque,
    float slope
  ) {
    var source = new BlockEntityCreativeMpEnergy { Torque = 500f, Speed = 10f };
    Assert.Equal(torque, source.DriveTorque(speed), 3);
    Assert.Equal(slope, source.DriveSlope(speed), 3);
  }

  /// <summary>Fails when a speed of 0 or less is kept, which makes the drive infinite.</summary>
  [Fact]
  public void A_speed_of_zero_is_not_kept() {
    var source = new BlockEntityCreativeMpEnergy { Torque = 500f, Speed = 0f };
    Assert.True(float.IsFinite(source.DriveTorque(0f)));
    Assert.True(float.IsFinite(source.DriveSlope(0f)));
  }
}
