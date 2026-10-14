using System.IO;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>A scenario file as the live runner loads it, and each thing it refuses.</summary>
public class CaseTests {
  private const string Place =
    """[{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } }]""";

  private static string Refused(string steps, string extra = "{}") =>
    Assert
      .Throws<SetupFileException>(() => LiveFixture.Case(steps, extra))
      .Message;

  /// <summary>Fails when the default plot is not 16 blocks each way (V19).</summary>
  [Fact]
  public void A_file_without_keys_gets_a_16_block_plot_and_three_seconds_of_settle() {
    ScenarioCase scenario = LiveFixture.Case(Place);
    Assert.Equal([16, 16, 16], scenario.Plot);
    Assert.Equal(3, scenario.Settle);
    Assert.Empty(scenario.AllowErrors);
    Assert.Equal("exlib/case", scenario.Name);
  }

  /// <summary>Fails when the plot, settle or allowErrors keys are not read (V20).</summary>
  [Fact]
  public void Plot_settle_and_allow_errors_are_read_from_the_file() {
    ScenarioCase scenario = LiveFixture.Case(
      Place,
      """{ plot: [4, 5, 6], settle: 1.5, allowErrors: ["a+", "b"] }"""
    );
    Assert.Equal([4, 5, 6], scenario.Plot);
    Assert.Equal(1.5, scenario.Settle);
    Assert.Equal(2, scenario.AllowErrors.Count);
    Assert.Matches(scenario.AllowErrors[0], "xaax");
  }

  /// <summary>Fails when a plot size of 0 is accepted (V21).</summary>
  [Fact]
  public void A_plot_size_under_one_is_refused() =>
    Assert.Contains("at least 1", Refused(Place, "{ plot: [0, 4, 4] }"));

  /// <summary>Fails when a plot of other than three sizes is accepted (V22).</summary>
  [Fact]
  public void A_plot_of_two_sizes_is_refused() =>
    Assert.Contains("[x, y, z]", Refused(Place, "{ plot: [4, 4] }"));

  /// <summary>Fails when a negative settle is accepted (V23).</summary>
  [Fact]
  public void A_negative_settle_is_refused() =>
    Assert.Contains("not negative", Refused(Place, "{ settle: -0.5 }"));

  /// <summary>Fails when the named key is dropped from the refused list (V24 climate, V25
  /// variants, V26 during, V27 checks); each input is valid to the setup model, so only this
  /// guard rejects it.</summary>
  [Theory]
  [InlineData("climate", "{ temperature: -10, rainfall: 1 }")]
  [InlineData("variants", "{}")]
  [InlineData("during", "[]")]
  [InlineData("checks", "[]")]
  public void A_key_the_live_runner_does_not_read_yet_is_refused(
    string key,
    string value
  ) =>
    Assert.Contains(
      "not read by the live runner",
      Refused(Place, $"{{ \"{key}\": {value} }}")
    );

  /// <summary>Fails when tickOrders is dropped from the refused list (V33).</summary>
  [Fact]
  public void Tick_orders_are_refused() =>
    Assert.Contains(
      "not read by the live runner",
      Refused(
        """
        [
          { "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } },
          { "place": { "id": "b", "code": "game:stone", "at": [1, 0, 0] } }
        ]
        """,
        """{ tickOrders: ["a", "b"] }"""
      )
    );

  /// <summary>Fails when a layout is dropped from the refused list (V34).</summary>
  [Fact]
  public void A_layout_is_refused() =>
    Assert.Contains(
      "not read by the live runner",
      Refused(
        Place,
        """{ layout: { origin: [0, 0], legend: { "A": { code: "x:a" } }, layers: { "0": ["A"] } } }"""
      )
    );

  /// <summary>Fails when a recorded machine no longer refuses the file (V28).</summary>
  [Fact]
  public void A_recording_is_refused() =>
    Assert.Contains(
      "recording is not read",
      Refused(
        Place,
        """{ record: { machines: { a: { series: { state: "on" } } } } }"""
      )
    );

  /// <summary>Fails when a pack that starts from another stage is read, since the live runner has
  /// no stage files to start it from.</summary>
  [Fact]
  public void A_pack_after_another_stage_is_refused() =>
    Assert.Contains(
      "not read by the live runner",
      Refused(
        Place,
        """{ pack: { scope: "all", after: { "iiex/setups/play-stations": 1 } } }"""
      )
    );

  /// <summary>Fails when a pack without an <c>after</c> is refused, since a stage that starts from
  /// its gathered stacks alone must load.</summary>
  [Fact]
  public void A_pack_without_an_after_is_read() =>
    Assert.NotNull(
      LiveFixture.Case(Place, """{ pack: { scope: "all", gathered: { "game:stone": 1 } } }""").Model.Pack
    );

  /// <summary>Fails when a step the live runner cannot run is accepted (V29).</summary>
  [Fact]
  public void A_step_kind_the_live_runner_has_no_step_for_is_refused() =>
    Assert.Contains(
      "\"advance\" is not a step the live runner runs yet",
      Refused("""[{ "advance": { "hours": 1 } }]""")
    );

  /// <summary>Fails when a step's own refusal is not asked (V30).</summary>
  [Fact]
  public void A_place_by_player_is_refused_with_its_path() =>
    Assert.Contains(
      "steps[0]",
      Refused(
        """[{ "place": { "code": "game:stone", "at": [0, 0, 0], "by": "player" } }]"""
      )
    );

  /// <summary>Fails when only the outermost step is asked (V31).</summary>
  [Fact]
  public void A_step_under_a_repeat_is_refused_too() =>
    Assert.Contains(
      "\"advance\"",
      Refused(
        """[{ "repeat": { "times": 2, "steps": [{ "advance": { "hours": 1 } }] } }]"""
      )
    );

  /// <summary>Fails when a wait for a condition may run without a cap (V32).</summary>
  [Fact]
  public void A_wait_until_without_max_is_refused() =>
    Assert.Contains(
      "takes \"max\"",
      Refused(
        """[{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } }, { "wait": { "until": { "at": { "of": "a" }, "block": "game:stone" } } }]"""
      )
    );

  /// <summary>Fails when a committed sample stops loading or stops giving its verdict (V92): the
  /// files extools runs live share the setup model with the other readers.</summary>
  [Theory]
  [InlineData("sample", true, 0)]
  [InlineData("sample-2", true, 0)]
  [InlineData("sample-fails", false, 2)]
  public void A_committed_sample_loads_and_gives_its_verdict(
    string name,
    bool passes,
    int failedStep
  ) {
    string path = Path.Combine(RepoPaths.Root, "tests", "live", name + ".json");
    ScenarioCase scenario = ScenarioCase.Load(
      "exlib/" + name,
      File.ReadAllText(path),
      name + ".json"
    );
    var fixture = new LiveFixture();
    fixture.Test.Register(
      new Vintagestory.API.Common.Block {
        BlockId = 6,
        Code = new Vintagestory.API.Common.AssetLocation("game:rock-granite"),
      }
    );
    var run = new ScenarioCaseRun(scenario, fixture.Context);
    foreach (bool _ in run.Run()) {
      fixture.Clock += 0.5;
      if (fixture.Clock > 1000)
        break;
    }
    Assert.Equal(passes, run.Passed);
    Assert.Equal(failedStep, run.FailedStep);
  }
}
