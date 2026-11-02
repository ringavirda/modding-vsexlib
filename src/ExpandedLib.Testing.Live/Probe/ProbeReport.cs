using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing.Live;

/// <summary>One tile's measurement: its columns, the seconds its generation and count took and the
/// blocks found per requested code.</summary>
internal sealed record ProbeTileResult(
  ProbeTile Tile,
  double Seconds,
  IReadOnlyDictionary<string, long> Counts
);

/// <summary>The JSON a probe run writes under <c>&lt;dataPath&gt;/probe</c>.</summary>
internal static class ProbeReport {
  /// <summary>The report: <c>seed</c>, <c>spawn</c> (x, y, z of the default spawn, null when not given),
  /// <c>columns</c>, <c>seconds</c>, <c>columnsPerSecond</c> (0 when no time passed), <c>codes</c>, the
  /// per-code <c>totals</c> and one <c>tiles</c> entry per tile with its column corners, seconds and
  /// <c>counts</c>.</summary>
  public static string ToJson(
    int seed,
    int[]? spawn,
    double seconds,
    IReadOnlyList<string> codes,
    IReadOnlyList<ProbeTileResult> tiles
  ) {
    int columns = tiles.Sum(t => t.Tile.Columns);
    var totals = new JObject();
    foreach (string code in codes)
      totals[code] = tiles.Sum(t => t.Counts.GetValueOrDefault(code));
    var report = new JObject {
      ["seed"] = seed,
      ["spawn"] = spawn == null ? null : new JArray(spawn),
      ["columns"] = columns,
      ["seconds"] = seconds,
      ["columnsPerSecond"] = seconds > 0 ? columns / seconds : 0,
      ["codes"] = new JArray(codes),
      ["totals"] = totals,
      ["tiles"] = new JArray(
        tiles.Select(t => new JObject {
          ["minColumnX"] = t.Tile.MinColumnX,
          ["minColumnZ"] = t.Tile.MinColumnZ,
          ["maxColumnX"] = t.Tile.MaxColumnX,
          ["maxColumnZ"] = t.Tile.MaxColumnZ,
          ["seconds"] = t.Seconds,
          ["counts"] = new JObject(
            codes.Select(c => new JProperty(c, t.Counts.GetValueOrDefault(c)))
          ),
        })
      ),
    };
    return report.ToString(Formatting.Indented);
  }
}
