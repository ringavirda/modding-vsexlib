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
    string record
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
          + "}",
        "run.json"
      ),
      [ModPath(mod)],
      null,
      w =>
        w.RegisterClass("SetupBox", typeof(BlockEntity))
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
          .RegisterClass("SetupHoldBox", typeof(SetupHoldBox))
          .RegisterBlockEntityFactory("SetupHoldBox", () => new SetupHoldBox())
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

  [Fact]
  public void Record_writes_the_setup_when_EX_WRITE_SETUPS_names_it()
  {
    Directory.CreateDirectory(_folder);
    string committed = Path.Combine(_folder, "run.json");
    File.WriteAllText(committed, "stale");
    Environment.SetEnvironmentVariable("EX_WRITE_SETUPS", "1");
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
