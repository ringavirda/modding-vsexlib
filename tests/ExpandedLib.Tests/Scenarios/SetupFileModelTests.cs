using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests.Scenarios;

/// <summary>What a setup file loads as, and each malformed file refused with a message naming
/// the file, where in it and the key at fault (setups.md, The file).</summary>
public class SetupFileModelTests {
  private const string File = "boiler.json";

  #region Fixtures

  /// <summary>A file placing <c>boiler</c> and recording it, its root keys replaced or added
  /// by <paramref name="changes"/>, a JSON object's text.</summary>
  private static string Text(string changes = "{}") {
    var root = JObject.Parse(
      """
      {
        setup: "boiler",
        description: "a boiler on its own",
        mods: ["iiex"],
        seconds: 100,
        steps: [{ "place": { "id": "boiler", "code": "game:stone", "at": [0, 0, 0] } }],
        record: {
          machines: {
            boiler: { series: { state: "on", heat: { at: [0, 0, 0], member: "Heat", unit: "C" } } }
          }
        }
      }
      """
    );
    root.Merge(
      JObject.Parse(changes),
      new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace }
    );
    return root.ToString();
  }

  private static SetupFileModel Load(string changes = "{}") =>
    SetupFileModel.Parse(Text(changes), File);

  private static string Refused(string changes) =>
    Assert.Throws<SetupFileException>(() => Load(changes)).Message;

  #endregion

  #region Loads

  // Fails when a default is not the spec's: game 1.22, the last third steady, the stepped
  // clock, a timeout of seconds x 1.05 plus 120.
  [Fact]
  public void A_file_takes_the_defaults_of_what_it_leaves_out() {
    SetupFileModel model = Load();

    Assert.Equal("boiler", model.Setup);
    Assert.Equal("1.22", model.Game);
    Assert.Equal((67, 100), model.Steady);
    Assert.True(model.SteppedClock);
    Assert.Equal(225d, model.Timeout);
    Assert.Equal("game:stone", model.Placements["boiler"]);
    Assert.Equal(
      new[] { "heat" },
      model.Record.Machines[0].Series.Select(s => s.Name)
    );
  }

  // Fails when the machine's code is not taken from the step that places it.
  [Fact]
  public void A_recorded_machine_takes_its_code_from_its_step() {
    Assert.Equal("game:stone", Load().Record.Machines[0].Code);
  }

  // Fails when an action at a second the run never reaches loads.
  [Fact]
  public void An_action_at_or_past_the_runs_seconds_is_refused_naming_it() {
    string during(int at) =>
      $$"""{ "during": [{ "at": {{at}}, "step": { "wait": { "seconds": 1 } } }] }""";

    Assert.Equal(1, Load(during(99)).During.Count);
    string message = Refused(during(100));
    Assert.Contains("during[0].at", message);
    Assert.Contains("second 100", message);
  }

  // Fails when a variant's during is judged against the main case's seconds, not its own.
  [Fact]
  public void A_variants_action_past_its_own_seconds_is_refused() {
    string message = Refused(
      """{ "variants": { "short": { "seconds": 50, "during": [{ "at": 60, "step": { "wait": { "seconds": 1 } } }] } } }"""
    );

    Assert.Contains("short", message);
    Assert.Contains("second 60", message);
  }

  // Fails when a variant's shorter seconds leaves an inherited action at or past its end.
  [Fact]
  public void A_variant_shorter_than_an_inherited_action_is_refused_naming_both() {
    const string during =
      """{ "during": [{ "at": 60, "step": { "wait": { "seconds": 1 } } }], "variants": { "short": { "seconds": SECONDS } } }""";

    Assert.Equal(
      (int?)61,
      Load(during.Replace("SECONDS", "61")).Variants.Single().Seconds
    );
    string message = Refused(during.Replace("SECONDS", "60"));
    Assert.Contains("variants.short.seconds", message);
    Assert.Contains("during[0].at, second 60", message);
  }

  // Fails when a variant's shorter seconds leaves an inherited every or when action starting at or
  // past its end.
  [Fact]
  public void A_variant_shorter_than_an_inherited_from_is_refused_naming_it() {
    string message = Refused(
      """{ "during": [{ "every": 5, "from": 70, "step": { "wait": { "seconds": 1 } } }], "variants": { "short": { "seconds": 50 } } }"""
    );

    Assert.Contains("variants.short.seconds", message);
    Assert.Contains("during[0].from, second 70", message);
  }

  // Fails when an every action starting at or past the run's seconds loads.
  [Fact]
  public void An_every_action_starting_at_or_past_the_runs_seconds_is_refused_naming_it() {
    string message = Refused(
      """{ "during": [{ "every": 5, "from": 100, "step": { "wait": { "seconds": 1 } } }] }"""
    );

    Assert.Contains("during[0].from", message);
    Assert.Contains("second 100", message);
  }

  // Fails when a variant name holding a dot is refused, as the spec's water-gate-1.5 does.
  [Fact]
  public void A_variant_name_may_hold_a_dot() {
    SetupFileModel model = Load(
      """{ "variants": { "water-gate-1.5": { "seconds": 50 } } }"""
    );

    Assert.Equal("water-gate-1.5", model.Variants.Single().Name);
  }

  // Fails when a comment or an unquoted key is refused, or a key written twice is read.
  [Fact]
  public void A_file_reads_as_an_asset_reads_and_refuses_a_key_twice() {
    string text = Text()
      .Replace("{", "{ // a comment\n")
      .Replace("\"seconds\"", "seconds");

    Assert.Equal(100, SetupFileModel.Parse(text, File).Seconds);
    var e = Assert.Throws<SetupFileException>(() =>
      SetupFileModel.Parse(
        text.Replace("seconds:", "seconds: 5, seconds:"),
        File
      )
    );
    Assert.StartsWith("boiler.json: ", e.Message);
    Assert.Contains("seconds", e.Message);
  }

  #endregion

  #region Refused

  // Fails when an unknown key inside a step passes, or its message loses the file or the step.
  [Fact]
  public void An_unknown_step_key_names_the_file_and_the_step() {
    Assert.Equal(
      "boiler.json: steps[0] (place): unknown key \"cod\".",
      Refused(
        """{ "steps": [{ "place": { "code": "game:stone", "cod": 1, "at": [0, 0, 0] } }] }"""
      )
    );
  }

  // Fails when an unknown root key passes.
  [Fact]
  public void An_unknown_root_key_is_refused() {
    Assert.Equal(
      "boiler.json: unknown key \"secs\".",
      Refused("""{ "secs": 5 }""")
    );
  }

  // Fails when a scenario file's cases or matrix is read as part of a setup.
  [Theory]
  [InlineData("cases")]
  [InlineData("matrix")]
  public void Cases_and_matrix_are_refused(string key) {
    Assert.Equal(
      $"boiler.json: \"{key}\" is refused in a setup file; variants take its place.",
      Refused($$"""{ "{{key}}": [] }""")
    );
  }

  // Fails when an id declared by two steps passes.
  [Fact]
  public void An_id_declared_twice_is_refused() {
    Assert.Equal(
      "boiler.json: steps[1]: \"boiler\" is declared twice, by steps[0] and steps[1].",
      Refused(
        """
        { "steps": [
          { "place": { "id": "boiler", "code": "game:stone", "at": [0, 0, 0] } },
          { "place": { "id": "boiler", "code": "game:stone", "at": [1, 0, 0] } }
        ] }
        """
      )
    );
  }

  // Fails when an "of" naming an id nobody places passes.
  [Fact]
  public void An_of_naming_no_placed_id_is_refused() {
    Assert.Equal(
      "boiler.json: steps[1] (wait).until.at: \"pump\" is placed by no step or legend entry.",
      Refused(
        """
        { "steps": [
          { "place": { "id": "boiler", "code": "game:stone", "at": [0, 0, 0] } },
          { "wait": { "seconds": 5, "until": { "at": { "of": "pump" }, "block": "game:*" } } }
        ] }
        """
      )
    );
  }

  // Fails when a clock other than stepped or real passes, or real is read as stepped.
  [Fact]
  public void The_clock_is_stepped_or_real() {
    Assert.False(Load("""{ "clock": "real" }""").SteppedClock);
    Assert.Equal(
      "boiler.json: clock: \"fast\" is not \"stepped\" or \"real\".",
      Refused("""{ "clock": "fast" }""")
    );
  }

  // Fails when the timeout floor is not seconds x 1.05 plus 60, or a timeout on it is refused.
  [Fact]
  public void A_timeout_below_its_floor_is_refused() {
    Assert.Equal(165d, Load("""{ "timeout": 165 }""").Timeout);
    Assert.Equal(
      "boiler.json: timeout: 164.5 s is below seconds x 1.05 plus 60, 165 s.",
      Refused("""{ "timeout": 164.5 }""")
    );
  }

  // Fails when a check's series names an id the record does not record, or a series it lacks.
  [Fact]
  public void A_check_series_the_record_lacks_is_refused() {
    Assert.Equal(
      "boiler.json: checks[0].never.series: \"pump\" is not recorded.",
      Refused(
        """{ "checks": [{ "id": "c", "says": "s", "never": { "series": "pump.state", "min": 1 } }] }"""
      )
    );
    Assert.Equal(
      "boiler.json: checks[0].never.series: \"boiler\" records no series \"flow\".",
      Refused(
        """{ "checks": [{ "id": "c", "says": "s", "never": { "series": "boiler.flow", "min": 1 } }] }"""
      )
    );
  }

  // Fails when intact names an id that laid no run.
  [Fact]
  public void An_intact_naming_no_run_is_refused() {
    Assert.Equal(
      "boiler.json: checks: check \"c\": \"boiler\" is no run.",
      Refused(
        """{ "checks": [{ "id": "c", "says": "s", "intact": ["boiler"] }] }"""
      )
    );
  }

  // Fails when a variant's fails names no check of its case.
  [Fact]
  public void A_variant_failing_no_check_is_refused() {
    Assert.Equal(
      "boiler.json: variants.dry.fails: \"burst\" is no check of this case.",
      Refused("""{ "variants": { "dry": { "fails": "burst" } } }""")
    );
  }

  // Fails when a run too short for a 2 s default window loads without a steady of its own.
  [Fact]
  public void A_run_too_short_for_the_default_window_takes_a_steady() {
    Assert.Equal(
      "boiler.json: steady: the last third of 5 s is under 2 s; the file takes a \"steady\".",
      Refused("""{ "seconds": 5 }""")
    );
    Assert.Equal((4, 6), Load("""{ "seconds": 6 }""").Steady);
  }

  // Fails when a run window shorter than 2 s or outside the run passes.
  [Fact]
  public void A_steady_window_outside_the_run_is_refused() {
    Assert.Equal(
      "boiler.json: steady: [90, 101] is not a window of at least 2 s inside the 100 s run.",
      Refused("""{ "steady": [90, 101] }""")
    );
  }

  #endregion
}
