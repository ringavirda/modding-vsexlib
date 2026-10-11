using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing.Live;

/// <summary>Runs the named scenarios one case at a time on the server's main thread, each on a
/// plot of its own, and logs the <c>[scenario]</c> lines (scenarios.md, Output).</summary>
internal sealed class ScenarioRunner {
  private const int TickMilliseconds = 100;
  private const double PlotLoadSeconds = 60;

  private readonly ICoreServerAPI _api;
  private readonly bool _keep;
  private readonly IServerPlayer? _chatTo;
  private readonly Queue<string> _names;
  private readonly PlotAllocator _plots;
  private readonly ClimateHold _climate;
  private readonly List<ScenarioPlot> _held = [];
  private readonly long _tick;
  private readonly OnGetClimateDelegate _hold;
  private readonly LogEntryDelegate _feed;
  private ScenarioErrors? _errors;
  private int _run;
  private int _passed;
  private int _failed;

  private string _name = "";
  private ScenarioCase? _case;
  private ScenarioPlot? _plot;
  private double _loadStarted;
  private ScenarioContext? _ctx;
  private ScenarioCaseRun? _caseRun;
  private IEnumerator<bool>? _steps;

  /// <summary>True while a run has cases left or one running.</summary>
  public bool Active { get; private set; }

  /// <param name="names">Scenario names, <c>&lt;modid&gt;/&lt;path&gt;</c>, read from
  /// <c>&lt;dataPath&gt;/Scenarios</c>.</param>
  /// <param name="keep">Leaves every plot standing when its case ends.</param>
  /// <param name="chatTo">A joined player who also gets the case, pass, fail and done lines in
  /// chat, or null.</param>
  public ScenarioRunner(
    ICoreServerAPI api,
    IReadOnlyList<string> names,
    bool keep,
    IServerPlayer? chatTo
  ) {
    _api = api;
    _keep = keep;
    _chatTo = chatTo;
    _names = new Queue<string>(names);
    int[] spawn = api.WorldManager.DefaultSpawnPosition;
    _plots = new PlotAllocator(spawn[0], spawn[2]);
    _climate = new ClimateHold(api.WorldManager.RegionSize);
    _hold = _climate.Apply;
    _feed = (type, message, args) => _errors?.Add(type, Format(message, args));
    api.Event.OnGetClimate += _hold;
    api.Logger.EntryAdded += _feed;
    Active = true;
    _tick = api.Event.RegisterGameTickListener(Tick, TickMilliseconds);
    var cases = new List<ScenarioCase>();
    foreach (string name in names)
      if (TryLoad(name, out ScenarioCase? loaded, out _))
        cases.Add(loaded!);
    Emit(
      ScenarioOutput.Run(
        names.Count,
        names.Count,
        cases.Sum(c => c.Timeout),
        GameVersion.OverallVersion
      )
    );
    if (names.Count == 0) {
      Emit(ScenarioOutput.Error("run", "no case selected"));
      Finish();
    }
  }

  private void Tick(float dt) {
    try {
      Advance();
    } catch (Exception e) {
      Emit(ScenarioOutput.Error(_name, ScenarioSteps.Describe(e)));
      Abandon();
    }
  }

  private void Advance() {
    if (!Active)
      return;
    if (_steps != null) {
      if (_steps.MoveNext())
        return;
      EndCase();
      return;
    }
    if (_plot != null) {
      if (_plot.Loaded)
        StartCase();
      else if (Seconds() - _loadStarted > PlotLoadSeconds) {
        Emit(ScenarioOutput.Error(_name, "its plot never loaded"));
        Abandon();
      }
      return;
    }
    if (_names.Count == 0) {
      Finish();
      return;
    }
    string name = _names.Dequeue();
    if (!TryLoad(name, out ScenarioCase? scenario, out string? error)) {
      _run++;
      _failed++;
      Emit(ScenarioOutput.Error(name, error!));
      return;
    }
    _name = name;
    _case = scenario;
    PlotBox box = _plots.Next(scenario!.Plot[0], scenario.Plot[2]);
    _climate.Hold(box.CenterX, box.CenterZ);
    _plot = new ScenarioPlot(box, scenario.Plot);
    _loadStarted = Seconds();
    _plot.Load(_api);
  }

  private void StartCase() {
    ScenarioPlot plot = _plot!;
    ScenarioCase scenario = _case!;
    plot.Locate(_api.World);
    _held.Add(plot);
    _errors = new ScenarioErrors(scenario.AllowErrors);
    var world = new ScenarioWorld(
      _api.World,
      plot.Origin,
      _api.ModLoader.GetModSystem<BlockNetworkModSystem>()
    );
    _ctx = new ScenarioContext(
      _api.World,
      world,
      Seconds,
      _errors,
      Emit,
      Path.GetFileName(File(_name))
    );
    Emit(ScenarioOutput.Case(_name, plot.Origin));
    _caseRun = new ScenarioCaseRun(scenario, _ctx);
    _steps = _caseRun.Run().GetEnumerator();
  }

  private void EndCase() {
    ScenarioCaseRun run = _caseRun!;
    ScenarioContext ctx = _ctx!;
    ScenarioPlot plot = _plot!;
    bool passed = run.Passed;
    string suspect = run.Suspect;
    int failedStep = run.FailedStep;
    if (!passed)
      Save(plot, ctx);
    if (!_keep) {
      try {
        plot.Clear(_api.World);
        ctx.ThrowIfLogged();
      } catch (Exception e) when (passed) {
        passed = false;
        failedStep = _case!.Model.Steps.Count + 1;
        suspect = Suspect.Mod;
        string reason =
          e is StepFailure ? e.Message : ScenarioSteps.Describe(e);
        Emit(
          ScenarioOutput.StepFail(
            failedStep.ToString(),
            "clear",
            "plot",
            reason
          )
        );
      } catch (Exception) { }
    }
    _run++;
    if (passed) {
      _passed++;
      Emit(ScenarioOutput.Pass(_name, run.Seconds));
    } else {
      _failed++;
      Emit(
        ScenarioOutput.Fail(_name, failedStep.ToString(), suspect, run.Seconds)
      );
    }
    Reset();
  }

  private void Save(ScenarioPlot plot, ScenarioContext ctx) {
    try {
      (BlockPos min, BlockPos max) = plot.Bounds();
      var schematic = new BlockSchematic(
        (IServerWorldAccessor)_api.World,
        min,
        max,
        false
      );
      string path = Path.Combine(
        _api.GetOrCreateDataPath("WorldEdit"),
        $"scenario-{_name.Replace('/', '-')}.json"
      );
      if (schematic.Save(path) is { Length: > 0 } saveError)
        Emit(ScenarioOutput.Detail($"schematic not saved: {saveError}"));
      else
        Emit(ScenarioOutput.Detail($"schematic {path}"));
    } catch (Exception e) {
      Emit(ScenarioOutput.Detail($"schematic not saved: {e.Message}"));
    }
    BlockPos o = plot.Origin;
    Emit(ScenarioOutput.Detail($"teleport /tp ={o.X} ={o.Y + 2} ={o.Z}"));
  }

  private void Abandon() {
    _run++;
    _failed++;
    Reset();
  }

  private void Reset() {
    _steps?.Dispose();
    _steps = null;
    _caseRun = null;
    _ctx = null;
    _plot = null;
    _case = null;
    _errors = null;
  }

  private void Finish() {
    Emit(ScenarioOutput.Done(_run, _passed, _failed));
    _api.Event.UnregisterGameTickListener(_tick);
    _api.Event.OnGetClimate -= _hold;
    _api.Logger.EntryAdded -= _feed;
    Active = false;
  }

  private bool TryLoad(
    string name,
    out ScenarioCase? scenario,
    out string? error
  ) {
    scenario = null;
    error = null;
    string path = File(name);
    if (name.Contains("..") || !System.IO.File.Exists(path)) {
      error = $"no scenario file {path}.";
      return false;
    }
    try {
      scenario = ScenarioCase.Load(
        name,
        System.IO.File.ReadAllText(path),
        Path.GetFileName(path)
      );
      return true;
    } catch (SetupFileException e) {
      error = e.Message;
      return false;
    } catch (Newtonsoft.Json.JsonException e) {
      error = e.Message;
      return false;
    } catch (ArgumentException e) {
      error = e.Message;
      return false;
    }
  }

  private string File(string name) =>
    Path.Combine(_api.GetOrCreateDataPath("Scenarios"), name + ".json");

  private double Seconds() => _api.World.ElapsedMilliseconds / 1000.0;

  private void Emit(string line) {
    _api.Logger.Notification("{0}", line);
    if (
      _chatTo is { ConnectionState: EnumClientState.Playing }
      && (
        line.StartsWith($"{ScenarioOutput.Prefix} case ")
        || line.StartsWith($"{ScenarioOutput.Prefix} pass ")
        || line.StartsWith($"{ScenarioOutput.Prefix} fail ")
        || line.StartsWith($"{ScenarioOutput.Prefix} done")
      )
    )
      _chatTo.SendMessage(
        GlobalConstants.GeneralChatGroup,
        ScenarioOutput.ForChat(line),
        EnumChatType.Notification
      );
  }

  private static string Format(string message, object[] args) {
    try {
      return args is { Length: > 0 } ? string.Format(message, args) : message;
    } catch (FormatException) {
      return message;
    }
  }
}
