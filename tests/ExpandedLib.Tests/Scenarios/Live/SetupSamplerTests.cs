using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing.Live;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The live run's seconds: the sampler's firings, its actions and the recording it
/// writes.</summary>
public class SetupSamplerTests : IDisposable {
  private const string Place =
    """{ "place": { "id": "b", "code": "game:stone", "at": [1, 0, 0] } }""";
  private const string Record = """
    { "machines": { "a": { "series": { "state": { "words": [
      { "when": { "at": [1, 0, 0], "block": "game:stone" }, "word": "set" }, "unset" ] } } } } }
    """;

  private readonly string _folder = Path.Combine(
    Path.GetTempPath(),
    "exlib-sampler-" + Guid.NewGuid().ToString("N")
  );

  public void Dispose() {
    if (Directory.Exists(_folder))
      Directory.Delete(_folder, true);
  }

  private sealed class Rig {
    public LiveFixture Fixture { get; } = new();
    public IServerEventAPI Events { get; } = Substitute.For<IServerEventAPI>();
    public Action<float>? Fire { get; private set; }
    public int Period { get; private set; }
    public SetupSampler Sampler { get; }
    public ScenarioCase Case { get; }

    public Rig(
      string during,
      string folder,
      string extra = "{}",
      string[]? loaded = null,
      ModInfo[]? infos = null
    ) {
      Events
        .RegisterGameTickListener(
          Arg.Any<Action<float>>(),
          Arg.Any<int>(),
          Arg.Any<int>()
        )
        .Returns(call => {
          Fire = call.Arg<Action<float>>();
          Period = call.ArgAt<int>(1);
          return 7L;
        });
      string text = LiveFixture.Text(
        """[{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } }]""",
        extra == "{}"
          ? $$"""{ "during": {{during}}, "record": {{Record}} }"""
          : extra
      );
      Case = ScenarioCase.Load(
        "exlib/case",
        text,
        "case.json",
        loaded ?? [],
        "1.22"
      );
      Sampler = new SetupSampler(
        Events,
        Case,
        Fixture.Context,
        "1.22",
        infos ?? [],
        folder
      );
    }

    public void Begin() {
      foreach (
        bool _ in ScenarioSteps.Run(Fixture.Context, Case.Model.Steps, "")
      )
        Fixture.Clock += 0.5;
      Sampler.Start();
    }

    public void Fires(int times) {
      for (int i = 0; i < times; i++)
        Fire!(1f);
    }
  }

  /// <summary>Fails when a firing runs its second's actions before its sample, which shows an
  /// action due at second 2 at sample 1.</summary>
  [Fact]
  public void An_action_due_at_a_second_shows_in_that_seconds_sample() {
    var rig = new Rig($"[{{ \"at\": 2, \"step\": {Place} }}]", _folder);
    rig.Begin();
    Assert.Equal(1000, rig.Period);
    rig.Fires(6);
    Assert.True(rig.Sampler.Done);
    Assert.Equal("unset", rig.Sampler.Recording!.ValueAt("a", "state", 1));
    Assert.Equal("set", rig.Sampler.Recording.ValueAt("a", "state", 2));
  }

  /// <summary>Fails when a block a <c>during</c> action places is not handed to the case's context,
  /// so the end-of-run checks never see it.</summary>
  [Fact]
  public void A_block_a_during_action_places_reaches_the_cases_placed_cells() {
    var rig = new Rig($"[{{ \"at\": 2, \"step\": {Place} }}]", _folder);
    rig.Begin();
    rig.Fires(3);
    Assert.Contains(new BlockPos(1, 0, 0), rig.Fixture.Context.Placed);
  }

  /// <summary>Fails when the sampler waits on an action that runs past its firing, which leaves
  /// fewer than the file's seconds sampled.</summary>
  [Fact]
  public void A_held_action_does_not_stop_the_samples() {
    var rig = new Rig(
      """[{ "at": 1, "step": { "wait": 100 } }, { "at": 2, "step": { "wait": 100 } }]""",
      _folder
    );
    rig.Begin();
    rig.Fires(6);
    Assert.True(rig.Sampler.Done);
    Assert.Equal(6, rig.Sampler.Recording!.Samples);
    Assert.Null(rig.Sampler.Failure);
  }

  /// <summary>Fails when an action's ok line reaches the case's output (the actions run on the
  /// shared context), or its failing step's FAIL line does not.</summary>
  [Fact]
  public void An_action_prints_only_when_it_fails() {
    var rig = new Rig(
      $$"""
      [{ "at": 2, "step": {{Place}} },
       { "at": 3, "step": { "expect": { "at": [0, 0, 0], "block": "game:air" } } }]
      """,
      _folder
    );
    rig.Begin();
    int before = rig.Fixture.Lines.Count;
    rig.Fires(4);
    Assert.NotNull(rig.Sampler.Failure);
    Assert.True(rig.Sampler.Failure.Reported);
    string[] lines = [.. rig.Fixture.Lines.Skip(before)];
    Assert.DoesNotContain(lines, ScenarioOutput.IsStepOk);
    Assert.Contains(
      lines,
      l => l.Contains(" d2@3.1 expect ") && l.Contains("FAIL")
    );
    Assert.False(rig.Sampler.Done);
  }

  /// <summary>Fails when the early end writes the full length (4 samples written as 6 seconds), or
  /// writes under 3 samples.</summary>
  [Fact]
  public void A_run_that_fails_early_writes_the_seconds_it_sampled() {
    var rig = new Rig(
      """[{ "at": 4, "step": { "expect": { "at": [0, 0, 0], "block": "game:air" } } }]""",
      _folder
    );
    rig.Begin();
    rig.Fires(5);
    Assert.NotNull(rig.Sampler.Failure);
    Assert.True(rig.Sampler.Save());
    string text = File.ReadAllText(Path.Combine(_folder, "case.json"));
    Assert.Contains("\"seconds\":4,\"steady\":[3,4]", text);

    var early = new Rig(
      """[{ "at": 1, "step": { "expect": { "at": [0, 0, 0], "block": "game:air" } } }]""",
      Path.Combine(_folder, "early")
    );
    early.Begin();
    early.Fires(2);
    Assert.False(early.Sampler.Save());
    Assert.False(Directory.Exists(Path.Combine(_folder, "early")));
  }

  /// <summary>Fails when the mods map is built from the file's ids at exlib's own version: the
  /// fixture's siex requires exlib 0.5.0, and exlib itself is 0.8.4.</summary>
  [Fact]
  public void The_recording_carries_the_game_and_the_loaded_mods() {
    ModInfo[] infos =
    [
      new() { ModID = "exlib", Version = "0.8.4" },
      new()
      {
        ModID = "siex",
        Version = "0.9.9",
        Dependencies = [new ModDependency("exlib", "0.5.0")],
      },
    ];
    var rig = new Rig(
      "[]",
      _folder,
      $$"""{ "mods": ["siex"], "record": {{Record}} }""",
      ["siex"],
      infos
    );
    rig.Begin();
    rig.Fires(6);
    Assert.True(rig.Sampler.Save());
    string text = File.ReadAllText(Path.Combine(_folder, "case.json"));
    Assert.Contains("\"game\":\"1.22\"", text);
    Assert.Contains("\"mods\":{\"exlib\":\"0.5.0\",\"siex\":\"0.9.9\"}", text);
  }

  /// <summary>Fails when Stop leaves the listener registered.</summary>
  [Fact]
  public void Stopping_removes_the_listener() {
    var rig = new Rig("[]", _folder);
    rig.Begin();
    rig.Sampler.Stop();
    rig.Events.Received(1).UnregisterGameTickListener(7L);
    rig.Sampler.Stop();
    rig.Events.Received(1).UnregisterGameTickListener(7L);
  }

  private static (ScenarioCaseRun Run, List<bool> Ticks) Drive(
    Rig rig,
    Action<int>? onTick = null,
    int fireEvery = 2
  ) {
    var run = new ScenarioCaseRun(rig.Case, rig.Fixture.Context, rig.Sampler);
    var ticks = new List<bool>();
    foreach (bool _ in run.Run()) {
      ticks.Add(true);
      rig.Fixture.Clock += 0.5;
      onTick?.Invoke(ticks.Count);
      if (ticks.Count % fireEvery == 0)
        rig.Fire?.Invoke(1f);
      if (ticks.Count > 2000)
        break;
    }
    return (run, ticks);
  }

  /// <summary>Fails when a passing run prints no record line, writes no file, or its line drops
  /// the seconds or the path.</summary>
  [Fact]
  public void A_case_run_writes_its_recording_and_prints_the_record_line() {
    var rig = new Rig($"[{{ \"at\": 2, \"step\": {Place} }}]", _folder);
    (ScenarioCaseRun run, _) = Drive(rig);
    Assert.True(run.Passed);
    Assert.Contains(
      "[scenario]   run record 6 s, Scenarios/recordings/case.json ok",
      rig.Fixture.Lines
    );
    Assert.Equal(
      "set",
      SetupRecording
        .Parse(File.ReadAllText(Path.Combine(_folder, "case.json")))
        .ValueAt("a", "state", 3)
    );
  }

  /// <summary>Fails when a failing action ends the run without the case failing at the step after
  /// the last, or without the recording written.</summary>
  [Fact]
  public void A_failing_action_fails_the_case_and_still_writes_the_recording() {
    var rig = new Rig(
      """[{ "at": 4, "step": { "expect": { "at": [0, 0, 0], "block": "game:air" } } }]""",
      _folder
    );
    (ScenarioCaseRun run, _) = Drive(rig);
    Assert.False(run.Passed);
    Assert.Equal(2, run.FailedStep);
    Assert.True(File.Exists(Path.Combine(_folder, "case.json")));
    Assert.DoesNotContain(rig.Fixture.Lines, l => l.Contains(" run record "));
  }

  /// <summary>Fails when a when test the world cannot answer is not reported as a failed run line.</summary>
  [Fact]
  public void A_reading_the_run_cannot_take_fails_the_case_with_a_run_line() {
    var rig = new Rig(
      $$"""[{ "every": 1, "when": { "at": [5, 5, 5], "member": "Missing", "equals": 1 }, "step": {{Place}} }]""",
      _folder
    );
    (ScenarioCaseRun run, _) = Drive(rig);
    Assert.False(run.Passed);
    Assert.Contains(
      rig.Fixture.Lines,
      l => l.Contains(" run record FAIL: ") && l.Contains("during")
    );
  }

  /// <summary>Fails when a run that never finishes its seconds is not failed at the timeout.</summary>
  [Fact]
  public void A_run_that_outlasts_the_timeout_fails() {
    var rig = new Rig(
      "[]",
      _folder,
      $$"""{ "timeout": 130, "record": {{Record}} }"""
    );
    (ScenarioCaseRun run, _) = Drive(rig, fireEvery: 100000);
    Assert.False(run.Passed);
    Assert.Contains(
      rig.Fixture.Lines,
      l => l.Contains(" run record FAIL: still running after 130 s")
    );
  }

  /// <summary>Fails when a recordings folder that cannot be written does not fail the case.</summary>
  [Fact]
  public void A_recording_that_cannot_be_written_fails_the_case() {
    Directory.CreateDirectory(_folder);
    string blocked = Path.Combine(_folder, "blocked");
    File.WriteAllText(blocked, "");
    var rig = new Rig("[]", blocked);
    (ScenarioCaseRun run, _) = Drive(rig);
    Assert.False(run.Passed);
    Assert.Contains(rig.Fixture.Lines, l => l.Contains(" run record FAIL: "));
  }

  private static Rig Packed(
    string folder,
    List<Entity> spawned,
    out EntityItem flint
  ) {
    var rig = new Rig(
      "",
      folder,
      $$"""{ "pack": { "scope": "stacks", "gathered": {} }, "during": [{ "at": 2, "step": {{Place}} }], "record": {{Record}} }"""
    );
    rig.Fixture.Context.Pack = SetupPack.Start(rig.Case.Model, _ => null);
    Item item = rig.Fixture.Test.RegisterItem("game:flint");
    flint = new EntityItem { EntityId = 7, Itemstack = new ItemStack(item, 1) };
    flint.Pos.SetPos(0.5, 0.5, 0.5);
    rig.Fixture.Test.World.GetEntitiesInsideCuboid(
        Arg.Any<BlockPos>(),
        Arg.Any<BlockPos>(),
        Arg.Any<ActionConsumable<Entity>>()
      )
      .Returns(call =>
        spawned.Where(e => call.ArgAt<ActionConsumable<Entity>>(2)(e)).ToArray()
      );
    return rig;
  }

  /// <summary>Fails when the entities an action's context saw spawn are not merged back into the
  /// case's context, which a later step then gives again.</summary>
  [Fact]
  public void An_item_a_during_action_sees_spawn_is_marked_seen_on_the_cases_context() {
    var spawned = new List<Entity>();
    Rig rig = Packed(_folder, spawned, out EntityItem flint);
    rig.Begin();
    spawned.Add(flint);
    rig.Fires(3);
    Assert.Contains(7L, rig.Fixture.Context.SpawnsSeen);
    Assert.Equal(1, rig.Fixture.Context.Pack!.Held("game:flint"));
  }

  /// <summary>Fails when an action's context starts without the entities the case's context has
  /// seen, which gives an item spawned before the action a second time.</summary>
  [Fact]
  public void An_item_the_case_has_given_is_not_given_again_by_a_during_action() {
    var spawned = new List<Entity>();
    Rig rig = Packed(_folder, spawned, out EntityItem flint);
    spawned.Add(flint);
    rig.Begin();
    Assert.Equal(1, rig.Fixture.Context.Pack!.Held("game:flint"));
    rig.Fires(3);
    Assert.Equal(1, rig.Fixture.Context.Pack.Held("game:flint"));
  }
}
