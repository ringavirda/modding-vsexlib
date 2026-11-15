#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>A setup second runs each block entity listener at its own registered interval with that
/// interval as its dt, so a machine's work over the run does not depend on its tick length.</summary>
[Collection("Setups")]
public class SetupTickIntervalTests
{
  private static readonly string FixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupHold"
  );

  private static readonly IReadOnlyList<(string Mod, string Version)> Mods =
  [
    ("exlib", "0.8.4"),
    ("setuphold", "1.0.0"),
  ];

  private const int Seconds = 6;

  private static string Place(string id, int x) =>
    $"{{\"place\": {{\"id\": \"{id}\", \"code\": \"setuphold:{id}\", \"at\": [{x}, 0, 0]}}}}";

  private static string Work(string id) =>
    $"\"{id}\": {{\"series\": {{\"state\": {{\"words\": [\"idle\"]}}, \"work\": {{\"member\": \"SetupMill.Work\", \"unit\": \"u\"}}}}}}";

  private static SetupScene Build(params string[] mills) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setuphold\"], \"steps\": ["
          + string.Join(", ", mills.Select((id, x) => Place(id, x)))
          + "], \"seconds\": "
          + Seconds
          + ", \"record\": {\"machines\": {"
          + string.Join(", ", mills.Select(Work))
          + "}}}",
        "t.json"
      ),
      [FixturePath],
      null,
      w =>
      {
        SetupHoldTests.Register(w);
        w.RegisterBlockEntityBehaviorFactory(
          "SetupMill",
          be => new SetupMill(be)
        );
      }
    );

  private static double[] WorkOf(SetupRecording recording, string id) =>
    [
      .. Enumerable
        .Range(0, recording.Seconds)
        .Select(s => Convert.ToDouble(recording.ValueAt(id, "work", s))),
    ];

  // Fails when a setup second fires every listener once with dt 1: the 250 ms machine's catch-up clamp
  // then cuts each second to 0.5 s of work and it falls to half the 500 ms machine's pace.
  [Fact]
  public void A_250_ms_and_a_500_ms_machine_do_the_same_work_in_the_same_seconds()
  {
    using SetupScene scene = Build("quickmill", "slowmill");

    SetupRecording recording = scene.Run(Mods);

    double[] each = [.. Enumerable.Range(1, Seconds).Select(s => (double)s)];
    Assert.Equal(each, WorkOf(recording, "slowmill"));
    Assert.Equal(each, WorkOf(recording, "quickmill"));
  }

  // Fails when a listener over 1000 ms fires every second or drops the time it banked across seconds:
  // a 1500 ms machine fires 1.5, 3, 4.5 and 6 s into the run, mid-second and at a second's end.
  [Fact]
  public void A_1500_ms_machine_fires_on_its_interval_across_seconds()
  {
    using SetupScene scene = Build("longmill");

    SetupRecording recording = scene.Run(Mods);

    Assert.Equal([0, 1.5, 3, 3, 4.5, 6], WorkOf(recording, "longmill"));
  }
}
#endif
