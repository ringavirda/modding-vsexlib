using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Scenarios;

/// <summary>A recording that differs from the committed file; the message names each line.</summary>
internal sealed class SetupRecordingMismatchException(string message)
  : Exception(message);

/// <summary>Decimal places a recorded number keeps, by what it measures.</summary>
internal static class SetupDigits {
  /// <summary>A volume in litres or units, or a rate of either per second.</summary>
  public const int Volume = 2;

  /// <summary>A pressure in atm.</summary>
  public const int Pressure = 3;

  /// <summary>A temperature in C.</summary>
  public const int Temperature = 1;

  /// <summary>A mechanical load, speed, power budget, or a value of any other unit.</summary>
  public const int Power = 3;

  /// <summary>The places a machine value or constant in <paramref name="unit"/> keeps.</summary>
  public static int Of(string unit) =>
    unit switch {
      "L" or "l/s" => Volume,
      "atm" => Pressure,
      "C" => Temperature,
      _ => Power,
    };

  /// <summary><paramref name="value"/> rounded to <paramref name="digits"/> places away from
  /// zero, invariant culture, negative zero written as zero.</summary>
  public static string Fixed(float value, int digits) {
    double rounded = Math.Round(
      (double)value,
      digits,
      MidpointRounding.AwayFromZero
    );
    if (rounded == 0d)
      rounded = 0d;
    return rounded.ToString("F" + digits, CultureInfo.InvariantCulture);
  }
}

/// <summary>
/// What a setup did, per second, as schema 1 (setups.md, What is recorded): each run's medium, flow,
/// pressure, temperature and volume, each machine's state and values. A series writes
/// <c>[second, value]</c> when the value rounded to <see cref="SetupDigits"/> places changes.
/// </summary>
internal sealed class SetupRecording {
  /// <summary>Largest file <see cref="Save"/> writes, in bytes.</summary>
  public const int MaxBytes = 1024 * 1024;

  private readonly string _setup;
  private readonly string _game;
  private readonly IReadOnlyList<(string Mod, string Version)> _mods;
  private readonly List<RecordedRun> _runs = [];
  private readonly List<RecordedMoltenRun> _moltenRuns = [];
  private readonly List<RecordedMachine> _machines = [];
  private int _samples;
  private bool _started;

  /// <summary>What the run did with its pack, written as the <c>pack</c> section; null for a file
  /// without a <c>pack</c>, which writes no section.</summary>
  internal SetupPackRecord? Pack { get; set; }

  /// <summary>The last <see cref="Sample"/> second plus one.</summary>
  public int Samples => _samples;

  /// <summary>Length of the recording in seconds.</summary>
  public int Seconds { get; }

  /// <summary>First second of the steady window.</summary>
  public int SteadyFrom { get; }

  /// <summary>The second after the steady window's last.</summary>
  public int SteadyTo { get; }

  /// <param name="setup">The setup's id, the file name without its extension.</param>
  /// <param name="game">The game series that ran it, such as <c>1.22</c>.</param>
  /// <param name="mods">The header's <c>mods</c> map, in order (<see cref="SetupMods.Map"/>).</param>
  /// <param name="seconds">How many seconds are sampled, at least 3.</param>
  /// <param name="steady">The steady window, <c>[from, to)</c>; the last third when null.</param>
  public SetupRecording(
    string setup,
    string game,
    IReadOnlyList<(string Mod, string Version)> mods,
    int seconds,
    (int From, int To)? steady = null
  ) {
    if (seconds < 3)
      throw new ArgumentOutOfRangeException(nameof(seconds));
    (int from, int to) = steady ?? (seconds - seconds / 3, seconds);
    if (from < 0 || to > seconds || to - from < 2)
      throw new ArgumentOutOfRangeException(nameof(steady));
    _setup = setup;
    _game = game;
    _mods = mods;
    Seconds = seconds;
    SteadyFrom = from;
    SteadyTo = to;
  }

  /// <summary>The recording a file's <c>record</c> makes, its positions resolved in
  /// <paramref name="world"/> after the build.</summary>
  public static SetupRecording ForFile(
    SetupFileModel file,
    ISetupWorld world,
    string game,
    IReadOnlyList<(string Mod, string Version)> mods
  ) {
    var recording = new SetupRecording(
      file.RecordName,
      game,
      mods,
      file.Seconds,
      file.Steady
    );
    foreach (RecordedRunSpec run in file.Record.Runs) {
      BlockPos at = run.At.Resolve(world);
      if (run.Entered == null)
        recording.Run(run.Id, at);
      else
        recording.MoltenRun(run.Id, at, run.Entered);
    }
    foreach (RecordedMachineSpec spec in file.Record.Machines) {
      RecordedMachine machine = recording
        .Machine(spec.Id, spec.Code)
        .State(spec.State);
      foreach (var (name, unit, reading) in spec.Series)
        machine.Value(name, unit, reading);
      foreach (var (name, unit, reading) in spec.Constants)
        machine.Constant(name, unit, reading);
    }
    return recording;
  }

  /// <summary>Records the pipe run owning <paramref name="pos"/> as <paramref name="id"/>. A
  /// second with no network or no state there reads as an empty medium and zeros.</summary>
  public RecordedRun Run(string id, BlockPos pos) {
    var run = new RecordedRun(id, pos);
    _runs.Add(run);
    return run;
  }

  /// <summary>Records the molten run owning <paramref name="pos"/> as <paramref name="id"/>,
  /// written after the pipe runs; its flow is the <c>rise</c> of <paramref name="entered"/>, the
  /// units that have entered the run.</summary>
  public RecordedMoltenRun MoltenRun(string id, BlockPos pos, Reading entered) {
    var run = new RecordedMoltenRun(
      id,
      pos,
      new ChangeReading(entered, rise: true)
    );
    _moltenRuns.Add(run);
    return run;
  }

  /// <summary>Records a machine as <paramref name="id"/>, placed as <paramref name="code"/>.</summary>
  public RecordedMachine Machine(string id, string code) {
    var machine = new RecordedMachine(id, code);
    _machines.Add(machine);
    return machine;
  }

  private IEnumerable<Reading> Readings =>
    _moltenRuns
      .Select(r => r.Entered)
      .Concat(_machines.SelectMany(m => m.Readings));

  /// <summary>Reads what the recording keeps from the finished build: the baselines of
  /// <c>delta</c> and <c>rise</c>, and the constants.</summary>
  /// <exception cref="SetupReadingException">A run's cell is on no network of its type.</exception>
  public void Start(ReadingContext ctx) {
    ctx.Phase = ReadingPhase.Start;
    ctx.Second = -1;
    foreach (RecordedRun run in _runs)
      run.RequireNetwork(ctx.World);
    foreach (RecordedMoltenRun run in _moltenRuns)
      run.RequireNetwork(ctx.World);
    foreach (Reading reading in Readings)
      reading.Start(ctx);
    foreach (RecordedMachine machine in _machines)
      machine.ReadConstants(ctx);
    _started = true;
  }

  /// <summary>Reads the <c>before</c> readings of second <paramref name="second"/>, before its
  /// ticks.</summary>
  public void Before(int second, ReadingContext ctx) {
    ctx.Phase = ReadingPhase.Before;
    ctx.Second = second;
    foreach (Reading reading in Readings)
      reading.Before(ctx);
  }

  /// <summary>Reads every run and machine as second <paramref name="second"/>, after its ticks.
  /// Seconds are sampled in order from 0, after <see cref="Start"/>.</summary>
  public void Sample(int second, ReadingContext ctx) {
    if (!_started)
      throw new InvalidOperationException($"Sample({second}) before Start.");
    if (second != _samples)
      throw new InvalidOperationException(
        $"Sample({second}) after {_samples} samples."
      );
    if (second >= Seconds)
      throw new InvalidOperationException(
        $"Sample({second}) past {Seconds} s."
      );
    ctx.Phase = ReadingPhase.After;
    ctx.Second = second;
    foreach (RecordedRun run in _runs)
      run.Sample(second, ctx.World);
    foreach (RecordedMoltenRun run in _moltenRuns)
      run.Sample(second, ctx);
    foreach (RecordedMachine machine in _machines)
      machine.Sample(second, ctx);
    _samples++;
  }

  /// <summary>Whether <c>{id}.{name}</c> is a recorded series.</summary>
  public bool Has(string id, string name) => Find(id, name) != null;

  /// <summary>The rounded value <c>{id}.{name}</c> held at <paramref name="second"/>: a float, or
  /// a string for a word.</summary>
  /// <exception cref="SetupReadingException">No such series, or the second is not recorded.</exception>
  public object ValueAt(string id, string name, int second) {
    Series series =
      Find(id, name)
      ?? throw new SetupReadingException($"No series {id}.{name} is recorded.");
    if (second < 0 || second >= _samples)
      throw new SetupReadingException($"{id}.{name} has no second {second}.");
    return series.ValueAt(second);
  }

  /// <summary>The time-weighted mean of <c>{id}.{name}</c> over the steady window.</summary>
  public double SteadyMean(string id, string name) {
    RequireComplete();
    return (
      Find(id, name)
      ?? throw new SetupReadingException($"No series {id}.{name} is recorded.")
    ).Mean(SteadyFrom, SteadyTo);
  }

  private Series? Find(string id, string name) =>
    AllSeries()
      .FirstOrDefault(s => s.Owner == id && s.Series.Name == name)
      .Series;

  /// <summary>The steady figure of every series, keyed <c>{id}.{series}</c>: a number is the
  /// time-weighted mean over the steady window, a word the one held longest in it (the
  /// earliest on a tie).</summary>
  public IReadOnlyDictionary<string, object> Steady() {
    RequireComplete();
    var steady = new Dictionary<string, object>();
    foreach (var (owner, series) in AllSeries())
      steady[owner + "." + series.Name] = series.Steady(SteadyFrom, SteadyTo);
    return steady;
  }

  /// <summary>The series, keyed <c>{id}.{series}</c>, that are not steady: a number whose
  /// means over the two halves of the steady window differ by more than 5% of the larger
  /// (0.05 when that is below 1), a word not the longest held in both halves.</summary>
  public IReadOnlyList<string> Unsteady() {
    RequireComplete();
    return
    [
      .. AllSeries()
        .Where(s => !s.Series.IsSteady(SteadyFrom, SteadyTo))
        .Select(s => s.Owner + "." + s.Series.Name),
    ];
  }

  /// <summary>Whether a write variable's <paramref name="value"/> (<c>1</c>, or a
  /// comma-separated list of setup ids; null when unset) names <paramref name="setup"/>.</summary>
  public static bool Writes(string? value, string setup) =>
    value == "1" || (value ?? "").Split(',').Any(n => n.Trim() == setup);

  /// <summary>
  /// Writes <c>{setup}.json</c> into <paramref name="folder"/> when it is absent or
  /// <paramref name="write"/> is set, else compares the whole text with it: an equal text
  /// deletes <c>{setup}.received.json</c>, a differing one writes it. Returns the text.
  /// </summary>
  /// <exception cref="InvalidOperationException">The recording is incomplete and not
  /// <paramref name="partial"/>, has under 3 samples when it is, or is over <see cref="MaxBytes"/>.</exception>
  /// <exception cref="SetupRecordingMismatchException">The text differs from the committed file.</exception>
  public string Save(string folder, bool write, bool partial = false) {
    string text = partial ? TextSoFar() : Text();
    if (Encoding.UTF8.GetByteCount(text) > MaxBytes)
      throw new InvalidOperationException(
        $"{_setup} recording is over {MaxBytes} bytes."
      );
    string committed = Path.Combine(folder, _setup + ".json");
    string received = Path.Combine(folder, _setup + ".received.json");
    Directory.CreateDirectory(folder);
    if (write || !File.Exists(committed)) {
      Replace(committed, text);
      File.Delete(received);
      return text;
    }
    List<string> differences = Differences(File.ReadAllText(committed), text);
    if (differences.Count == 0) {
      File.Delete(received);
      return text;
    }
    Replace(received, text);
    throw new SetupRecordingMismatchException(
      $"{_setup} recording differs from {committed}; received {received}:\n"
        + string.Join('\n', differences)
    );
  }

  /// <summary>
  /// Every line of <paramref name="recorded"/> that differs from the same line of
  /// <paramref name="committed"/>, one entry each naming the line and the text around the first
  /// differing character; empty when the two texts are equal.
  /// </summary>
  public static List<string> Differences(string committed, string recorded) {
    var differences = new List<string>();
    if (committed == recorded)
      return differences;
    string[] was = committed.Split('\n');
    string[] now = recorded.Split('\n');
    for (int i = 0; i < Math.Max(was.Length, now.Length); i++) {
      string? a = i < was.Length ? was[i] : null;
      string? b = i < now.Length ? now[i] : null;
      if (a == b)
        continue;
      int at = 0;
      while (
        a != null
        && b != null
        && at < a.Length
        && at < b.Length
        && a[at] == b[at]
      )
        at++;
      differences.Add(
        $"line {i + 1}: committed {Around(a, at)}, recorded {Around(b, at)}"
      );
    }
    return differences;
  }

  /// <summary>Up to 40 characters either side of <paramref name="at"/>, or "absent".</summary>
  private static string Around(string? line, int at) {
    if (line == null)
      return "absent";
    int from = Math.Max(0, at - 40);
    int to = Math.Min(line.Length, at + 40);
    return (from > 0 ? "..." : "")
      + line[from..to]
      + (to < line.Length ? "..." : "");
  }

  private void RequireComplete() {
    if (_samples != Seconds)
      throw new InvalidOperationException(
        $"{_setup} has {_samples} of {Seconds} samples."
      );
  }

  private IEnumerable<(string Owner, Series Series)> AllSeries() =>
    _runs
      .SelectMany(r => r.All.Select(s => (r.Id, s)))
      .Concat(_moltenRuns.SelectMany(r => r.All.Select(s => (r.Id, s))))
      .Concat(_machines.SelectMany(m => m.All.Select(s => (m.Id, s))));

  /// <summary>The recording's text, as <see cref="Save"/> writes it.</summary>
  /// <exception cref="InvalidOperationException">The recording is incomplete.</exception>
  public string Text() {
    RequireComplete();
    return Render(Seconds, SteadyFrom, SteadyTo);
  }

  /// <summary>The text of a run that ended early: the seconds sampled, with the default steady
  /// window of that length.</summary>
  /// <exception cref="InvalidOperationException">Fewer than 3 seconds were sampled.</exception>
  public string TextSoFar() {
    if (_samples < 3)
      throw new InvalidOperationException(
        $"{_setup} has {_samples} samples, under the 3 a recording needs."
      );
    return Render(_samples, _samples - _samples / 3, _samples);
  }

  private string Render(int seconds, int steadyFrom, int steadyTo) {
    var text = new StringBuilder();
    text.Append("{\"schema\":1,\"setup\":").Append(Quote(_setup));
    text.Append(",\"game\":").Append(Quote(_game));
    text.Append(",\"mods\":{")
      .Append(
        string.Join(
          ",",
          _mods.Select(m => Quote(m.Mod) + ":" + Quote(m.Version))
        )
      );
    text.Append("},\"seconds\":")
      .Append(seconds.ToString(CultureInfo.InvariantCulture));
    text.Append(",\"steady\":[")
      .Append(steadyFrom.ToString(CultureInfo.InvariantCulture))
      .Append(',')
      .Append(steadyTo.ToString(CultureInfo.InvariantCulture))
      .Append("],\n\"runs\":{");
    text.Append(
      string.Join(
        ",\n",
        _runs
          .Select(r => Entry(r.Id, r.Json(steadyFrom, steadyTo)))
          .Concat(
            _moltenRuns.Select(r => Entry(r.Id, r.Json(steadyFrom, steadyTo)))
          )
      )
    );
    text.Append("},\n\"machines\":{");
    text.Append(
      string.Join(
        ",\n",
        _machines.Select(m => Entry(m.Id, m.Json(steadyFrom, steadyTo)))
      )
    );
    text.Append('}');
    if (Pack != null)
      text.Append(",\n\"pack\":").Append(PackJson(Pack));
    text.Append("}\n");
    return text.ToString();
  }

  private static string PackJson(SetupPackRecord pack) =>
    "{\n\"start\":"
    + CountsJson(pack.Start)
    + ",\n\"end\":"
    + CountsJson(pack.End)
    + ",\n\"standing\":"
    + CountsJson(pack.Standing)
    + ",\n\"draws\":["
    + string.Join(
      ",\n",
      pack.Draws.Select(d =>
        "["
        + Quote(d.Where)
        + ","
        + Quote(d.Code)
        + ","
        + d.Count.ToString(CultureInfo.InvariantCulture)
        + "]"
      )
    )
    + "]}";

  private static string CountsJson(IReadOnlyDictionary<string, int> counts) =>
    "{"
    + string.Join(
      ",",
      counts
        .OrderBy(c => c.Key, StringComparer.Ordinal)
        .Select(c =>
          Quote(c.Key) + ":" + c.Value.ToString(CultureInfo.InvariantCulture)
        )
    )
    + "}";

  private static Dictionary<string, int> ReadCounts(JsonElement counts) =>
    counts.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());

  private static string Entry(string id, string body) => Quote(id) + ":" + body;

  internal static string Quote(string value) => JsonSerializer.Serialize(value);

  private static void Replace(string path, string text) {
    if (File.Exists(path) && File.ReadAllText(path) == text)
      return;
    string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    File.WriteAllText(temp, text);
    File.Move(temp, path, overwrite: true);
  }

  /// <summary>
  /// A committed recording's text read back into its series, so that <see cref="Text"/> writes
  /// it again: the steady figures are worked out anew from the change lists, the rest is kept.
  /// A run whose flow is in <c>u/s</c> is a molten run.
  /// </summary>
  /// <exception cref="InvalidOperationException">The text is not a schema 1 recording.</exception>
  public static SetupRecording Parse(string text) {
    using JsonDocument document = JsonDocument.Parse(text);
    JsonElement root = document.RootElement;
    if (root.GetProperty("schema").GetInt32() != 1)
      throw new InvalidOperationException("Not a schema 1 recording.");
    JsonElement steady = root.GetProperty("steady");
    int seconds = root.GetProperty("seconds").GetInt32();
    var recording = new SetupRecording(
      root.GetProperty("setup").GetString()!,
      root.GetProperty("game").GetString()!,
      [
        .. root.GetProperty("mods")
          .EnumerateObject()
          .Select(p => (p.Name, p.Value.GetString()!)),
      ],
      seconds,
      (steady[0].GetInt32(), steady[1].GetInt32())
    );
    foreach (JsonProperty run in root.GetProperty("runs").EnumerateObject()) {
      JsonElement at = run.Value.GetProperty("at");
      var pos = new BlockPos(
        at[0].GetInt32(),
        at[1].GetInt32(),
        at[2].GetInt32()
      );
      bool molten =
        run.Value.GetProperty("units").GetProperty("flow").GetString() == "u/s";
      IEnumerable<Series> all = molten
        ? recording.MoltenRun(run.Name, pos, new ValueReading(0f)).All
        : recording.Run(run.Name, pos).All;
      ReadSeries(run.Value.GetProperty("series"), all);
    }
    foreach (
      JsonProperty entry in root.GetProperty("machines").EnumerateObject()
    ) {
      RecordedMachine machine = recording.Machine(
        entry.Name,
        entry.Value.GetProperty("code").GetString()!
      );
      foreach (
        JsonProperty unit in entry.Value.GetProperty("units").EnumerateObject()
      )
        machine.Value(unit.Name, unit.Value.GetString()!, new ValueReading(0f));
      foreach (
        JsonProperty constant in entry
          .Value.GetProperty("constants")
          .EnumerateObject()
      )
        machine.Constant(
          constant.Name,
          constant.Value[1].GetString()!,
          float.Parse(
            constant.Value[0].GetRawText(),
            CultureInfo.InvariantCulture
          )
        );
      ReadSeries(entry.Value.GetProperty("series"), machine.All);
    }
    if (root.TryGetProperty("pack", out JsonElement pack))
      recording.Pack = new SetupPackRecord(
        ReadCounts(pack.GetProperty("start")),
        ReadCounts(pack.GetProperty("end")),
        ReadCounts(pack.GetProperty("standing")),
        [
          .. pack.GetProperty("draws")
            .EnumerateArray()
            .Select(d =>
              (d[0].GetString()!, d[1].GetString()!, d[2].GetInt32())
            ),
        ]
      );
    recording._samples = seconds;
    recording._started = true;
    return recording;
  }

  private static void ReadSeries(JsonElement series, IEnumerable<Series> all) {
    foreach (Series s in all)
      foreach (JsonElement change in series.GetProperty(s.Name).EnumerateArray())
        s.Add(
          change[0].GetInt32(),
          change[1].ValueKind == JsonValueKind.String
            ? change[1].GetString()!
            : change[1].GetRawText()
        );
  }

  /// <summary>One recorded quantity: its change list and how it is rounded.</summary>
  internal sealed class Series {
    private readonly List<(int Second, string Text, double Number)> _changes =
    [];
    private readonly bool _word;
    private readonly int _digits;

    public string Name { get; }

    public string? Unit { get; }

    private Series(string name, string? unit, int digits, bool word) {
      Name = name;
      Unit = unit;
      _digits = digits;
      _word = word;
    }

    public static Series Number(string name, string unit, int digits) =>
      new(name, unit, digits, word: false);

    public static Series Word(string name) => new(name, null, 0, word: true);

    public void Add(int second, float value) =>
      Add(second, SetupDigits.Fixed(value, _digits));

    public void Add(int second, string text) {
      if (_changes.Count > 0 && _changes[^1].Text == text)
        return;
      double number = _word
        ? 0d
        : double.Parse(text, CultureInfo.InvariantCulture);
      _changes.Add((second, text, number));
    }

    private (string Text, double Number) At(int second) {
      var change = _changes.Last(c => c.Second <= second);
      return (change.Text, change.Number);
    }

    /// <summary>The rounded value at <paramref name="second"/>: a float, or a string for a word.</summary>
    public object ValueAt(int second) {
      var (text, number) = At(second);
      return _word ? text : (float)number;
    }

    public double Mean(int from, int to) {
      double sum = 0d;
      for (int t = from; t < to; t++)
        sum += At(t).Number;
      return sum / (to - from);
    }

    public string Longest(int from, int to) =>
      Enumerable
        .Range(from, to - from)
        .Select(t => At(t).Text)
        .GroupBy(w => w)
        .OrderByDescending(g => g.Count())
        .ThenBy(g =>
          Enumerable.Range(from, to - from).First(t => At(t).Text == g.Key)
        )
        .First()
        .Key;

    public object Steady(int from, int to) =>
      _word
        ? Longest(from, to)
        : double.Parse(SteadyText(from, to), CultureInfo.InvariantCulture);

    private string SteadyText(int from, int to) =>
      SetupDigits.Fixed((float)Mean(from, to), _digits);

    public bool IsSteady(int from, int to) {
      int mid = from + (to - from) / 2;
      if (_word)
        return Longest(from, mid) == Longest(mid, to);
      double a = Mean(from, mid);
      double b = Mean(mid, to);
      double scale = Math.Max(Math.Abs(a), Math.Abs(b));
      double tolerance = scale < 1d ? 0.05 : 0.05 * scale;
      return Math.Abs(a - b) <= tolerance;
    }

    public string SteadyJson(int from, int to) =>
      _word ? Quote(Longest(from, to)) : SteadyText(from, to);

    public string Json() =>
      Quote(Name)
      + ":["
      + string.Join(
        ",",
        _changes.Select(c =>
          "["
          + c.Second.ToString(CultureInfo.InvariantCulture)
          + ","
          + (_word ? Quote(c.Text) : c.Text)
          + "]"
        )
      )
      + "]";
  }

  /// <summary>A pipe run read by a cell it passes through, from its network's state.</summary>
  internal sealed class RecordedRun {
    private readonly BlockPos _pos;
    private readonly Series _medium = Series.Word("medium");
    private readonly Series _flow = Series.Number(
      "flow",
      "l/s",
      SetupDigits.Volume
    );
    private readonly Series _pressure = Series.Number(
      "pressure",
      "atm",
      SetupDigits.Pressure
    );
    private readonly Series _temperature = Series.Number(
      "temperature",
      "C",
      SetupDigits.Temperature
    );
    private readonly Series _volume = Series.Number(
      "volume",
      "L",
      SetupDigits.Volume
    );

    public string Id { get; }

    public RecordedRun(string id, BlockPos pos) {
      _pos = pos.Copy();
      Id = id;
    }

    public IEnumerable<Series> All =>
      [_medium, _flow, _pressure, _temperature, _volume];

    public void RequireNetwork(ISetupWorld world) {
      if (world.NetworkAt(_pos, "pipe") == null)
        throw new SetupReadingException(
          $"Run {Id} at {_pos} is on no pipe network."
        );
    }

    public void Sample(int second, ISetupWorld world) {
      object? network = world.NetworkAt(_pos, "pipe");
      object? state =
        network == null ? null : SetupMembers.Walk(network, ["State"], null);
      _medium.Add(
        second,
        state == null ? "" : (string)Member(state, "MediumType")
      );
      _flow.Add(second, state == null ? 0f : (float)Member(state, "FlowRate"));
      _pressure.Add(
        second,
        state == null ? 0f : (float)Member(state, "Pressure")
      );
      _temperature.Add(
        second,
        state == null ? 0f : (float)Member(state, "Temperature")
      );
      _volume.Add(second, state == null ? 0f : (float)Member(state, "Volume"));
    }

    private static object Member(object state, string name) =>
      SetupValues.Normalize(SetupMembers.Walk(state, [name], null));

    public string Json(int from, int to) =>
      "{\"at\":["
      + $"{_pos.X},{_pos.Y},{_pos.Z}"
      + "],\"units\":{"
      + string.Join(
        ",",
        All.Skip(1).Select(s => Quote(s.Name) + ":" + Quote(s.Unit!))
      )
      + "},\n\"series\":{"
      + string.Join(",\n", All.Select(s => s.Json()))
      + "},\n\"steady\":{"
      + string.Join(
        ",",
        All.Select(s => Quote(s.Name) + ":" + s.SteadyJson(from, to))
      )
      + "}}";
  }

  /// <summary>
  /// A molten run read by a cell it passes through: its medium (<c>molten-</c> and the path of
  /// the metal standing in it, <c>molten-iron</c>), the units entering it per second, the
  /// hottest standing metal and the units standing in its cells.
  /// </summary>
  internal sealed class RecordedMoltenRun {
    private readonly BlockPos _pos;
    private readonly Series _medium = Series.Word("medium");
    private readonly Series _flow = Series.Number(
      "flow",
      "u/s",
      SetupDigits.Volume
    );
    private readonly Series _temperature = Series.Number(
      "temperature",
      "C",
      SetupDigits.Temperature
    );
    private readonly Series _volume = Series.Number(
      "volume",
      "u",
      SetupDigits.Volume
    );

    public string Id { get; }

    /// <summary>The run's flow: the rise of the units that have entered it.</summary>
    public Reading Entered { get; }

    public RecordedMoltenRun(string id, BlockPos pos, Reading entered) {
      _pos = pos.Copy();
      Entered = entered;
      Id = id;
    }

    public IEnumerable<Series> All => [_medium, _flow, _temperature, _volume];

    public void RequireNetwork(ISetupWorld world) {
      if (world.NetworkAt(_pos, "molten") == null)
        throw new SetupReadingException(
          $"Run {Id} at {_pos} is on no molten network."
        );
    }

    public void Sample(int second, ReadingContext ctx) {
      List<MoltenCellReading> holding =
      [
        .. Cells(ctx.World).Where(c => c.Amount > 0),
      ];
      string metal = holding.Count == 0 ? "" : holding[0].Metal;
      _medium.Add(
        second,
        metal.Length == 0
          ? ""
          : "molten-" + new AssetLocation(metal).Path.Replace("ingot-", "")
      );
      _flow.Add(second, Entered.Number(ctx));
      _temperature.Add(
        second,
        holding.Count == 0 ? 0f : holding.Max(c => c.Temperature)
      );
      _volume.Add(second, holding.Sum(c => c.Amount));
    }

    /// <summary>The run's cells, nearest the recorded cell first.</summary>
    private IEnumerable<MoltenCellReading> Cells(ISetupWorld world) =>
      world
        .NetworkCells(_pos, "molten")
        .OrderBy(p =>
          Math.Abs(p.X - _pos.X)
          + Math.Abs(p.Y - _pos.Y)
          + Math.Abs(p.Z - _pos.Z)
        )
        .ThenBy(p => (p.X, p.Y, p.Z))
        .Select(world.MoltenCellAt)
        .OfType<MoltenCellReading>();

    public string Json(int from, int to) =>
      "{\"at\":["
      + $"{_pos.X},{_pos.Y},{_pos.Z}"
      + "],\"units\":{"
      + string.Join(
        ",",
        All.Skip(1).Select(s => Quote(s.Name) + ":" + Quote(s.Unit!))
      )
      + "},\n\"series\":{"
      + string.Join(",\n", All.Select(s => s.Json()))
      + "},\n\"steady\":{"
      + string.Join(
        ",",
        All.Select(s => Quote(s.Name) + ":" + s.SteadyJson(from, to))
      )
      + "}}";
  }

  /// <summary>A machine: a state word, named values read each second, and constants.</summary>
  internal sealed class RecordedMachine {
    private readonly Series _state = Series.Word("state");
    private readonly List<(Series Series, Reading Read)> _values = [];
    private readonly List<(
      string Name,
      string Unit,
      Reading Read
    )> _constants = [];
    private readonly List<float> _constantValues = [];
    private Reading? _readState;

    public string Id { get; }

    public string Code { get; }

    public RecordedMachine(string id, string code) {
      Id = id;
      Code = code;
    }

    public IEnumerable<Series> All =>
      new[] { _state }.Concat(_values.Select(v => v.Series));

    public IEnumerable<Reading> Readings =>
      new[] { _readState }
        .OfType<Reading>()
        .Concat(_values.Select(v => v.Read))
        .Concat(_constants.Select(c => c.Read));

    /// <summary>Sets the reading whose string form is the machine's state each second.</summary>
    public RecordedMachine State(Reading read) {
      _readState = read;
      return this;
    }

    /// <summary>Records <paramref name="read"/> each second as <paramref name="name"/>, in
    /// <paramref name="unit"/>, which sets its rounding (<see cref="SetupDigits.Of"/>).</summary>
    public RecordedMachine Value(string name, string unit, Reading read) {
      _values.Add((Series.Number(name, unit, SetupDigits.Of(unit)), read));
      return this;
    }

    /// <summary>Records a figure read once before second 0, written beside the series.</summary>
    public RecordedMachine Constant(string name, string unit, Reading read) {
      _constants.Add((name, unit, read));
      return this;
    }

    public RecordedMachine Constant(string name, string unit, float value) {
      _constants.Add((name, unit, new ValueReading(value)));
      _constantValues.Add(value);
      return this;
    }

    public void ReadConstants(ReadingContext ctx) {
      _constantValues.Clear();
      foreach (var (_, _, read) in _constants)
        _constantValues.Add(read.Number(ctx));
    }

    public void Sample(int second, ReadingContext ctx) {
      _state.Add(
        second,
        SetupValues.Text(
          (
            _readState
            ?? throw new InvalidOperationException($"{Id} has no state.")
          ).Read(ctx)
        )
      );
      foreach (var (series, read) in _values)
        series.Add(second, read.Number(ctx));
    }

    public string Json(int from, int to) =>
      "{\"code\":"
      + Quote(Code)
      + ",\"units\":{"
      + string.Join(
        ",",
        _values.Select(v => Quote(v.Series.Name) + ":" + Quote(v.Series.Unit!))
      )
      + "},\n\"series\":{"
      + string.Join(",\n", All.Select(s => s.Json()))
      + "},\n\"steady\":{"
      + string.Join(
        ",",
        All.Select(s => Quote(s.Name) + ":" + s.SteadyJson(from, to))
      )
      + "},\n\"constants\":{"
      + string.Join(
        ",",
        _constants.Select(
          (c, i) =>
            Quote(c.Name)
            + ":["
            + SetupDigits.Fixed(_constantValues[i], SetupDigits.Of(c.Unit))
            + ","
            + Quote(c.Unit)
            + "]"
        )
      )
      + "}}";
  }
}
