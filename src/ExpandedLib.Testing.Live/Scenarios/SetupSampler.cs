using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Testing.Live;

/// <summary>The seconds of a case's run, driven by a 1000 ms game tick listener: each firing
/// samples the second that just ticked, then pulls the next second's <c>during</c> actions once
/// and takes its <c>Before</c> readings. An action that runs on past its firing continues on
/// <see cref="Advance"/> while the samples go on, and the actions due meanwhile queue behind it.</summary>
internal sealed class SetupSampler(
  IServerEventAPI events,
  ScenarioCase scenario,
  ScenarioContext ctx,
  string game,
  IReadOnlyList<ModInfo> loaded,
  string folder
) {
  private const int PeriodMilliseconds = 1000;

  private readonly Queue<(int Second, IEnumerator<SetupDuring> Due)> _queued = [];
  private SetupRun? _run;
  private ScenarioContext? _actions;
  private IEnumerator<bool>? _acting;
  private long _listener;
  private bool _registered;
  private int _fired;

  /// <summary>The failure that stopped the run, or null; its FAIL line is printed unless
  /// <see cref="StepFailure.Reported"/> is false.</summary>
  public StepFailure? Failure { get; private set; }

  /// <summary>Whether every second of the file's <c>seconds</c> is sampled.</summary>
  public bool Done => _fired >= scenario.Model.Seconds;

  /// <summary>What the run has recorded; null before <see cref="Start"/>.</summary>
  public SetupRecording? Recording => _run?.Recording;

  /// <summary>The recording's path under the server's data path, as the output line names it.</summary>
  public string RecordPath => $"Scenarios/recordings/{scenario.Model.RecordName}.json";

  /// <summary>The recording's <c>mods</c> map over the loaded mods, in the file's order.</summary>
  /// <exception cref="InvalidOperationException">A listed mod is not among <paramref name="loaded"/>.</exception>
  public static IReadOnlyList<(string Mod, string Version)> ModMap(
    IReadOnlyList<string> mods,
    IReadOnlyList<ModInfo> loaded
  ) =>
    SetupMods.Map(
      mods,
      id => {
        ModInfo info = loaded.First(m =>
          string.Equals(m.ModID, id, StringComparison.OrdinalIgnoreCase)
        );
        return new SetupModVersions(
          info.Version,
          info.Dependencies?.FirstOrDefault(d => d.ModID == "exlib")?.Version
        );
      },
      loaded.FirstOrDefault(m => m.ModID == "exlib")?.Version ?? ""
    );

  /// <summary>Takes the first readings, runs second 0's actions once and registers the
  /// listener. A failure is left in <see cref="Failure"/>.</summary>
  public void Start() {
    _actions = new ScenarioContext(
      ctx.World,
      ctx.Setup,
      () => ctx.Now,
      ctx.Errors,
      line => {
        if (!ScenarioOutput.IsStepOk(line))
          ctx.Line(line);
      },
      ctx.File
    ) {
      Player = ctx.Player,
      Pack = ctx.Pack,
    };
    Guard(() => {
      _run = new SetupRun(
        scenario.Model,
        ctx.Setup,
        game,
        ModMap(scenario.Model.Mods, loaded)
      );
      _run.Start();
      _listener = events.RegisterGameTickListener(Fire, PeriodMilliseconds);
      _registered = true;
      Second(0);
    });
  }

  /// <summary>Continues the action that ran past its firing, then the ones queued behind it.</summary>
  public void Advance() => Guard(Pump);

  /// <summary>Removes the listener and drops an unfinished action.</summary>
  public void Stop() {
    if (_registered) {
      events.UnregisterGameTickListener(_listener);
      _registered = false;
    }
    _acting?.Dispose();
    _acting = null;
    while (_queued.Count > 0)
      _queued.Dequeue().Due.Dispose();
  }

  /// <summary>Writes the recording into the recordings folder, the seconds sampled when the
  /// run ended early.</summary>
  /// <returns>False when an early end left under 3 seconds, so nothing is written.</returns>
  /// <exception cref="IOException">The folder or file cannot be written.</exception>
  /// <exception cref="UnauthorizedAccessException">The folder or file is not writable.</exception>
  public bool Save() {
    if (_run == null || (!Done && _run.Recording.Samples < 3))
      return false;
    _run.Recording.Save(folder, write: true, partial: !Done);
    return true;
  }

  private void Fire(float dt) {
    if (Failure != null || Done)
      return;
    Guard(() => {
      _fired++;
      _run!.Sample(_fired - 1);
      if (Done)
        Stop();
      else
        Second(_fired);
    });
  }

  private void Second(int second) {
    _queued.Enqueue((second, _run!.Due(second).GetEnumerator()));
    Pump();
    if (Failure == null)
      _run!.Before(second);
  }

  private void Pump() {
    while (Failure == null) {
      if (_acting != null) {
        try {
          if (_acting.MoveNext())
            return;
        } catch (StepFailure e) {
          Fail(e);
          return;
        }
        _acting.Dispose();
        _acting = null;
      }
      if (!TakeNext())
        return;
    }
  }

  private bool TakeNext() {
    while (_queued.Count > 0) {
      (int second, IEnumerator<SetupDuring> due) = _queued.Peek();
      if (due.MoveNext()) {
        SetupDuring action = due.Current;
        int index = 1 + scenario.Model.During.TakeWhile(a => !ReferenceEquals(a, action)).Count();
        _acting = ScenarioSteps
          .Run(_actions!, action.Steps, $"d{index}@{second}.")
          .GetEnumerator();
        return true;
      }
      _queued.Dequeue().Due.Dispose();
    }
    return false;
  }

  private void Guard(Action action) {
    try {
      action();
    } catch (Exception e)
      when (e
          is SetupFileException
            or SetupReadingException
            or InvalidOperationException
      ) {
      Fail(
        new StepFailure(
          e.Message,
          e is SetupFileException ? Suspect.Scenario : Suspect.Mod,
          e
        )
      );
    }
  }

  private void Fail(StepFailure failure) {
    Failure = failure;
    Stop();
  }
}
