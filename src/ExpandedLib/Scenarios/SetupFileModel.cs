using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>A <c>record.runs</c> entry: a pipe run, or a molten one when it names
/// <c>entered</c>.</summary>
internal sealed record RecordedRunSpec(string Id, SetupPosition At, Reading? Entered) {
  public IReadOnlyList<string> SeriesNames =>
    Entered == null
      ? ["medium", "flow", "pressure", "temperature", "volume"]
      : ["medium", "flow", "temperature", "volume"];
}

/// <summary>A <c>record.machines</c> entry: its code, its state, its series and constants
/// with their units, in file order.</summary>
internal sealed record RecordedMachineSpec(
  string Id,
  string Code,
  Reading State,
  IReadOnlyList<(string Name, string Unit, Reading Reading)> Series,
  IReadOnlyList<(string Name, string Unit, Reading Reading)> Constants
);

internal sealed record SetupRecordSpec(
  IReadOnlyList<RecordedRunSpec> Runs,
  IReadOnlyList<RecordedMachineSpec> Machines
);

/// <summary>One <c>during</c> action: at a second, every <c>Period</c> seconds from
/// <c>From</c>, or when its tests hold on the checked seconds.</summary>
internal sealed record SetupDuring(
  int? At,
  int Period,
  int From,
  IReadOnlyList<ReadingTest> When,
  IReadOnlyList<SetupStep> Steps
);

/// <summary>A <c>variants</c> entry, its keys checked and its ids resolved; what it does to
/// the case is the harness reader's.</summary>
internal sealed record SetupVariant(
  string Name,
  JObject Body,
  IReadOnlyList<SetupCheck>? Checks,
  string? Fails
);

/// <summary>
/// A setup file as loaded (setups.md, The file): every key checked, every id declared once and
/// every id named resolved, the readings, tests and checks parsed. The layout is kept as written
/// for the layout reader; its legend's ids and runs join the file's ids.
/// </summary>
internal sealed class SetupFileModel {
  private static readonly string[] FileKeys =
  [
    "setup",
    "description",
    "follows",
    "mods",
    "game",
    "layout",
    "steps",
    "seconds",
    "steady",
    "clock",
    "during",
    "record",
    "checks",
    "variants",
    "tickOrders",
    "figure",
    "plot",
    "climate",
    "timeout",
    "settle",
    "allowErrors",
  ];

  private static readonly string[] VariantKeys =
  [
    "drop",
    "replace",
    "after",
    "layers",
    "config",
    "seconds",
    "steady",
    "checks",
    "during",
    "fails",
    "record",
    "tickOrders",
  ];

  private static readonly Regex VariantName = new(
    "^[a-z0-9.]+(-[a-z0-9.]+)*$",
    RegexOptions.CultureInvariant
  );

  public string File { get; private init; } = "";

  public string Setup { get; private init; } = "";

  public string Description { get; private init; } = "";

  public IReadOnlyList<string> Mods { get; private init; } = [];

  /// <summary>The first game series the file runs on, <c>1.22</c> by default.</summary>
  public string Game { get; private init; } = "1.22";

  /// <summary>The <c>layout</c> object as written, or null.</summary>
  public JObject? Layout { get; private init; }

  public IReadOnlyList<SetupStep> Steps { get; private init; } = [];

  public int Seconds { get; private init; }

  /// <summary>The steady window, <c>[From, To)</c>.</summary>
  public (int From, int To) Steady { get; private init; }

  /// <summary>Whether the live clock is stepped (<c>"stepped"</c>, the default) rather than
  /// real.</summary>
  public bool SteppedClock { get; private init; } = true;

  /// <summary>The live timeout in seconds of server clock, the build and the run.</summary>
  public double Timeout { get; private init; }

  public IReadOnlyList<SetupDuring> During { get; private init; } = [];

  public SetupRecordSpec Record { get; private init; } = new([], []);

  public IReadOnlyList<SetupCheck> Checks { get; private init; } = [];

  public IReadOnlyList<SetupVariant> Variants { get; private init; } = [];

  public IReadOnlyList<string> TickOrders { get; private init; } = [];

  /// <summary>Every id a step or a legend entry places, with the code it places when it
  /// names one.</summary>
  public IReadOnlyDictionary<string, string?> Placements { get; private init; } =
    new Dictionary<string, string?>();

  /// <summary>The ids of runs: <c>run</c> steps with an id and the legend's <c>run</c>s.</summary>
  public IReadOnlySet<string> Runs { get; private init; } = new HashSet<string>();

  /// <summary>Parses a setup file's <paramref name="text"/>; <paramref name="file"/> names it in
  /// messages.</summary>
  /// <exception cref="SetupFileException">The file is malformed, names an unknown key, declares
  /// an id twice or names one nobody declares; the message names the file, where and the key.</exception>
  public static SetupFileModel Parse(string text, string file) {
    JObject root = SetupJson.ParseObject(text, file);
    var top = new SetupJson.Where(file, "");
    foreach (string refused in new[] { "cases", "matrix" })
      if (root[refused] != null)
        throw top.Fail($"\"{refused}\" is refused in a setup file; variants take its place.");
    SetupJson.RefuseUnknown(root, top, FileKeys);
    foreach (string key in new[] { "setup", "description", "mods", "steps", "seconds", "record" })
      if (root[key] == null)
        throw top.Fail($"takes \"{key}\".");

    string setup = SetupJson.String(root["setup"], top.In("setup"));
    if (!SetupJson.IsId(setup))
      throw top.In("setup").Fail($"\"{setup}\" is not lower case words joined by -.");
    string game = root["game"] == null ? "1.22" : SetupJson.String(root["game"], top.In("game"));
    if (!Regex.IsMatch(game, "^[0-9]+\\.[0-9]+$"))
      throw top.In("game").Fail($"\"{game}\" is not a game series such as 1.22.");
    int seconds = SetupJson.Integer(root["seconds"], top.In("seconds"));
    if (seconds < 3)
      throw top.In("seconds").Fail("runs at least 3 seconds.");
    var steady = Window(root["steady"], top.In("steady"), seconds);
    bool stepped = true;
    if (root["clock"] != null) {
      string clock = SetupJson.String(root["clock"], top.In("clock"));
      stepped = clock switch {
        "stepped" => true,
        "real" => false,
        _ => throw top.In("clock").Fail($"\"{clock}\" is not \"stepped\" or \"real\"."),
      };
    }
    double timeout = seconds * 1.05 + 120;
    if (root["timeout"] != null) {
      timeout = SetupJson.Number(root["timeout"], top.In("timeout"));
      double floor = seconds * 1.05 + 60;
      if (timeout < floor)
        throw top.In("timeout")
          .Fail($"{timeout.ToString(CultureInfo.InvariantCulture)} s is below seconds x 1.05 plus 60, {floor.ToString(CultureInfo.InvariantCulture)} s.");
    }
    IReadOnlyList<string> mods = SetupJson.Strings(root["mods"], top.In("mods"));
    if (root["allowErrors"] != null)
      foreach (var (pattern, i) in SetupJson.Strings(root["allowErrors"], top.In("allowErrors")).Select((p, i) => (p, i)))
        try {
          _ = new Regex(pattern);
        } catch (ArgumentException e) {
          throw top.In("allowErrors").At(i).Fail(e.Message);
        }
    JObject? layout = root["layout"] == null ? null : SetupJson.Object(root["layout"], top.In("layout"));

    var references = new SetupReferences();
    var declared = new Dictionary<string, string>();
    var placements = new Dictionary<string, string?>();
    var runs = new HashSet<string>();
    void Declare(string id, string what, SetupJson.Where where) {
      if (declared.TryGetValue(id, out string? earlier))
        throw where.Fail($"\"{id}\" is declared twice, by {earlier} and {what}.");
      declared[id] = what;
    }

    if (layout?["legend"] is JObject legend)
      foreach (JProperty entry in legend.Properties()) {
        if (entry.Value is not JObject e)
          continue;
        SetupJson.Where w = top.In("layout").In("legend").In(entry.Name);
        if (e["run"] is JValue run && run.Type == JTokenType.String) {
          string id = (string)run!;
          if (!SetupJson.IsId(id))
            throw w.In("run").Fail($"\"{id}\" is not lower case words joined by -.");
          if (runs.Add(id)) {
            Declare(id, $"legend \"{entry.Name}\"", w.In("run"));
            placements[id] = e["code"]?.Type == JTokenType.String ? (string)e["code"]! : null;
          }
        }
        if (e["id"] is JValue idValue && idValue.Type == JTokenType.String) {
          string id = (string)idValue!;
          if (!SetupJson.IsId(id))
            throw w.In("id").Fail($"\"{id}\" is not lower case words joined by -.");
          Declare(id, $"legend \"{entry.Name}\"", w.In("id"));
          placements[id] = e["code"]?.Type == JTokenType.String ? (string)e["code"]! : null;
        }
      }

    IReadOnlyList<SetupStep> steps = SetupSteps.ParseList(root["steps"], top.In("steps"), references);
    void DeclareSteps(IEnumerable<SetupStep> list) {
      foreach (SetupStep step in list.SelectMany(s => s.All()))
        if (step.Id != null) {
          Declare(step.Id, step.Path, new SetupJson.Where(file, step.Path));
          placements[step.Id] = step.Body["code"]?.Type == JTokenType.String
            ? (string)step.Body["code"]!
            : null;
          if (step.Kind == "run")
            runs.Add(step.Id);
        }
    }
    DeclareSteps(steps);

    IReadOnlyList<SetupDuring> during = root["during"] == null
      ? []
      : ParseDuring(root["during"], top.In("during"), references);
    DeclareSteps(during.SelectMany(d => d.Steps));

    SetupRecordSpec record = ParseRecord(root["record"], top.In("record"), references, placements);

    var checks = new List<SetupCheck>();
    if (root["checks"] != null)
      foreach (var (c, i) in SetupJson.Array(root["checks"], top.In("checks")).Select((c, i) => (c, i))) {
        SetupCheck check = SetupCheck.Parse(c, top.In("checks").At(i), references);
        Declare(check.Id, $"checks[{i}]", top.In("checks").At(i).In("id"));
        checks.Add(check);
      }

    var variants = new List<SetupVariant>();
    if (root["variants"] != null) {
      JObject all = SetupJson.Object(root["variants"], top.In("variants"));
      foreach (JProperty v in all.Properties()) {
        SetupJson.Where w = top.In("variants").In(v.Name);
        if (!VariantName.IsMatch(v.Name))
          throw w.Fail($"\"{v.Name}\" is not lower case words joined by -.");
        Declare(v.Name, $"variant \"{v.Name}\"", w);
        variants.Add(ParseVariant(v.Name, v.Value, w, references, Declare, DeclareSteps, checks));
      }
    }

    IReadOnlyList<string> tickOrders = [];
    if (root["tickOrders"] != null) {
      tickOrders = SetupJson.Strings(root["tickOrders"], top.In("tickOrders"));
      if (tickOrders.Count is < 2 or > 4 || tickOrders.Distinct().Count() != tickOrders.Count)
        throw top.In("tickOrders").Fail("names 2 to 4 machines, each once.");
      foreach (var (id, i) in tickOrders.Select((id, i) => (id, i)))
        references.Placement(id, top.In("tickOrders").At(i));
    }

    foreach (SetupCheck check in checks.Concat(variants.SelectMany(v => v.Checks ?? [])))
      foreach (string run in check.Runs)
        if (!runs.Contains(run))
          throw top.In("checks").Fail($"check \"{check.Id}\": \"{run}\" is no run.");
    foreach (var (id, where) in references.Placements)
      if (!placements.ContainsKey(id))
        throw where.Fail($"\"{id}\" is placed by no step or legend entry.");
    foreach (var (reading, where) in references.SeriesNamed) {
      RecordedMachineSpec? machine = record.Machines.FirstOrDefault(m => m.Id == reading.Id);
      IReadOnlyList<string>? names =
        record.Runs.FirstOrDefault(r => r.Id == reading.Id)?.SeriesNames
        ?? (machine == null ? null : ["state", .. machine.Series.Select(s => s.Name)]);
      if (names == null)
        throw where.Fail($"\"{reading.Id}\" is not recorded.");
      if (!names.Contains(reading.Name))
        throw where.Fail($"\"{reading.Id}\" records no series \"{reading.Name}\".");
    }

    return new SetupFileModel {
      File = file,
      Setup = setup,
      Description = SetupJson.String(root["description"], top.In("description")),
      Mods = mods,
      Game = game,
      Layout = layout,
      Steps = steps,
      Seconds = seconds,
      Steady = steady,
      SteppedClock = stepped,
      Timeout = timeout,
      During = during,
      Record = record,
      Checks = checks,
      Variants = variants,
      TickOrders = tickOrders,
      Placements = placements,
      Runs = runs,
    };
  }

  private static (int From, int To) Window(JToken? token, SetupJson.Where where, int seconds) {
    if (token == null)
      return (seconds - seconds / 3, seconds);
    JArray a = SetupJson.Array(token, where);
    if (a.Count != 2)
      throw where.Fail("expected [from, to].");
    int from = SetupJson.Integer(a[0], where.At(0));
    int to = SetupJson.Integer(a[1], where.At(1));
    if (from < 0 || to > seconds || to - from < 2)
      throw where.Fail($"[{from}, {to}] is not a window of at least 2 s inside the {seconds} s run.");
    return (from, to);
  }

  private static IReadOnlyList<SetupDuring> ParseDuring(
    JToken? token,
    SetupJson.Where where,
    SetupReferences references
  ) {
    var during = new List<SetupDuring>();
    foreach (var (t, i) in SetupJson.Array(token, where).Select((t, i) => (t, i))) {
      SetupJson.Where w = where.At(i);
      JObject node = SetupJson.Object(t, w);
      SetupJson.RefuseUnknown(node, w, "at", "every", "when", "from", "step", "steps");
      int forms = new[] { "at", "every", "when" }.Count(k => node[k] != null);
      if (forms != 1 && !(forms == 2 && node["when"] != null && node["every"] != null))
        throw w.Fail("takes one of \"at\", \"every\" and \"when\".");
      if ((node["step"] == null) == (node["steps"] == null))
        throw w.Fail("takes one of \"step\" and \"steps\".");
      if (node["at"] != null && node["from"] != null)
        throw w.In("from").Fail("goes with \"every\" or \"when\".");
      IReadOnlyList<SetupStep> steps = node["step"] != null
        ? [SetupSteps.Parse(node["step"], w.In("step"), references, "complete")]
        : SetupSteps.ParseList(node["steps"], w.In("steps"), references, "complete");
      int period = node["every"] == null ? 1 : SetupJson.Integer(node["every"], w.In("every"));
      if (period < 1)
        throw w.In("every").Fail("is at least 1 s.");
      var scope = new ReadingScope(null, InRecord: false, InCheck: false, references);
      during.Add(
        new SetupDuring(
          node["at"] == null ? null : SetupJson.Integer(node["at"], w.In("at")),
          period,
          node["from"] == null ? 0 : SetupJson.Integer(node["from"], w.In("from")),
          node["when"] == null ? [] : ReadingTests.ParseList(node["when"], w.In("when"), scope),
          steps
        )
      );
    }
    return during;
  }

  private static SetupRecordSpec ParseRecord(
    JToken? token,
    SetupJson.Where where,
    SetupReferences references,
    IReadOnlyDictionary<string, string?> placements
  ) {
    JObject node = SetupJson.Object(token, where);
    SetupJson.RefuseUnknown(node, where, "runs", "machines");
    var runs = new List<RecordedRunSpec>();
    var machines = new List<RecordedMachineSpec>();
    if (node["runs"] != null)
      foreach (JProperty p in SetupJson.Object(node["runs"], where.In("runs")).Properties()) {
        SetupJson.Where w = where.In("runs").In(p.Name);
        references.Placement(p.Name, w);
        JObject run = SetupJson.Object(p.Value, w);
        SetupJson.RefuseUnknown(run, w, "at", "entered");
        SetupPosition at = run["at"] == null
          ? SetupPosition.OfId(p.Name)
          : SetupPosition.Parse(run["at"], w.In("at"));
        references.Position(at, w.In("at"));
        var scope = new ReadingScope(SetupPosition.OfId(p.Name), InRecord: true, InCheck: false, references);
        Reading? entered = run["entered"] == null
          ? null
          : SetupReadings.Parse(run["entered"], w.In("entered"), scope);
        runs.Add(new RecordedRunSpec(p.Name, at, entered));
      }
    if (node["machines"] != null)
      foreach (JProperty p in SetupJson.Object(node["machines"], where.In("machines")).Properties()) {
        SetupJson.Where w = where.In("machines").In(p.Name);
        references.Placement(p.Name, w);
        JObject machine = SetupJson.Object(p.Value, w);
        SetupJson.RefuseUnknown(machine, w, "code", "series", "constants");
        var scope = new ReadingScope(SetupPosition.OfId(p.Name), InRecord: true, InCheck: false, references);
        string? code = machine["code"] == null
          ? placements.GetValueOrDefault(p.Name)
          : SetupJson.String(machine["code"], w.In("code"));
        if (code == null)
          throw w.Fail("takes a \"code\": its step places none.");
        JObject series = SetupJson.Object(machine["series"], w.In("series"));
        if (series["state"] == null)
          throw w.In("series").Fail("takes a \"state\".");
        Reading state = SetupReadings.Parse(series["state"], w.In("series").In("state"), scope);
        var values = Units(series, w.In("series"), scope, except: "state");
        var constants = machine["constants"] == null
          ? []
          : Units(SetupJson.Object(machine["constants"], w.In("constants")), w.In("constants"), scope, except: null);
        machines.Add(new RecordedMachineSpec(p.Name, code, state, values, constants));
      }
    return new SetupRecordSpec(runs, machines);
  }

  private static IReadOnlyList<(string, string, Reading)> Units(
    JObject node,
    SetupJson.Where where,
    ReadingScope scope,
    string? except
  ) {
    var list = new List<(string, string, Reading)>();
    foreach (JProperty p in node.Properties()) {
      if (p.Name == except)
        continue;
      SetupJson.Where w = where.In(p.Name);
      var reading = (JObject)SetupJson.Object(p.Value, w).DeepClone();
      string unit = SetupJson.String(reading["unit"], w.In("unit"));
      reading.Remove("unit");
      list.Add((p.Name, unit, SetupReadings.Parse(reading, w, scope)));
    }
    return list;
  }

  private static SetupVariant ParseVariant(
    string name,
    JToken token,
    SetupJson.Where where,
    SetupReferences references,
    Action<string, string, SetupJson.Where> declare,
    Action<IEnumerable<SetupStep>> declareSteps,
    IReadOnlyList<SetupCheck> mainChecks
  ) {
    JObject node = SetupJson.Object(token, where);
    SetupJson.RefuseUnknown(node, where, VariantKeys);
    if (node["drop"] != null)
      foreach (var (id, i) in SetupJson.Strings(node["drop"], where.In("drop")).Select((id, i) => (id, i)))
        references.Placement(id, where.In("drop").At(i));
    if (node["replace"] != null)
      foreach (JProperty p in SetupJson.Object(node["replace"], where.In("replace")).Properties())
        references.Placement(p.Name, where.In("replace").In(p.Name));
    if (node["after"] != null)
      foreach (JProperty p in SetupJson.Object(node["after"], where.In("after")).Properties()) {
        references.Placement(p.Name, where.In("after").In(p.Name));
        declareSteps(SetupSteps.ParseList(p.Value, where.In("after").In(p.Name), references));
      }
    if (node["config"] != null)
      foreach (JProperty p in SetupJson.Object(node["config"], where.In("config")).Properties())
        SetupJson.Object(p.Value, where.In("config").In(p.Name));
    if (node["seconds"] != null && SetupJson.Integer(node["seconds"], where.In("seconds")) < 3)
      throw where.In("seconds").Fail("runs at least 3 seconds.");
    if (node["during"] != null)
      declareSteps(ParseDuring(node["during"], where.In("during"), references).SelectMany(d => d.Steps));
    if (node["record"] != null && !SetupJson.IsId(SetupJson.String(node["record"], where.In("record"))))
      throw where.In("record").Fail("is not lower case words joined by -.");
    if (node["tickOrders"] != null && !SetupJson.Bool(node["tickOrders"], where.In("tickOrders")))
      throw where.In("tickOrders").Fail("is true or absent.");
    List<SetupCheck>? checks = null;
    if (node["checks"] != null) {
      checks = [];
      foreach (var (c, i) in SetupJson.Array(node["checks"], where.In("checks")).Select((c, i) => (c, i))) {
        SetupCheck check = SetupCheck.Parse(c, where.In("checks").At(i), references);
        declare(check.Id, $"{where.Path}.checks[{i}]", where.In("checks").At(i).In("id"));
        checks.Add(check);
      }
    }
    string? fails = null;
    if (node["fails"] != null) {
      fails = SetupJson.String(node["fails"], where.In("fails"));
      if (!(checks ?? mainChecks).Any(c => c.Id == fails))
        throw where.In("fails").Fail($"\"{fails}\" is no check of this case.");
    }
    return new SetupVariant(name, node, checks, fails);
  }
}
