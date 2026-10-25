using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>The ids a file names outside their declarations, gathered as it parses and
/// resolved once every declaration is known.</summary>
internal sealed class SetupReferences {
  private readonly List<(string Id, SetupJson.Where Where)> _placements = [];
  private readonly List<(
    SeriesReading Reading,
    SetupJson.Where Where
  )> _series = [];

  public IReadOnlyList<(string Id, SetupJson.Where Where)> Placements =>
    _placements;

  public IReadOnlyList<(
    SeriesReading Reading,
    SetupJson.Where Where
  )> SeriesNamed => _series;

  public void Position(SetupPosition position, SetupJson.Where where) {
    if (position.Of != null)
      _placements.Add((position.Of, where));
  }

  public void Placement(string id, SetupJson.Where where) =>
    _placements.Add((id, where));

  public void Series(SeriesReading reading, SetupJson.Where where) =>
    _series.Add((reading, where));
}

/// <summary>One step of <c>steps</c>, <c>during</c>, a <c>repeat</c> or a variant: its kind,
/// the id it declares and its body as written, every key checked.</summary>
internal sealed class SetupStep(
  string kind,
  JToken body,
  string? id,
  string path,
  IReadOnlyList<SetupStep> steps,
  IReadOnlyList<ReadingTest> tests,
  IReadOnlyList<(string Code, int Count)>? draws = null,
  IReadOnlyList<(string Code, int Count)>? gives = null
) {
  public string Kind => kind;

  /// <summary>The step's object under its kind; a bare number for <c>wait</c>.</summary>
  public JToken Body => body;

  public string? Id => id;

  /// <summary>Where the step sits, such as <c>steps[2]</c>.</summary>
  public string Path => path;

  /// <summary>A <c>repeat</c>'s steps; empty for every other kind.</summary>
  public IReadOnlyList<SetupStep> Steps => steps;

  /// <summary>The conditions the step polls or expects, parsed as tests: a <c>wait</c>'s
  /// <c>until</c> and <c>hold</c>, a <c>repeat</c>'s <c>until</c>, an <c>expect</c>.</summary>
  public IReadOnlyList<ReadingTest> Tests => tests;

  /// <summary>The stacks the step's <c>draws</c> takes from the pack besides what its kind draws, in
  /// file order; empty when it has none.</summary>
  public IReadOnlyList<(string Code, int Count)> Draws => draws ?? [];

  /// <summary>The stacks the step's <c>gives</c> adds to the pack besides what its kind gives, in
  /// file order; empty when it has none.</summary>
  public IReadOnlyList<(string Code, int Count)> Gives => gives ?? [];

  /// <summary>This step and every step under it, depth first.</summary>
  public IEnumerable<SetupStep> All() =>
    new[] { this }.Concat(steps.SelectMany(s => s.All()));
}

/// <summary>Parses steps (scenarios.md, Steps, with the kinds setups.md adds).</summary>
internal static class SetupSteps {
  private static readonly Dictionary<string, string[]> Keys = new() {
    ["place"] =
    [
      "id",
      "code",
      "at",
      "by",
      "facing",
      "complete",
      "materials",
      "tree",
    ],
    ["complete"] = ["of", "to", "materials"],
    ["run"] = ["id", "code", "from", "via", "to", "family", "open"],
    ["paste"] = ["file", "at", "angle", "origin"],
    ["power"] = ["at", "face", "speed", "torque"],
    ["source"] = ["at", "face", "medium", "rate", "pressure", "temperature"],
    ["insert"] =
    [
      "at",
      "stack",
      "count",
      "temperature",
      "slot",
      "content",
      "litres",
    ],
    ["set"] = ["at", "tree", "member"],
    ["call"] = ["at", "method", "args", "returns"],
    ["interact"] =
    [
      "at",
      "with",
      "offHand",
      "keys",
      "face",
      "seconds",
      "returns",
      "control",
      "keep",
    ],
    ["wait"] = ["seconds", "until", "max", "hold"],
    ["advance"] = ["hours"],
    ["fillInventory"] = ["code"],
    ["select"] = ["slot"],
    ["stand"] = ["at"],
    ["reload"] = ["hours"],
    ["timespeed"] = ["speed", "seconds"],
    ["repeat"] = ["times", "until", "max", "steps"],
    ["break"] = ["at", "by", "with", "sneak", "take"],
    ["expect"] =
    [
      "at",
      "block",
      "member",
      "tree",
      "info",
      "control",
      "network",
      "same",
      "of",
      "footprint",
      "drops",
      "exact",
      "or",
      "equals",
      "matches",
      "min",
      "max",
      "slot",
      "code",
      "count",
      "attribute",
    ],
    ["fill"] = ["id", "code", "from", "to"],
    ["window"] = ["at", "open", "put", "take", "press", "close"],
    ["ignite"] = ["at", "seconds"],
    ["craft"] = ["at", "output", "recipe", "times", "with"],
  };

  private static readonly Dictionary<string, string[]> Required = new() {
    ["place"] = ["code", "at"],
    ["complete"] = ["of"],
    ["run"] = ["code", "from", "to"],
    ["fill"] = ["code", "from", "to"],
    ["set"] = ["at"],
    ["call"] = ["at", "method"],
    ["insert"] = ["at", "stack"],
    ["interact"] = ["at"],
    ["window"] = ["at"],
    ["ignite"] = ["at"],
    ["craft"] = ["output"],
    ["break"] = ["at"],
    ["repeat"] = ["steps"],
    ["advance"] = ["hours"],
    ["fillInventory"] = ["code"],
    ["select"] = ["slot"],
    ["stand"] = ["at"],
    ["timespeed"] = ["speed", "seconds"],
  };

  /// <summary>The kinds a step may hold: every kind but those in
  /// <paramref name="refused"/>.</summary>
  public static IReadOnlyList<SetupStep> ParseList(
    JToken? token,
    SetupJson.Where where,
    SetupReferences references,
    params string[] refused
  ) =>
    [
      .. SetupJson
        .Array(token, where)
        .Select((t, i) => Parse(t, where.At(i), references, refused)),
    ];

  /// <exception cref="SetupFileException">The step names no kind or two, a kind it may not
  /// hold, an unknown key, or lacks a key its kind requires.</exception>
  public static SetupStep Parse(
    JToken? token,
    SetupJson.Where where,
    SetupReferences references,
    params string[] refused
  ) {
    JObject node = SetupJson.Object(token, where);
    string[] kinds =
    [
      .. node.Properties().Select(p => p.Name).Where(Keys.ContainsKey),
    ];
    if (kinds.Length != 1)
      throw where.Fail(
        kinds.Length == 0 ? "names no step kind." : "names two step kinds."
      );
    string kind = kinds[0];
    SetupJson.RefuseUnknown(
      node,
      where,
      kind,
      "guide",
      "design",
      "draws",
      "gives"
    );
    IReadOnlyList<(string Code, int Count)> draws = SetupFileModel.Counts(
      node["draws"],
      where.In("draws")
    );
    IReadOnlyList<(string Code, int Count)> gives = SetupFileModel.Counts(
      node["gives"],
      where.In("gives")
    );
    if (refused.Contains(kind))
      throw where.Fail($"\"{kind}\" is not a step here.");
    var at = new SetupJson.Where(where.File, $"{where.Path} ({kind})");
    JToken body = node[kind]!;
    if (kind == "wait" && body is JValue) {
      if (SetupJson.Number(body, at) < 0)
        throw at.Fail("waits no negative time.");
      return new SetupStep(kind, body, null, where.Path, [], [], draws, gives);
    }
    JObject o = SetupJson.Object(body, at);
    SetupJson.RefuseUnknown(o, at, Keys[kind]);
    foreach (string key in Required.GetValueOrDefault(kind, []))
      if (o[key] == null)
        throw at.Fail($"takes \"{key}\".");
    string? id = null;
    if (o["id"] != null) {
      id = SetupJson.String(o["id"], at.In("id"));
      if (!SetupJson.IsId(id))
        throw at.In("id")
          .Fail($"\"{id}\" is not lower case words joined by -.");
    }
    if (kind == "craft")
      ValidateCraft(o, at);
    if (kind == "interact" && o["keep"] != null)
      SetupJson.Bool(o["keep"], at.In("keep"));
    if (kind == "break" && o["sneak"] != null)
      SetupJson.Bool(o["sneak"], at.In("sneak"));
    if (kind == "expect" && SetupSlotExpect.Is(o))
      SetupSlotExpect.Validate(o, at);
    if (kind == "set" && (o["tree"] == null) == (o["member"] == null))
      throw at.Fail("takes one of \"tree\" and \"member\".");
    if (kind == "complete")
      references.Placement(SetupJson.String(o["of"], at.In("of")), at.In("of"));
    if (kind == "expect" && o["of"] != null)
      references.Placement(SetupJson.String(o["of"], at.In("of")), at.In("of"));
    if (
      kind == "complete"
      && o["to"] is { } stage
      && stage is not JValue { Type: JTokenType.Integer }
      && !(
        stage is JValue { Type: JTokenType.String } name
        && (string?)name == "all"
      )
    )
      throw at.In("to").Fail("takes \"all\" or a whole number.");
    foreach (string key in new[] { "at", "from", "to", "same" })
      if (o[key] != null && !(kind == "complete" && key == "to"))
        references.Position(
          SetupPosition.Parse(o[key], at.In(key)),
          at.In(key)
        );
    if (o["via"] is { } via)
      foreach (
        var (v, i) in SetupJson
          .Array(via, at.In("via"))
          .Select((v, i) => (v, i))
      )
        references.Position(
          SetupPosition.Parse(v, at.In("via").At(i)),
          at.In("via").At(i)
        );
    if (kind == "run" && o["open"] is { } open)
      foreach (
        var (v, i) in SetupJson
          .Array(open, at.In("open"))
          .Select((v, i) => (v, i))
      ) {
        SetupJson.Where w = at.In("open").At(i);
        JObject face = SetupJson.Object(v, w);
        SetupJson.RefuseUnknown(face, w, "at", "face");
        references.Position(
          SetupPosition.Parse(face["at"], w.In("at")),
          w.In("at")
        );
        SetupJson.String(face["face"], w.In("face"));
      }
    var scope = new ReadingScope(
      null,
      InRecord: false,
      InCheck: false,
      references
    );
    var tests = new List<ReadingTest>();
    if (kind is "wait" or "repeat") {
      if (o["until"] != null)
        tests.AddRange(
          ReadingTests.ParseList(o["until"], at.In("until"), scope)
        );
      if (o["hold"] != null)
        tests.AddRange(ReadingTests.ParseList(o["hold"], at.In("hold"), scope));
    }
    if (
      kind == "expect"
      && o["info"] == null
      && o["same"] == null
      && o["footprint"] == null
      && o["drops"] == null
      && !SetupSlotExpect.Is(o)
    )
      tests.Add(ReadingTests.Parse(o, at, scope));
    IReadOnlyList<SetupStep> steps =
      kind == "repeat"
        ? ParseList(o["steps"], at.In("steps"), references, refused)
        : [];
    return new SetupStep(kind, o, id, where.Path, steps, tests, draws, gives);
  }

  private static void ValidateCraft(JObject o, SetupJson.Where at) {
    SetupJson.String(o["output"], at.In("output"));
    if (o["recipe"] != null)
      SetupJson.String(o["recipe"], at.In("recipe"));
    if (o["times"] != null && SetupJson.Integer(o["times"], at.In("times")) < 1)
      throw at.In("times").Fail("takes a whole number of at least 1.");
    if (o["with"] != null)
      foreach (
        JProperty pick in SetupJson
          .Object(o["with"], at.In("with"))
          .Properties()
      )
        SetupJson.String(pick.Value, at.In("with").In(pick.Name));
  }
}
