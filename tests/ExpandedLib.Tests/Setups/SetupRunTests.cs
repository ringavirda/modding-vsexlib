#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The run's seconds: their order, the <c>during</c> actions and the <c>record</c> samples, and
/// the write or compare of the recording (setups.md, The run and What is recorded).</summary>
[Collection("Setups")]
public class SetupRunTests : IDisposable
{
  private readonly string _folder = Path.Combine(
    Path.GetTempPath(),
    "exlib-setup-run-" + Guid.NewGuid().ToString("N")
  );

  public SetupRunTests()
  {
    Environment.SetEnvironmentVariable("EX_WRITE_SETUPS", null);
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable("EX_WRITE_SETUPS", null);
    if (Directory.Exists(_folder))
      Directory.Delete(_folder, recursive: true);
  }

  private static string Fixture(params string[] parts) =>
    Path.Combine([RepoPaths.Root, .. parts]);

  private static string ModPath(string id) =>
    id switch
    {
      "setupbox" => Fixture(
        "tests",
        "ExpandedLib.Tests",
        "Setups",
        "Fixtures",
        "SetupBox"
      ),
      "setuphold" => Fixture(
        "tests",
        "ExpandedLib.Tests",
        "Setups",
        "Fixtures",
        "SetupHold"
      ),
      _ => throw new ArgumentOutOfRangeException(nameof(id), id, "no such mod"),
    };

  private static readonly IReadOnlyList<(string Mod, string Version)> BoxMods =
  [
    ("exlib", "0.8.4"),
    ("setupbox", "1.0.0"),
  ];

  private static readonly IReadOnlyList<(
    string Mod,
    string Version
  )> HoldMods = [("exlib", "0.8.4"), ("setuphold", "1.0.0")];

  private const string Dial =
    "{\"place\": {\"id\": \"dial\", \"code\": \"setuphold:dial\", \"at\": [0, 0, 0]}}";
  private const string Box =
    "{\"place\": {\"id\": \"box\", \"code\": \"setupbox:box\", \"at\": [0, 0, 0]}}";
  private const string DialRecord =
    "{\"machines\": {\"dial\": {\"series\": {\"state\": {\"words\": [\"idle\"]}, \"fires\": {\"member\": \"Fires\", \"unit\": \"u\"}}}}}";
  private const string BoxRecord =
    "{\"machines\": {\"box\": {\"series\": {\"state\": {\"words\": [\"idle\"]}, \"level\": {\"member\": \"Level\", \"unit\": \"u\"}}}}}";

  private static string SetLevel(int level) =>
    "{\"set\": {\"at\": [0, 0, 0], \"member\": {\"Level\": " + level + "}}}";

  private static string During(string body) => "{" + body + "}";

  private static SetupScene Build(
    string mod,
    string steps,
    string during,
    string record,
    string checks = "[]"
  ) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"run\", \"description\": \"d\", \"mods\": [\""
          + mod
          + "\"], \"steps\": "
          + steps
          + ", \"during\": "
          + during
          + ", \"seconds\": 6, \"record\": "
          + record
          + ", \"checks\": "
          + checks
          + "}",
        "run.json"
      ),
      [ModPath(mod)],
      null,
      w =>
        SetupTickOrderTests
          .RegisterTick(w, [])
          .RegisterClass("SetupBox", typeof(BlockEntity))
          .RegisterBlockEntityFactory(
            "SetupBox",
            () =>
            {
              var box = new SetupBox();
              box.Behaviors.Add(new BoxBehaviour(box));
              return box;
            }
          )
          .RegisterClass("SetupHoldBlock", typeof(SetupHoldBlock))
          .RegisterClass("SetupHoldEntity", typeof(SetupHoldEntity))
          .RegisterBlockEntityFactory(
            "SetupHoldEntity",
            () => new SetupHoldEntity()
          )
          .RegisterClass("SetupHoldEmber", typeof(SetupHoldEmber))
          .RegisterBlockEntityFactory(
            "SetupHoldEmber",
            () => new SetupHoldEmber()
          )
          .RegisterClass("SetupHoldWand", typeof(SetupHoldWand))
          .RegisterClass("SetupHoldBox", typeof(SetupHoldBox))
          .RegisterBlockEntityFactory("SetupHoldBox", () => new SetupHoldBox())
          .RegisterBlockEntityBehaviorFactory(
            "SetupSpin",
            be => new SetupSpin(be)
          )
    );

  private static double[] Series(
    SetupRecording recording,
    string id,
    string name
  ) =>
    Enumerable
      .Range(0, recording.Seconds)
      .Select(s => Convert.ToDouble(recording.ValueAt(id, name, s)))
      .ToArray();

  [Fact]
  public void A_run_takes_each_second_s_due_actions_before_its_ticks()
  {
    string expect =
      "{\"expect\": {\"at\": [0, 0, 0], \"member\": \"Fires\", \"equals\": 1}}";
    using SetupScene scene = Build(
      "setuphold",
      $"[{Dial}]",
      "[" + During("\"at\": 1, \"step\": " + expect) + "]",
      "{}"
    );
    scene.Run(HoldMods);
  }

  // Fails when TickSecond does not advance the calendar.
  [Fact]
  public void Each_harness_second_moves_the_calendar_one_120th_of_a_game_hour()
  {
    using SetupScene scene = Build("setuphold", $"[{Dial}]", "[]", DialRecord);
    double before = scene.World.Calendar.TotalHours;
    SetupRecording recording = scene.Run(HoldMods);
    Assert.Equal(6, recording.Seconds);
    Assert.Equal(
      before + 6 / 120.0,
      scene.World.Calendar.TotalHours,
      precision: 9
    );
  }

  [Fact]
  public void A_run_samples_each_second_after_that_second_s_ticks()
  {
    var actions = Enumerable
      .Range(1, 5)
      .Select(t =>
        During(
          $"\"at\": {t}, \"step\": {{\"expect\": {{\"at\": [0, 0, 0], \"member\": \"Fires\", \"equals\": {t}}}}}"
        )
      );
    using SetupScene scene = Build(
      "setuphold",
      $"[{Dial}]",
      "[" + string.Join(", ", actions) + "]",
      DialRecord
    );
    SetupRecording recording = scene.Run(HoldMods);
    Assert.Equal([1, 2, 3, 4, 5, 6], Series(recording, "dial", "fires"));
  }

  [Fact]
  public void An_at_action_runs_once_in_its_second()
  {
    using SetupScene scene = Build(
      "setupbox",
      $"[{Box}]",
      "["
        + During("\"every\": 1, \"from\": 3, \"step\": " + SetLevel(0))
        + ", "
        + During("\"at\": 2, \"step\": " + SetLevel(4))
        + "]",
      BoxRecord
    );
    SetupRecording recording = scene.Run(BoxMods);
    Assert.Equal([0, 0, 4, 0, 0, 0], Series(recording, "box", "level"));
  }

  [Fact]
  public void An_every_action_runs_in_its_seconds_from_its_from()
  {
    using SetupScene scene = Build(
      "setupbox",
      $"[{Box}]",
      "["
        + During("\"every\": 2, \"from\": 2, \"step\": " + SetLevel(7))
        + ", "
        + During("\"every\": 2, \"from\": 3, \"step\": " + SetLevel(0))
        + "]",
      BoxRecord
    );
    SetupRecording recording = scene.Run(BoxMods);
    Assert.Equal([0, 0, 7, 0, 7, 0], Series(recording, "box", "level"));
  }

  private const string DrawnCheck =
    "{\"id\": \"drawn\", \"says\": \"drawn says\", \"orders\": \"drawn\", \"always\": {\"series\": \"box.level\", \"equals\": 9}}";
  private const string OrderedCheck =
    "{\"id\": \"ordered\", \"says\": \"ordered says\", \"always\": {\"series\": \"box.level\", \"equals\": 8}}";

  // Fails when a drawn check is judged under ordersOnly.
  [Fact]
  public void Judging_orders_only_skips_a_drawn_check_and_fails_on_an_ordered_one()
  {
    using SetupScene scene = Build(
      "setupbox",
      $"[{Box}]",
      "[]",
      BoxRecord,
      $"[{DrawnCheck}, {OrderedCheck}]"
    );
    SetupRecording recording = scene.Run(BoxMods);
    var run = new SetupRun(scene.File.Model, recording);

    Assert.StartsWith(
      "check drawn (drawn says) fails at second 0",
      run.Judge()
    );
    Assert.StartsWith(
      "check ordered (ordered says) fails at second 0",
      run.Judge(ordersOnly: true)
    );
    IReadOnlyList<SetupVerdict> verdicts = run.Verdicts(ordersOnly: true);
    Assert.Equal([0, 1], verdicts.Select(v => v.Index));
    Assert.True(verdicts[0].Skipped);
    Assert.Null(verdicts[0].Failure);
    Assert.False(verdicts[1].Skipped);
    Assert.Equal("ordered", verdicts[1].Failure!.Id);
  }

  [Fact]
  public void A_when_action_runs_only_in_checked_seconds_its_test_holds()
  {
    using SetupScene scene = Build(
      "setupbox",
      $"[{Box}, {SetLevel(3)}]",
      "["
        + During("\"at\": 1, \"step\": " + SetLevel(5))
        + ", "
        + During(
          "\"every\": 1, \"when\": {\"at\": [0, 0, 0], \"member\": \"Level\", \"equals\": 3}, \"step\": "
            + SetLevel(0)
        )
        + "]",
      BoxRecord
    );
    SetupRecording recording = scene.Run(BoxMods);
    Assert.Equal([0, 5, 5, 5, 5, 5], Series(recording, "box", "level"));
  }

  // Fails when a when action latches to its first checked second, or runs once.
  [Fact]
  public void A_when_action_runs_again_each_time_its_test_comes_true()
  {
    using SetupScene scene = Build(
      "setupbox",
      $"[{Box}, {SetLevel(3)}]",
      "["
        + During("\"at\": 1, \"step\": " + SetLevel(5))
        + ", "
        + During("\"at\": 3, \"step\": " + SetLevel(3))
        + ", "
        + During(
          "\"every\": 1, \"when\": {\"at\": [0, 0, 0], \"member\": \"Level\", \"equals\": 3}, \"step\": "
            + SetLevel(0)
        )
        + "]",
      BoxRecord
    );
    SetupRecording recording = scene.Run(BoxMods);
    Assert.Equal([0, 5, 5, 0, 0, 0], Series(recording, "box", "level"));
  }

  // Fails when the run materialises the second's due actions before running them, so a when
  // action tests its reading before an earlier action of that second has run.
  [Fact]
  public void A_when_action_sees_the_step_an_earlier_action_ran_in_the_same_second()
  {
    using SetupScene scene = Build(
      "setupbox",
      $"[{Box}]",
      "["
        + During("\"at\": 1, \"step\": " + SetLevel(3))
        + ", "
        + During(
          "\"every\": 1, \"when\": {\"at\": [0, 0, 0], \"member\": \"Level\", \"equals\": 3}, \"step\": "
            + SetLevel(0)
        )
        + "]",
      BoxRecord
    );
    SetupRecording recording = scene.Run(BoxMods);
    Assert.Equal([0, 0, 0, 0, 0, 0], Series(recording, "box", "level"));
  }

  // Fails when a when action tests its reading in a second its period skips.
  [Fact]
  public void A_when_action_does_not_run_in_a_second_its_period_skips()
  {
    using SetupScene scene = Build(
      "setupbox",
      $"[{Box}]",
      "["
        + During("\"at\": 1, \"step\": " + SetLevel(3))
        + ", "
        + During("\"at\": 2, \"step\": " + SetLevel(8))
        + ", "
        + During(
          "\"every\": 2, \"when\": {\"at\": [0, 0, 0], \"member\": \"Level\", \"equals\": 3}, \"step\": "
            + SetLevel(0)
        )
        + "]",
      BoxRecord
    );
    SetupRecording recording = scene.Run(BoxMods);
    Assert.Equal([0, 3, 8, 8, 8, 8], Series(recording, "box", "level"));
  }

  // Fails when the lines are not joined after a during action that ends with a placement.
  [Fact]
  public void A_during_placement_joins_the_mechanical_lines_before_the_second_ticks()
  {
    const string axle =
      "{\"place\": {\"id\": \"first\", \"code\": \"setuphold:spinaxle-ns\", \"at\": [0, 0, 0]}}, "
      + "{\"place\": {\"code\": \"setuphold:spinaxle-ns\", \"at\": [0, 0, 2]}}";
    const string middle =
      "{\"place\": {\"code\": \"setuphold:spinaxle-ns\", \"at\": [0, 0, 1]}}";
    using SetupScene scene = Build(
      "setuphold",
      $"[{axle}]",
      "[" + During("\"at\": 1, \"step\": " + middle) + "]",
      "{\"machines\": {\"first\": {\"series\": {\"state\": {\"words\": [\"idle\"]}, \"speed\": {\"network\": \"mechanical\", \"member\": \"Speed\", \"or\": -1, \"unit\": \"u\"}}}}}"
    );
    SetupRecording recording = scene.Run(HoldMods);
    Assert.Equal([-1, 0, 0, 0, 0, 0], Series(recording, "first", "speed"));
  }

  // Fails when the recording names the file's game instead of the series that ran it.
  [Fact]
  public void Record_names_the_game_series_that_ran_the_case()
  {
    using SetupScene scene = Build("setuphold", $"[{Dial}]", "[]", DialRecord);
    string text = scene.Record(scene.Run(HoldMods, "9.9"), _folder);
    Assert.Contains("\"game\":\"9.9\"", text);
  }

  // Fails when Record reads EX_WRITE_SETUPS whatever variable it is given.
  [Fact]
  public void Record_writes_the_setup_when_the_variable_it_is_given_names_it()
  {
    Directory.CreateDirectory(_folder);
    string committed = Path.Combine(_folder, "run.json");
    File.WriteAllText(committed, "stale");
    Environment.SetEnvironmentVariable("LEGACY_WRITE_SETUPS", "1");
    try
    {
      using SetupScene scene = Build(
        "setuphold",
        $"[{Dial}]",
        "[]",
        DialRecord
      );
      string text = scene.Record(
        scene.Run(HoldMods),
        _folder,
        "LEGACY_WRITE_SETUPS"
      );
      Assert.Equal(text, File.ReadAllText(committed));
    }
    finally
    {
      Environment.SetEnvironmentVariable("LEGACY_WRITE_SETUPS", null);
    }
  }

  [Fact]
  public void Record_writes_the_setup_when_EX_WRITE_SETUPS_names_it()
  {
    Directory.CreateDirectory(_folder);
    string committed = Path.Combine(_folder, "run.json");
    File.WriteAllText(committed, "stale");
    Environment.SetEnvironmentVariable("EX_WRITE_SETUPS", "run");
    using SetupScene scene = Build("setuphold", $"[{Dial}]", "[]", DialRecord);
    string text = scene.Record(scene.Run(HoldMods), _folder);
    Assert.Equal(text, File.ReadAllText(committed));
    Assert.NotEqual("stale", text);
  }

  [Fact]
  public void Record_returns_the_text_of_an_equal_committed_file()
  {
    using (SetupScene first = Build("setuphold", $"[{Dial}]", "[]", DialRecord))
      first.Record(first.Run(HoldMods), _folder);
    string committed = File.ReadAllText(Path.Combine(_folder, "run.json"));
    using SetupScene again = Build("setuphold", $"[{Dial}]", "[]", DialRecord);
    Assert.Equal(committed, again.Record(again.Run(HoldMods), _folder));
    Assert.False(File.Exists(Path.Combine(_folder, "run.received.json")));
  }

  [Fact]
  public void Record_fails_naming_the_differing_line_and_writes_received()
  {
    string committed = Path.Combine(_folder, "run.json");
    using (SetupScene first = Build("setuphold", $"[{Dial}]", "[]", DialRecord))
      first.Record(first.Run(HoldMods), _folder);
    string[] lines = File.ReadAllLines(committed);
    int line = Array.FindIndex(lines, l => l.Contains("1.000"));
    Assert.True(line >= 0);
    lines[line] = lines[line].Replace("1.000", "9.000");
    File.WriteAllLines(committed, lines);
    using SetupScene again = Build("setuphold", $"[{Dial}]", "[]", DialRecord);
    SetupRecording recording = again.Run(HoldMods);
    var error = Assert.Throws<SetupRecordingMismatchException>(() =>
      again.Record(recording, _folder)
    );
    Assert.Contains($"line {line + 1}:", error.Message);
    Assert.True(File.Exists(Path.Combine(_folder, "run.received.json")));
  }
}
#endif
