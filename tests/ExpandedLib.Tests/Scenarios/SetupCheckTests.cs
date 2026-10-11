using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios;

/// <summary>The check kinds of setups.md, Checks, run over scripted recordings.</summary>
public class SetupCheckTests {
  private const int Seconds = 12;

  #region Fixtures

  /// <summary>Reads the scripted value of the context's second.</summary>
  private sealed class Script(object[] values) : Reading {
    public override object Read(ReadingContext ctx) => values[ctx.Second];
  }

  /// <summary>A finished recording whose machine <c>m</c> records <c>v</c> in atm and the
  /// state word of each second, "on" when none is given.</summary>
  private static SetupRecording Recorded(float[] values, string[]? words = null) {
    var recording = new SetupRecording(
      "setup",
      "1.22",
      [("exlib", "1.0.0")],
      Seconds
    );
    recording
      .Machine("m", "test:m")
      .State(new Script([.. words ?? Enumerable.Repeat("on", Seconds)]))
      .Value("v", "atm", new Script([.. values.Cast<object>()]));
    var ctx = new ReadingContext(null);
    recording.Start(ctx);
    for (int t = 0; t < Seconds; t++)
      recording.Sample(t, ctx);
    return recording;
  }

  private static float[] Repeat(float value) =>
    [.. Enumerable.Repeat(value, Seconds)];

  private static SetupCheck Check(string json) =>
    SetupCheck.Parse(
      JToken.Parse(json),
      new SetupJson.Where("test.json", "checks[0]"),
      new SetupReferences()
    );

  #endregion

  #region Kinds

  // Fails when never fails on a second its test does not hold, or names another second.
  [Fact]
  public void Never_fails_at_the_first_second_its_test_holds() {
    SetupCheck check = Check(
      """{ "id": "no-choke", "says": "v stays under 5", "never": { "series": "m.v", "min": 5 } }"""
    );
    float[] values = Repeat(1f);
    values[3] = 6f;
    values[7] = 6f;

    SetupCheckFailure? failure = check.Run(Recorded(values));

    Assert.Null(check.Run(Recorded(Repeat(1f))));
    Assert.NotNull(failure);
    Assert.Equal(3, failure!.Second);
    Assert.Equal(
      "check no-choke (v stays under 5) fails at second 3: m.v = 6",
      failure.ToString()
    );
  }

  // Fails when a steady window check reads seconds before the window.
  [Fact]
  public void Always_over_the_steady_window_reads_only_the_window() {
    SetupCheck check = Check(
      """
      { "id": "band", "says": "v holds 2", "window": "steady",
        "always": { "series": "m.v", "equals": 2 } }
      """
    );
    float[] values = Repeat(2f);
    values[0] = 0f;
    values[7] = 0f;

    Assert.Null(check.Run(Recorded(values)));
    values[9] = 0f;
    Assert.Equal(9, check.Run(Recorded(values))!.Second);
  }

  // Fails when steady passes a series climbing in the window, or a named series fails for
  // another series' unsteadiness.
  [Fact]
  public void Steady_fails_on_the_named_series_or_any_for_a_star() {
    string[] words = [.. Enumerable.Repeat("idle", 10), "run", "run"];
    SetupRecording recording = Recorded(Repeat(1f), words);

    SetupCheckFailure? star = Check(
        """{ "id": "all-steady", "says": "everything settles", "steady": "*" }"""
      )
      .Run(recording);

    Assert.Equal("unsteady m.state", star!.Readings);
    Assert.Null(
      Check("""{ "id": "v-steady", "says": "v settles", "steady": "m.v" }""")
        .Run(recording)
    );
  }

  // Fails when mean compares another figure than the steady mean, or ignores within.
  [Fact]
  public void Mean_holds_while_the_steady_mean_is_within_its_bound() {
    float[] values = Repeat(9f);
    values[8] = 1f;
    values[9] = 1f;
    values[10] = 1f;
    values[11] = 5f;
    SetupRecording recording = Recorded(values);

    Assert.Null(
      Check(
          """{ "id": "rate", "says": "v means 2", "mean": { "series": "m.v", "equals": 2, "within": 0.01 } }"""
        )
        .Run(recording)
    );
    Assert.Equal(
      "m.v steady mean = 2",
      Check(
          """{ "id": "rate", "says": "v means 2.5", "mean": { "series": "m.v", "equals": 2.5, "within": 0.4 } }"""
        )
        .Run(recording)!
        .Readings
    );
  }

  // Fails when intact passes a run one of whose cells turned to air.
  [Fact]
  public void Intact_fails_at_the_first_second_a_run_cell_is_air() {
    var world = new SetupTestWorld();
    Block pipe = TestBlocks.Configure(new Block(), "test:pipe", 1);
    world
      .Test.Place(new BlockPos(0, 0, 0), pipe)
      .Place(new BlockPos(0, 0, 1), pipe);
    world.Laid("steam", new BlockPos(0, 0, 0), new BlockPos(0, 0, 1));
    SetupCheck check = Check(
      """{ "id": "no-burst", "says": "the steam run holds", "intact": ["steam"] }"""
    );
    var recording = new SetupRecording(
      "setup",
      "1.22",
      [("exlib", "1.0.0")],
      6
    );
    var ctx = new ReadingContext(world);
    for (int t = 0; t < 6; t++) {
      ctx.Second = t;
      if (t == 2)
        world.Test.Accessor.SetBlock(0, new BlockPos(0, 0, 1));
      foreach (SampledReading sampled in check.Sampled)
        sampled.Sample(ctx);
    }

    SetupCheckFailure? failure = check.Run(Complete(recording));

    Assert.Equal(2, failure!.Second);
  }

  // Fails when a check's world reading is read off the world at check time instead of from
  // the seconds sampled.
  [Fact]
  public void A_world_reading_in_a_check_is_read_back_from_its_samples() {
    var world = new SetupTestWorld();
    world.Test.Place(
      new BlockPos(0, 0, 0),
      TestBlocks.Configure(new Block(), "test:b", 1)
    );
    SetupCheck check = Check(
      """
      { "id": "unmoved", "says": "the block stays",
        "never": { "at": [0, 0, 0], "block": "game:air" } }
      """
    );
    var ctx = new ReadingContext(world);
    for (int t = 0; t < 6; t++) {
      ctx.Second = t;
      if (t == 1)
        world.Test.Accessor.SetBlock(0, new BlockPos(0, 0, 0));
      foreach (SampledReading sampled in check.Sampled)
        sampled.Sample(ctx);
    }

    SetupCheckFailure? failure = check.Run(
      Complete(new SetupRecording("setup", "1.22", [("exlib", "1.0.0")], 6))
    );

    Assert.Equal(1, failure!.Second);
  }

  // Fails when First runs the checks out of file order or skips a failing one.
  [Fact]
  public void First_returns_the_first_failing_check_in_file_order() {
    SetupRecording recording = Recorded(Repeat(6f));
    SetupCheck[] checks =
    [
      Check(
        """{ "id": "a", "says": "a", "always": { "series": "m.v", "min": 5 } }"""
      ),
      Check(
        """{ "id": "b", "says": "b", "never": { "series": "m.v", "min": 5 } }"""
      ),
      Check(
        """{ "id": "c", "says": "c", "never": { "series": "m.v", "max": 7 } }"""
      ),
    ];

    Assert.Equal("b", SetupCheck.First(checks, recording)!.Id);
  }

  // Fails when a check naming two kinds is read as one of them.
  [Fact]
  public void A_check_naming_two_kinds_is_refused() {
    var e = Assert.Throws<SetupFileException>(() =>
      Check(
        """{ "id": "a", "says": "a", "steady": "*", "intact": ["steam"] }"""
      )
    );

    Assert.Equal(
      "test.json: checks[0]: takes one of never, always, steady, mean and intact.",
      e.Message
    );
  }

  /// <summary><paramref name="recording"/> sampled to its end, with no runs or machines.</summary>
  private static SetupRecording Complete(SetupRecording recording) {
    var ctx = new ReadingContext(null);
    recording.Start(ctx);
    for (int t = 0; t < recording.Seconds; t++)
      recording.Sample(t, ctx);
    return recording;
  }

  #endregion
}
