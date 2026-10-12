#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Config;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Setups;

internal sealed class CaseConfig : IExVersionedConfig
{
  public string? ConfigVersion { get; set; }
  public int Level { get; set; } = 1;
  public float Rate { get; set; } = 0.5f;
  public bool Enabled { get; set; }
}

/// <summary>The harness cases a setup file makes: its checks in order, its variants' deltas and
/// the main case's <c>config</c> (setups.md, Checks and Variants).</summary>
[Collection("Setups")]
public class SetupCaseTests : IDisposable
{
  private readonly string _folder = Path.Combine(
    Path.GetTempPath(),
    "exlib-setup-case-" + Guid.NewGuid().ToString("N")
  );

  public void Dispose()
  {
    if (Directory.Exists(_folder))
      Directory.Delete(_folder, recursive: true);
  }

  internal static readonly string BoxPath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupBox"
  );

  private static readonly IReadOnlyList<(string Mod, string Version)> Mods =
  [
    ("exlib", "0.8.4"),
    ("setupbox", "1.0.0"),
  ];

  private const string PlaceA =
    "{\"place\": {\"id\": \"a\", \"code\": \"setupbox:box\", \"at\": [0, 0, 0]}}";
  private const string PlaceB =
    "{\"place\": {\"id\": \"b\", \"code\": \"setupbox:box\", \"at\": [1, 0, 0]}}";

  private static string SetA(int level) =>
    "{\"set\": {\"at\": [0, 0, 0], \"member\": {\"Level\": " + level + "}}}";

  internal static string Level(string id) =>
    "\""
    + id
    + "\": {\"series\": {\"state\": {\"words\": [\"idle\"]}, \"level\": {\"member\": \"Level\", \"unit\": \"u\"}}}";

  internal static string Check(string id, string op, string test) =>
    "{\"id\": \""
    + id
    + "\", \"says\": \""
    + id
    + " says\", \""
    + op
    + "\": "
    + test
    + "}";

  internal static string Series(string id, string key, int value) =>
    "{\"series\": \"" + id + ".level\", \"" + key + "\": " + value + "}";

  internal static string Cases(
    string variants,
    string checks = "",
    string config = "",
    string steps = "",
    string layout = "",
    string record = ""
  ) =>
    "{\"setup\": \"cases\", \"description\": \"d\", \"mods\": [\"setupbox\"], "
    + (config == "" ? "" : "\"config\": " + config + ", ")
    + (layout == "" ? "" : "\"layout\": " + layout + ", ")
    + "\"steps\": "
    + (steps == "" ? $"[{PlaceA}, {SetA(5)}, {PlaceB}]" : steps)
    + ", \"seconds\": 6, \"record\": {\"machines\": {"
    + (record == "" ? Level("a") + ", " + Level("b") : record)
    + "}}, \"checks\": ["
    + (
      checks == "" ? Check("hold", "always", Series("a", "equals", 5)) : checks
    )
    + "], \"variants\": {"
    + variants
    + "}}";

  private static SetupScene Build(
    SetupFile file,
    ExConfigRegister<CaseConfig>? store = null
  ) =>
    SetupScene.Build(
      file,
      [BoxPath],
      store == null ? null : _ => ExConfigProfiles.Register(store),
      Register
    );

  internal static void Register(TestWorld w)
  {
    w.RegisterClass("SetupBox", typeof(BlockEntity))
      .RegisterBlockEntityFactory(
        "SetupBox",
        () =>
        {
          var box = new SetupBox();
          box.Behaviors.Add(new BoxBehaviour(box));
          return box;
        }
      );
  }

  private static SetupFile Parse(string text) =>
    SetupFile.Parse(text, "cases.json");

  private static string? Judged(SetupFile file)
  {
    using SetupScene scene = Build(file);
    return scene.Judge(scene.Run(Mods));
  }

  private static double[] Levels(SetupRecording recording, string id) =>
    Enumerable
      .Range(0, recording.Seconds)
      .Select(s => Convert.ToDouble(recording.ValueAt(id, "level", s)))
      .ToArray();

  #region Checks

  // Fails when the checks run last to first, or the message names no check.
  [Fact]
  public void The_first_failing_check_in_file_order_fails_the_case()
  {
    string checks = string.Join(
      ", ",
      Check("hold", "always", Series("a", "equals", 5)),
      Check("low", "never", Series("a", "min", 0)),
      Check("high", "never", Series("a", "min", 1))
    );

    string? verdict = Judged(Parse(Cases("", checks)));

    Assert.NotNull(verdict);
    Assert.StartsWith("check low (low says) fails at second 0", verdict);
  }

  // Fails when a case whose checks all hold is judged as failing.
  [Fact]
  public void A_case_whose_checks_hold_passes()
  {
    Assert.Null(Judged(Parse(Cases(""))));
  }

  private const string LiveLevel =
    "{\"at\": [0, 0, 0], \"member\": \"Level\", \"equals\": 5}";

  // Fails when a check reading the world is not sampled each second of the run.
  [Fact]
  public void A_check_reading_the_world_is_judged_on_each_second_s_sample()
  {
    SetupFile file = Parse(
      Cases(
        "\"lowered\": {\"after\": {\"b\": [" + SetA(2) + "]}}",
        Check("live", "always", LiveLevel)
      )
    );

    using SetupScene main = Build(file);
    using SetupScene lowered = Build(file.ForVariant("lowered"));

    Assert.Null(main.Judge(main.Run(Mods)));
    Assert.StartsWith(
      "check live (live says) fails at second 0",
      lowered.Judge(lowered.Run(Mods))
    );
  }

  // Fails when a second run of a scene keeps the samples of the first.
  [Fact]
  public void A_scene_run_twice_samples_its_checks_afresh()
  {
    SetupFile file = Parse(Cases("", Check("live", "always", LiveLevel)));

    using SetupScene scene = Build(file);
    scene.Run(Mods);

    Assert.Null(scene.Judge(scene.Run(Mods)));
  }

  #endregion

  #region Variants

  // Fails when a dropped step id still places its block.
  [Fact]
  public void A_variant_drops_a_step_by_id()
  {
    SetupFile file = Parse(
      Cases(
        "\"no-b\": {\"drop\": [\"b\"], \"checks\": ["
          + Check("kept", "always", Series("a", "equals", 5))
          + "]}",
        record: Level("a") + ", " + Level("b")
      )
    );

    using SetupScene main = Build(file);
    using SetupScene variant = Build(file.ForVariant("no-b"));

    Assert.Equal(1, main.PositionOf("b").X);
    var error = Assert.Throws<InvalidOperationException>(() =>
      variant.PositionOf("b")
    );
    Assert.Contains("\"b\" has placed nothing", error.Message);
  }

  // Fails when the recording of a dropped id is still sampled, so its cell is read empty.
  [Fact]
  public void A_variant_that_drops_a_recorded_machine_does_not_sample_it()
  {
    SetupFile file = Parse(Cases("\"no-b\": {\"drop\": [\"b\"]}"));

    using SetupScene variant = Build(file.ForVariant("no-b"));
    SetupRecording recording = variant.Run(Mods);

    Assert.Equal([5, 5, 5, 5, 5, 5], Levels(recording, "a"));
    Assert.False(recording.Has("b", "level"));
  }

  // Fails when a check reading a dropped id's series is accepted.
  [Fact]
  public void A_check_reading_a_dropped_machine_fails_the_variant()
  {
    SetupFile file = Parse(
      Cases(
        "\"no-b\": {\"drop\": [\"b\"]}",
        Check("hold", "always", Series("b", "equals", 0))
      )
    );

    var error = Assert.Throws<InvalidOperationException>(() =>
      file.ForVariant("no-b")
    );
    Assert.Contains("no-b", error.Message);
    Assert.Contains("b.level", error.Message);
  }

  // Fails when a replaced step id keeps its first step.
  [Fact]
  public void A_variant_replaces_a_step_by_id()
  {
    SetupFile file = Parse(
      Cases(
        "\"far-b\": {\"replace\": {\"b\": {\"place\": {\"id\": \"b\", \"code\": \"setupbox:box\", \"at\": [3, 0, 0]}}}}"
      )
    );

    using SetupScene main = Build(file);
    using SetupScene variant = Build(file.ForVariant("far-b"));

    Assert.Equal(1, main.PositionOf("b").X);
    Assert.Equal(3, variant.PositionOf("b").X);
  }

  // Fails when steps inserted after an id run at the end of the steps, or not after that step.
  [Fact]
  public void A_variant_inserts_steps_directly_after_a_step_id()
  {
    SetupFile file = Parse(
      Cases(
        "\"early\": {\"after\": {\"a\": ["
          + SetA(9)
          + "]}}, "
          + "\"late\": {\"after\": {\"b\": ["
          + SetA(9)
          + "]}}"
      )
    );

    using SetupScene early = Build(file.ForVariant("early"));
    using SetupScene late = Build(file.ForVariant("late"));

    Assert.Equal([5, 5, 5, 5, 5, 5], Levels(early.Run(Mods), "a"));
    Assert.Equal([9, 9, 9, 9, 9, 9], Levels(late.Run(Mods), "a"));
  }

  // Fails when steps after a layout entry are dropped or run after the file's steps.
  [Fact]
  public void A_variant_inserts_steps_after_a_layout_entry_before_the_steps()
  {
    const string layout =
      "{\"legend\": {\"B\": {\"id\": \"lay\", \"code\": \"setupbox:box\"}}, \"layers\": {\"1\": [\"B\"]}}";
    SetupFile file = Parse(
      Cases(
        "\"seeded\": {\"after\": {\"lay\": [{\"set\": {\"at\": {\"of\": \"lay\"}, \"member\": {\"Level\": 9}}}]}}",
        Check("hold", "always", Series("lay", "equals", 9)),
        steps: "[{\"expect\": {\"at\": {\"of\": \"lay\"}, \"member\": \"Level\", \"equals\": 9}}]",
        layout: layout,
        record: Level("lay")
      )
    );

    using SetupScene variant = Build(file.ForVariant("seeded"));

    Assert.Equal([9, 9, 9, 9, 9, 9], Levels(variant.Run(Mods), "lay"));
  }

  // Fails when steps after a layout entry wait until every entry is placed.
  [Fact]
  public void A_variant_runs_the_steps_after_an_entry_before_the_next_entry_is_placed()
  {
    const string layout =
      "{\"legend\": {\"B\": {\"id\": \"first\", \"code\": \"setupbox:box\"}, \"C\": {\"id\": \"second\", \"code\": \"setupbox:box\"}}, \"layers\": {\"1\": [\"BC\"]}}";
    const string seesSecond =
      "{\"expect\": {\"at\": {\"of\": \"second\"}, \"member\": \"Level\", \"equals\": 0}}";
    SetupFile file = Parse(
      Cases(
        "\"early\": {\"after\": {\"first\": ["
          + seesSecond
          + "]}}, \"late\": {\"after\": {\"second\": ["
          + seesSecond
          + "]}}",
        Check("hold", "always", Series("first", "equals", 0)),
        steps: "[]",
        layout: layout,
        record: Level("first")
      )
    );

    using SetupScene late = Build(file.ForVariant("late"));
    var early = Assert.Throws<InvalidOperationException>(() =>
      Build(file.ForVariant("early"))
    );

    Assert.NotNull(late);
    Assert.Contains("second", early.Message);
  }

  // Fails when a dropped layout entry's after steps still run, or its record is still sampled.
  [Fact]
  public void A_variant_that_drops_a_layout_entry_drops_the_steps_after_it()
  {
    const string layout =
      "{\"legend\": {\"B\": {\"id\": \"lay\", \"code\": \"setupbox:box\"}}, \"layers\": {\"1\": [\"B\"]}}";
    SetupFile file = Parse(
      Cases(
        "\"bare\": {\"drop\": [\"lay\"], \"after\": {\"lay\": [{\"set\": {\"at\": {\"of\": \"lay\"}, \"member\": {\"Level\": 9}}}]}, \"checks\": []}",
        Check("hold", "always", Series("a", "equals", 5)),
        layout: layout,
        record: Level("a") + ", " + Level("lay")
      )
    );

    using SetupScene bare = Build(file.ForVariant("bare"));

    Assert.Equal([5, 5, 5, 5, 5, 5], Levels(bare.Run(Mods), "a"));
  }

  // Fails when a step inside a repeat is out of reach of a variant's drop.
  [Fact]
  public void A_variant_drops_a_step_inside_a_repeat()
  {
    string steps =
      $"[{PlaceA}, {{\"repeat\": {{\"times\": 1, \"steps\": [{{\"place\": {{\"id\": \"c\", \"code\": \"setupbox:box\", \"at\": [2, 0, 0]}}}}]}}}}, {PlaceB}]";
    SetupFile file = Parse(
      Cases("\"no-c\": {\"drop\": [\"c\"]}", steps: steps)
    );

    using SetupScene main = Build(file);
    using SetupScene variant = Build(file.ForVariant("no-c"));

    Assert.Equal(2, main.PositionOf("c").X);
    Assert.Throws<InvalidOperationException>(() => variant.PositionOf("c"));
  }

  // Fails when a machine replaced by another id is still sampled under its old one.
  [Fact]
  public void A_variant_that_replaces_a_recorded_id_does_not_sample_the_old_one()
  {
    const string layout =
      "{\"legend\": {\"B\": {\"id\": \"lay\", \"code\": \"setupbox:box\"}}, \"layers\": {\"1\": [\"B\"]}}";
    SetupFile file = Parse(
      Cases(
        "\"far-b\": {\"replace\": {\"b\": {\"place\": {\"id\": \"far\", \"code\": \"setupbox:box\", \"at\": [3, 0, 0]}}}}, "
          + "\"new-lay\": {\"replace\": {\"lay\": {\"id\": \"other\", \"code\": \"setupbox:box\"}}}",
        layout: layout,
        record: Level("a") + ", " + Level("b") + ", " + Level("lay")
      )
    );

    using SetupScene far = Build(file.ForVariant("far-b"));
    using SetupScene other = Build(file.ForVariant("new-lay"));
    SetupRecording byStep = far.Run(Mods);
    SetupRecording byEntry = other.Run(Mods);

    Assert.False(byStep.Has("b", "level"));
    Assert.True(byStep.Has("lay", "level"));
    Assert.False(byEntry.Has("lay", "level"));
    Assert.True(byEntry.Has("b", "level"));
  }

  // Fails when a variant's own checks are replaced by the main case's.
  [Fact]
  public void A_variant_s_own_checks_replace_the_main_case_s()
  {
    SetupFile file = Parse(
      Cases(
        "\"two\": {\"after\": {\"b\": ["
          + SetA(2)
          + "]}, \"checks\": ["
          + Check("lowered", "always", Series("a", "equals", 2))
          + "]}"
      )
    );

    Assert.Null(Judged(file.ForVariant("two")));
  }

  // Fails when a variant's seconds, its steady window or its record name is ignored.
  [Fact]
  public void A_variant_overrides_seconds_steady_and_record()
  {
    SetupFile file = Parse(
      Cases(
        "\"short\": {\"seconds\": 4, \"steady\": [1, 4], \"record\": \"cases-short\"}"
      )
    );

    using SetupScene main = Build(file);
    using SetupScene variant = Build(file.ForVariant("short"));
    SetupRecording shortRun = variant.Run(Mods);
    variant.Record(shortRun, _folder);

    Assert.Equal(6, main.Run(Mods).Seconds);
    Assert.Equal(4, shortRun.Seconds);
    Assert.Equal((1, 4), (shortRun.SteadyFrom, shortRun.SteadyTo));
    Assert.True(File.Exists(Path.Combine(_folder, "cases-short.json")));
    Assert.False(File.Exists(Path.Combine(_folder, "cases.json")));
  }

  // Fails when a variant that gives seconds alone keeps the main case's steady window.
  [Fact]
  public void A_variant_with_seconds_alone_takes_the_last_third_as_its_steady_window()
  {
    SetupFile file = Parse(Cases("\"long\": {\"seconds\": 9}"));

    using SetupScene variant = Build(file.ForVariant("long"));
    SetupRecording recording = variant.Run(Mods);

    Assert.Equal((6, 9), (recording.SteadyFrom, recording.SteadyTo));
  }

  // Fails when a config value that is not a number, a boolean or text is accepted.
  [Fact]
  public void A_config_value_that_is_not_a_scalar_is_refused()
  {
    var error = Assert.Throws<InvalidOperationException>(() =>
      Parse(Cases("", config: "{\"casesmod\": {\"Level\": null}}"))
    );
    Assert.Throws<InvalidOperationException>(() =>
      Parse(Cases("", config: "{\"casesmod\": {\"Level\": {\"a\": 1}}}"))
    );

    Assert.Contains("config.casesmod.Level", error.Message);
  }

  // Fails when a variant without record is taken to write one, or the main case is not.
  [Fact]
  public void Only_the_main_case_and_a_variant_with_record_write_a_recording()
  {
    SetupFile file = Parse(
      Cases(
        "\"plain\": {\"drop\": [\"b\"]}, \"kept\": {\"record\": \"cases-kept\"}"
      )
    );

    using SetupScene main = Build(file);
    using SetupScene plain = Build(file.ForVariant("plain"));
    using SetupScene kept = Build(file.ForVariant("kept"));

    Assert.True(main.RecordsOwn);
    Assert.False(plain.RecordsOwn);
    Assert.True(kept.RecordsOwn);
  }

  // Fails when a variant that names no check of the file is accepted by ForVariant.
  [Fact]
  public void A_variant_the_file_does_not_hold_is_refused()
  {
    SetupFile file = Parse(Cases("\"one\": {\"drop\": [\"b\"]}"));

    Assert.Throws<ArgumentException>(() => file.ForVariant("nothing"));
  }

  [Fact]
  public void The_variant_names_are_listed_in_file_order()
  {
    SetupFile file = Parse(
      Cases("\"one\": {\"drop\": [\"b\"]}, \"two\": {\"drop\": [\"b\"]}")
    );

    Assert.Equal(["one", "two"], file.VariantNames);
  }

  #endregion

  #region fails

  private const string Late =
    "\"after\": {\"b\": [{\"set\": {\"at\": [0, 0, 0], \"member\": {\"Level\": 2}}}]}";

  private static string Pair() =>
    string.Join(
      ", ",
      Check("hold", "always", Series("a", "equals", 5)),
      Check("zero", "always", Series("b", "equals", 0))
    );

  // Fails when a variant passes only when the named check is not the first to fail.
  [Fact]
  public void A_variant_with_fails_passes_when_that_check_fails_first()
  {
    SetupFile file = Parse(
      Cases("\"low\": {" + Late + ", \"fails\": \"hold\"}", Pair())
    );

    Assert.Null(Judged(file.ForVariant("low")));
  }

  // Fails when a variant passes although another check than the named one fails first.
  [Fact]
  public void A_variant_with_fails_fails_when_another_check_fails_first()
  {
    SetupFile file = Parse(
      Cases("\"low\": {" + Late + ", \"fails\": \"zero\"}", Pair())
    );

    string? verdict = Judged(file.ForVariant("low"));

    Assert.NotNull(verdict);
    Assert.StartsWith("check zero should fail first, but check hold", verdict);
  }

  // Fails when a variant with fails passes although no check fails.
  [Fact]
  public void A_variant_with_fails_fails_when_no_check_fails()
  {
    SetupFile file = Parse(
      Cases(
        "\"same\": {\"after\": {\"b\": ["
          + SetA(5)
          + "]}, \"fails\": \"hold\"}",
        Pair()
      )
    );

    Assert.Equal(
      "check hold should fail first, and no check fails",
      Judged(file.ForVariant("same"))
    );
  }

  #endregion

  #region config

  private static ExConfigRegister<CaseConfig> Store() =>
    new("cases.json", "casesmod");

  // Fails when the main case's config is not written through the register.
  [Fact]
  public void The_main_case_config_is_applied_while_the_scene_lives()
  {
    ExConfigRegister<CaseConfig> store = Store();
    SetupFile file = Parse(
      Cases(
        "",
        config: "{\"casesmod\": {\"Level\": 7, \"Rate\": 2.5, \"Enabled\": true}}"
      )
    );

    using SetupScene scene = Build(file, store);

    Assert.Equal(7, store.Config.Level);
    Assert.Equal(2.5f, store.Config.Rate);
    Assert.True(store.Config.Enabled);
  }

  // Fails when the register keeps the case's values after the scene is disposed.
  [Fact]
  public void The_register_is_restored_after_the_case()
  {
    ExConfigRegister<CaseConfig> store = Store();
    SetupFile file = Parse(
      Cases("", config: "{\"casesmod\": {\"Level\": 7, \"Rate\": 2.5}}")
    );

    SetupScene scene = Build(file, store);
    scene.Dispose();

    Assert.Equal(1, store.Config.Level);
    Assert.Equal(0.5f, store.Config.Rate);
  }

  // Fails when a build that fails after the config is written leaves the register changed.
  [Fact]
  public void A_build_that_fails_restores_the_register()
  {
    ExConfigRegister<CaseConfig> store = Store();
    SetupFile file = Parse(
      Cases(
        "",
        config: "{\"casesmod\": {\"Level\": 7}}",
        steps: $"[{PlaceA}, {PlaceB}, {{\"set\": {{\"at\": [5, 0, 0], \"member\": {{\"Level\": 1}}}}}}]"
      )
    );

    Assert.Throws<InvalidOperationException>(() => Build(file, store));

    Assert.Equal(1, store.Config.Level);
  }

  // Fails when a variant's key does not override the main case's, or drops its other keys.
  [Fact]
  public void A_variant_config_overrides_the_main_case_s_key_and_keeps_the_rest()
  {
    ExConfigRegister<CaseConfig> store = Store();
    SetupFile file = Parse(
      Cases(
        "\"strong\": {\"config\": {\"casesmod\": {\"Level\": 9}}}",
        config: "{\"casesmod\": {\"Level\": 7, \"Rate\": 2.5}}"
      )
    );

    using SetupScene variant = Build(file.ForVariant("strong"), store);

    Assert.Equal(9, store.Config.Level);
    Assert.Equal(2.5f, store.Config.Rate);
  }

  // Fails when a key the register does not hold is skipped.
  [Fact]
  public void A_config_key_the_register_does_not_hold_fails_the_build()
  {
    SetupFile file = Parse(
      Cases("", config: "{\"casesmod\": {\"Missing\": 7}}")
    );

    var error = Assert.Throws<InvalidOperationException>(() =>
      Build(file, Store())
    );

    Assert.Contains("config.casesmod.Missing", error.Message);
    Assert.Contains("no value \"Missing\"", error.Message);
  }

  // Fails when a mod without a register is skipped.
  [Fact]
  public void A_config_of_a_mod_without_a_register_fails_the_build()
  {
    SetupFile file = Parse(Cases("", config: "{\"nomod\": {\"Level\": 7}}"));

    var error = Assert.Throws<InvalidOperationException>(() =>
      Build(file, Store())
    );

    Assert.Contains("\"nomod\" has no config register", error.Message);
  }

  // Fails when a value the register refuses is skipped.
  [Fact]
  public void A_config_value_the_register_refuses_fails_the_build()
  {
    SetupFile file = Parse(
      Cases("", config: "{\"casesmod\": {\"Level\": \"high\"}}")
    );

    var error = Assert.Throws<InvalidOperationException>(() =>
      Build(file, Store())
    );

    Assert.Contains("ParseFailed", error.Message);
  }

  #endregion
}
#endif
