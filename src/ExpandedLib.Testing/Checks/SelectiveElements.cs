using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards the <c>selectiveElements</c> a definition writes against the shape it points at.
/// </summary>
/// <remarks>The game matches one path segment at a time: an exact name draws the element and drops
/// its children, so <c>Base</c> draws an empty picture wherever the block's own mesh is used; the
/// subtree form is <c>Base/*</c>.</remarks>
public static class SelectiveElements {
  /// <summary>What one run read and found.</summary>
  /// <param name="Lists">The <c>selectiveElements</c> arrays read, in <c>shape</c> and every
  /// <c>shapebytype</c> entry.</param>
  /// <param name="Paths">The paths across those arrays.</param>
  /// <param name="Findings">One line per definition and path, as
  /// <c>{golden path}: {path}: {reason}</c>.</param>
  public sealed record Result(
    int Lists,
    int Paths,
    IReadOnlyList<string> Findings
  );

  /// <summary>Runs the rule over every <c>selectiveElements</c> array in <paramref name="defs"/>,
  /// against the <c>base</c> shape of the object that carries it. A path ending in <c>/*</c> or
  /// in a prefix wildcard such as <c>Axle*</c> names the subtree; a path ending in an exact name
  /// is a finding when that element has children, and passes on a leaf.</summary>
  /// <remarks>A shape of another domain is not read. On a definition with <c>shapebytype</c>, a
  /// list beside no <c>base</c> is the one <c>ToJson</c> spreads into the entries, which are read
  /// instead.</remarks>
  /// <param name="defs">The definitions to read.</param>
  /// <param name="domain">The mod's domain; a <c>base</c> naming another domain is skipped.</param>
  /// <param name="shapesDirectory">The mod's <c>assets/{domain}/shapes</c> folder.</param>
  /// <returns>The arrays and paths read, and one finding per path that names no element, drops
  /// the children of one, or sits beside no <c>base</c> or beside a shape file that does not
  /// exist; no findings when clean.</returns>
  /// <exception cref="Newtonsoft.Json.JsonReaderException">A shape is not a JSON object.</exception>
  public static Result Check(
    IEnumerable<IExDef> defs,
    string domain,
    string shapesDirectory
  ) {
    var shapes = new Dictionary<string, JArray?>(StringComparer.Ordinal);
    int lists = 0,
      paths = 0;
    var findings = new List<string>();

    void Visit(JToken node, string golden, bool spread) {
      if (node is JObject obj) {
        string? shape = (string?)obj["base"];
        if (
          obj["selectiveElements"] is JArray selective
          && (shape != null || !spread)
        ) {
          lists++;
          foreach (string path in selective.Select(s => (string)s!)) {
            paths++;
            if (
              Verdict(shape, path, domain, shapes, shapesDirectory) is { } finding
            )
              findings.Add($"{golden}: {path}: {finding}");
          }
        }
        foreach (JProperty p in obj.Properties())
          Visit(p.Value, golden, spread);
      } else if (node is JArray array)
        foreach (JToken item in array)
          Visit(item, golden, spread);
    }

    foreach (IExDef def in defs) {
      JToken json = def.ToJson();
      Visit(
        json,
        DefinitionGoldens.RelativePath(def),
        json is JObject { } root && root["shapebytype"] is JObject
      );
    }
    return new Result(
      lists,
      paths,
      [
        .. findings
          .Distinct(StringComparer.Ordinal)
          .Order(StringComparer.Ordinal),
      ]
    );
  }

  private static string? Verdict(
    string? shape,
    string path,
    string domain,
    Dictionary<string, JArray?> cache,
    string dir
  ) {
    if (shape == null)
      return "has no shape base to read";
    string rel;
    if (!shape.Contains(':', StringComparison.Ordinal))
      rel = shape;
    else if (shape.StartsWith(domain + ":", StringComparison.Ordinal))
      rel = shape[(domain.Length + 1)..];
    else
      return null;
    return Read(rel, cache, dir) is not { } elements
      ? $"{rel}.json does not exist"
      : Judge(path, elements);
  }

  private static JArray? Read(
    string rel,
    Dictionary<string, JArray?> cache,
    string dir
  ) {
    if (cache.TryGetValue(rel, out JArray? hit))
      return hit;
    string file = Path.Combine(
      dir,
      rel.Replace('/', Path.DirectorySeparatorChar) + ".json"
    );
    return cache[rel] = File.Exists(file)
      ? JObject.Parse(File.ReadAllText(file))["elements"] as JArray ?? []
      : null;
  }

  private static string? Judge(string path, JArray roots) {
    string[] segments = path.Split('/');
    JArray level = roots;
    for (int i = 0; i < segments.Length; i++) {
      string segment = segments[i];
      bool last = i == segments.Length - 1;
      if (segment == "*")
        return last ? null : "* before the last segment";
      if (segment.EndsWith('*')) {
        string prefix = segment[..^1];
        return last
          && level.Any(e =>
            ((string?)e["name"])?.StartsWith(prefix, StringComparison.Ordinal)
            == true
          )
            ? null
          : last ? $"no element starts with {prefix}"
          : "* inside a segment before the last";
      }
      JToken? element = level.FirstOrDefault(e =>
        (string?)e["name"] == segment
      );
      if (element == null)
        return $"no element {segment}"
          + (i == 0 ? "" : $" under {segments[i - 1]}");
      if (last)
        return element["children"] is JArray { Count: > 0 }
          ? $"{segment} has children and an exact name drops them; write {path}/*"
          : null;
      level = element["children"] as JArray ?? [];
    }
    return "is empty";
  }
}
