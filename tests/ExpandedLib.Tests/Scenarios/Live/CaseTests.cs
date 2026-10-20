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

  /// <summary>Fails when climate is dropped from the refused list (V24); the input is valid to
  /// the setup model, so only this guard rejects it.</summary>
  [Theory]
  [InlineData("climate", "{ temperature: -10, rainfall: 1 }")]
  public void A_key_the_live_runner_does_not_read_yet_is_refused(
    string key,
    string value
  ) =>
    Assert.Contains(
      "not read by the live runner",
      Refused(Place, $"{{ \"{key}\": {value} }}")
    );

  /// <summary>Fails when the live refusal walks <c>steps</c> only, so a step kind the runner does
  /// not run is let through inside a variant's <c>after</c> (V35).</summary>
  [Fact]
  public void A_step_kind_the_runner_does_not_run_is_refused_inside_a_variants_after() {
    string message = Assert
      .Throws<SetupFileException>(() =>
        ScenarioCase.Load(
          "exlib/case",
          LiveFixture.Text(
            Place,
            """{ variants: { later: { after: { a: [{ "reload": {} }] } } } }"""
          ),
          "case.json",
          [],
          "1.22"
        )
      )
      .Message;
    Assert.Contains(
      "\"reload\" is not a step the live runner runs yet",
      message
    );
  }

  /// <summary>Fails when the live refusal walks <c>steps</c> only, so a <c>paste</c> inside
  /// <c>during</c> reaches the key refusal's text instead of its own (V41).</summary>
  [Fact]
  public void A_paste_inside_during_is_refused_naming_the_action() {
    string message = Refused(
      Place,
      """{ during: [{ at: 2, step: { "paste": { "file": "x", "at": [0, 0, 0] } } }] }"""
    );
    Assert.Contains("during[0]", message);
    Assert.Contains(
      "\"paste\" is not a step the live runner runs yet",
      message
    );
  }

  /// <summary>Fails when a <c>game</c> above the server's series is accepted (V36).</summary>
  [Fact]
  public void A_game_above_the_servers_series_is_refused() =>
    Assert.Equal(
      "case.json: game: needs 1.23; the server runs 1.22.",
      Assert
        .Throws<SetupFileException>(() =>
          LiveFixture.Case(Place, """{ game: "1.23" }""")
        )
        .Message
    );

  /// <summary>Fails when the series compare is dropped and an older game is refused too (V37).</summary>
  [Fact]
  public void A_game_at_or_below_the_servers_series_is_read() =>
    Assert.Equal(
      "1.21",
      LiveFixture.Case(Place, """{ game: "1.21" }""").Model.Game
    );

  /// <summary>Fails when a mod the server has not loaded is accepted (V38).</summary>
  [Fact]
  public void A_mod_that_is_not_loaded_is_refused() =>
    Assert.Equal(
      "case.json: mods: \"siex\" is not loaded.",
      Assert
        .Throws<SetupFileException>(() =>
          LiveFixture.Case(
            Place,
            """{ mods: ["iiex", "siex"] }""",
            "1.22",
            "iiex"
          )
        )
        .Message
    );

  /// <summary>Fails when a loaded mod is refused (V39).</summary>
  [Fact]
  public void Loaded_mods_are_read() =>
    Assert.Equal(
      ["iiex"],
      LiveFixture
        .Case(Place, """{ mods: ["iiex"] }""", "1.22", "iiex")
        .Model.Mods
    );

  /// <summary>Fails when tickOrders, variants or checks are refused again, or their reading is
  /// dropped from the model the live case runs.</summary>
  [Fact]
  public void Tick_orders_variants_and_checks_are_read() {
    ScenarioCase scenario = LiveFixture.Case(
      """
      [
        { "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } },
        { "place": { "id": "b", "code": "game:stone", "at": [1, 0, 0] } }
      ]
      """,
      """
      { tickOrders: ["a", "b"], variants: { slow: { seconds: 12 } },
        checks: [{ id: "held", says: "a holds", always: { at: [0, 0, 0], block: "game:stone" } }] }
      """
    );
    Assert.Equal(["a", "b"], scenario.Model.TickOrders);
    Assert.Equal("slow", Assert.Single(scenario.Model.Variants).Name);
    Assert.Equal("held", Assert.Single(scenario.Model.Checks).Id);
  }

  /// <summary>Fails when a layout is refused again, or its drawing is not read (V34).</summary>
  [Fact]
  public void A_layout_is_read() =>
    Assert.NotNull(
      LiveFixture
        .Case(
          "[]",
          """{ layout: { origin: [0, 0], legend: { "A": { code: "x:a" } }, layers: { "0": ["A"] } } }"""
        )
        .Model.Drawn
    );

  /// <summary>Fails when a file with a <c>during</c> action and a recorded machine is refused.</summary>
  [Fact]
  public void A_during_action_and_a_recording_are_read() {
    ScenarioCase scenario = LiveFixture.Case(
      Place,
      """
      { during: [{ at: 1, step: { wait: 1 } }],
        record: { machines: { a: { series: { state: "on" } } } } }
      """
    );
    Assert.Single(scenario.Model.During);
    Assert.Single(scenario.Model.Record.Machines);
  }

  /// <summary>Fails when a pack that starts from another stage is refused (the refusal back in
  /// Load), since the runner reads the stage files.</summary>
  [Fact]
  public void A_pack_after_another_stage_is_read() =>
    Assert.Equal(
      1,
      LiveFixture
        .Case(
          Place,
          """{ pack: { scope: "all", after: { "iiex/setups/play-stations": 1 } } }"""
        )
        .Model.Pack!.After.Count
    );

  /// <summary>Fails when a pack without an <c>after</c> is refused, since a stage that starts from
  /// its gathered stacks alone must load.</summary>
  [Fact]
  public void A_pack_without_an_after_is_read() =>
    Assert.NotNull(
      LiveFixture
        .Case(
          Place,
          """{ pack: { scope: "all", gathered: { "game:stone": 1 } } }"""
        )
        .Model.Pack
    );

  /// <summary>Fails when a step the live runner cannot run is accepted (V29).</summary>
  [Fact]
  public void A_step_kind_the_live_runner_has_no_step_for_is_refused() =>
    Assert.Contains(
      "\"reload\" is not a step the live runner runs yet",
      Refused("""[{ "reload": {} }]""")
    );

  /// <summary>Fails when a negative advance reaches the runner, which would run the calendar back.</summary>
  [Fact]
  public void An_advance_of_negative_hours_is_refused_live() =>
    Assert.Contains(
      "hours is negative",
      Refused("""[{ "advance": { "hours": -1 } }]""")
    );

  /// <summary>Fails when a select of a slot past the hotbar reaches the runner.</summary>
  [Fact]
  public void A_select_of_a_slot_past_the_hotbar_is_refused_live() =>
    Assert.Contains(
      "slot 10 is not a hotbar slot",
      Refused("""[{ "select": { "slot": 10 } }]""")
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
      "\"reload\"",
      Refused("""[{ "repeat": { "times": 2, "steps": [{ "reload": {} }] } }]""")
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
      name + ".json",
      [],
      "1.22"
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
