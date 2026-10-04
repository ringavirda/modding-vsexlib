using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Scenarios;

internal enum SetupCheckKind {
  Never,
  Always,
  Steady,
  Mean,
  Intact,
}

/// <summary>A check that failed: its id and <c>says</c>, the second (-1 for a check over the
/// window as a whole) and what was read.</summary>
internal sealed record SetupCheckFailure(
  string Id,
  string Says,
  int Second,
  string Readings
) {
  public override string ToString() =>
    Second < 0
      ? $"check {Id} ({Says}) fails: {Readings}"
      : $"check {Id} ({Says}) fails at second {Second}: {Readings}";
}

/// <summary>One entry of <c>checks</c> (setups.md, Checks).</summary>
internal sealed class SetupCheck {
  private SetupCheck(string id, string says, SetupCheckKind kind) {
    Id = id;
    Says = says;
    Kind = kind;
  }

  public string Id { get; }

  public string Says { get; }

  public SetupCheckKind Kind { get; }

  /// <summary>Whether <c>never</c>, <c>always</c> and <c>intact</c> hold over the steady
  /// window only.</summary>
  public bool SteadyWindow { get; private init; }

  /// <summary>Whether the check also holds in every tick order (<c>orders: "all"</c>).</summary>
  public bool AllOrders { get; private init; } = true;

  /// <summary>The test of <c>never</c> and <c>always</c>.</summary>
  public ReadingTest? Test { get; private init; }

  /// <summary>The series a <c>steady</c> or <c>mean</c> check reads; null for <c>"*"</c>.</summary>
  public SeriesReading? Series { get; private init; }

  public float MeanEquals { get; private init; }

  public float MeanWithin { get; private init; }

  /// <summary>The run ids of <c>intact</c>.</summary>
  public IReadOnlyList<string> Runs { get; private init; } = [];

  private Reading? _intact;

  /// <summary>The readings of the world the runner samples each second for this check.</summary>
  public IReadOnlyList<SampledReading> Sampled { get; private init; } = [];

  /// <exception cref="SetupFileException">The check is malformed.</exception>
  public static SetupCheck Parse(
    JToken? token,
    SetupJson.Where where,
    SetupReferences references
  ) {
    JObject node = SetupJson.Object(token, where);
    SetupJson.RefuseUnknown(
      node,
      where,
      "id",
      "says",
      "window",
      "never",
      "always",
      "steady",
      "mean",
      "intact",
      "orders"
    );
    string id = SetupJson.String(node["id"], where.In("id"));
    if (!SetupJson.IsId(id))
      throw where.In("id").Fail($"\"{id}\" is not lower case words joined by -.");
    string says = SetupJson.String(node["says"], where.In("says"));
    string[] kinds =
    [
      .. new[] { "never", "always", "steady", "mean", "intact" }.Where(k => node[k] != null),
    ];
    if (kinds.Length != 1)
      throw where.Fail("takes one of never, always, steady, mean and intact.");
    string window = node["window"] == null
      ? "all"
      : SetupJson.String(node["window"], where.In("window"));
    if (window is not ("all" or "steady"))
      throw where.In("window").Fail($"\"{window}\" is not \"all\" or \"steady\".");
    string orders = node["orders"] == null
      ? "all"
      : SetupJson.String(node["orders"], where.In("orders"));
    if (orders is not ("all" or "drawn"))
      throw where.In("orders").Fail($"\"{orders}\" is not \"all\" or \"drawn\".");
    string kind = kinds[0];
    SetupJson.Where at = where.In(kind);
    var scope = new ReadingScope(null, InRecord: false, InCheck: true, references);
    bool steadyWindow = window == "steady";
    bool allOrders = orders == "all";
    switch (kind) {
      case "never" or "always": {
        ReadingTest test = ReadingTests.Parse(node[kind], at, scope);
        return new SetupCheck(
          id,
          says,
          kind == "never" ? SetupCheckKind.Never : SetupCheckKind.Always
        ) {
          SteadyWindow = steadyWindow,
          AllOrders = allOrders,
          Test = test,
          Sampled = [.. test.Readings.SelectMany(r => r.All()).OfType<SampledReading>()],
        };
      }
      case "steady": {
        string series = SetupJson.String(node[kind], at);
        return new SetupCheck(id, says, SetupCheckKind.Steady) {
          SteadyWindow = steadyWindow,
          AllOrders = allOrders,
          Series = series == "*" ? null : SeriesNamed(series, at, scope),
        };
      }
      case "mean": {
        JObject mean = SetupJson.Object(node[kind], at);
        SetupJson.RefuseUnknown(mean, at, "series", "equals", "within");
        return new SetupCheck(id, says, SetupCheckKind.Mean) {
          SteadyWindow = steadyWindow,
          AllOrders = allOrders,
          Series = SeriesNamed(
            SetupJson.String(mean["series"], at.In("series")),
            at.In("series"),
            scope
          ),
          MeanEquals = (float)SetupJson.Number(mean["equals"], at.In("equals")),
          MeanWithin = (float)SetupJson.Number(mean["within"], at.In("within")),
        };
      }
      default: {
        IReadOnlyList<string> runs = SetupJson.Strings(node[kind], at);
        if (runs.Count == 0)
          throw at.Fail("names no run.");
        var intact = new SampledReading(new IntactReading(runs));
        return new SetupCheck(id, says, SetupCheckKind.Intact) {
          SteadyWindow = steadyWindow,
          AllOrders = allOrders,
          Runs = runs,
          _intact = intact,
          Sampled = [intact],
        };
      }
    }
  }

  private static SeriesReading SeriesNamed(
    string series,
    SetupJson.Where where,
    ReadingScope scope
  ) =>
    (SeriesReading)
      SetupReadings.Parse(new JObject { ["series"] = series }, where, scope);

  /// <summary>Runs the check over a finished <paramref name="recording"/> whose
  /// <see cref="Sampled"/> readings were sampled every second; null when it holds.</summary>
  public SetupCheckFailure? Run(SetupRecording recording) {
    var ctx = new ReadingContext(null, recording) { Phase = ReadingPhase.Check };
    int from = SteadyWindow ? recording.SteadyFrom : 0;
    int to = SteadyWindow ? recording.SteadyTo : recording.Seconds;
    switch (Kind) {
      case SetupCheckKind.Never or SetupCheckKind.Always:
        for (int t = from; t < to; t++) {
          ctx.Second = t;
          if (Test!.Holds(ctx) == (Kind == SetupCheckKind.Never))
            return new SetupCheckFailure(Id, Says, t, Test.Describe(ctx));
        }
        return null;
      case SetupCheckKind.Intact:
        for (int t = from; t < to; t++) {
          ctx.Second = t;
          if (_intact!.Read(ctx) is false)
            return new SetupCheckFailure(Id, Says, t, $"a cell of {string.Join(", ", Runs)} is air");
        }
        return null;
      case SetupCheckKind.Steady: {
        IReadOnlyList<string> unsteady = recording.Unsteady();
        string[] failing =
        [
          .. Series == null ? unsteady : unsteady.Where(s => s == Series.Describe),
        ];
        return failing.Length == 0
          ? null
          : new SetupCheckFailure(Id, Says, -1, "unsteady " + string.Join(", ", failing));
      }
      default: {
        double mean = recording.SteadyMean(Series!.Id, Series.Name);
        return Math.Abs(mean - MeanEquals) <= MeanWithin
          ? null
          : new SetupCheckFailure(
            Id,
            Says,
            -1,
            $"{Series.Describe} steady mean = {mean.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}"
          );
      }
    }
  }

  /// <summary>Runs <paramref name="checks"/> in order; the first that fails, or null.</summary>
  public static SetupCheckFailure? First(
    IEnumerable<SetupCheck> checks,
    SetupRecording recording
  ) {
    foreach (SetupCheck check in checks)
      if (check.Run(recording) is { } failure)
        return failure;
    return null;
  }
}
