using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>A test over readings: <c>equals</c>, <c>matches</c>, <c>min</c> and <c>max</c>
/// on one reading, a reading that is itself true or false, or <c>all</c>, <c>any</c> and
/// <c>not</c> over tests.</summary>
internal abstract class ReadingTest {
  /// <summary>The readings the test reads directly, its bounds included.</summary>
  public abstract IEnumerable<Reading> Readings { get; }

  /// <exception cref="SetupReadingException">A reading fails, or a value is of the wrong
  /// kind for its test.</exception>
  public abstract bool Holds(ReadingContext ctx);

  /// <summary>What the test read at <paramref name="ctx"/>'s second, for a failure line.</summary>
  public abstract string Describe(ReadingContext ctx);
}

internal sealed class CompareTest(
  Reading subject,
  object? equals,
  Regex? matches,
  Reading? min,
  Reading? max
) : ReadingTest {
  /// <summary>How far apart two floats may be and still be equal.</summary>
  public const float Tolerance = 1e-4f;

  public override IEnumerable<Reading> Readings =>
    new[] { subject, min, max }.OfType<Reading>();

  public override bool Holds(ReadingContext ctx) {
    object value = subject.Read(ctx);
    if (equals != null && !Same(value, equals))
      return false;
    if (matches != null && !matches.IsMatch(SetupValues.Text(value)))
      return false;
    if (min != null && SetupValues.Number(value) < min.Number(ctx))
      return false;
    if (max != null && SetupValues.Number(value) > max.Number(ctx))
      return false;
    return true;
  }

  private static bool Same(object value, object expected) =>
    value is float a && expected is float b ? Math.Abs(a - b) <= Tolerance
    : value is bool p && expected is bool q ? p == q
    : SetupValues.Text(value) == SetupValues.Text(expected);

  public override string Describe(ReadingContext ctx) =>
    $"{subject.Describe} = {SetupValues.Text(subject.Read(ctx))}";
}

/// <summary>A reading that reads true or false, as <c>block</c> and a bare <c>network</c>
/// do.</summary>
internal sealed class TruthTest(Reading reading) : ReadingTest {
  public override IEnumerable<Reading> Readings => [reading];

  public override bool Holds(ReadingContext ctx) =>
    reading.Read(ctx) is bool b
      ? b
      : throw new SetupReadingException(
        $"{reading.Describe} is no test: it reads no true or false."
      );

  public override string Describe(ReadingContext ctx) =>
    $"{reading.Describe} = {SetupValues.Text(reading.Read(ctx))}";
}

internal sealed class CombinedTest(string op, IReadOnlyList<ReadingTest> tests)
  : ReadingTest {
  public override IEnumerable<Reading> Readings =>
    tests.SelectMany(t => t.Readings);

  public override bool Holds(ReadingContext ctx) =>
    op switch {
      "all" => tests.All(t => t.Holds(ctx)),
      "any" => tests.Any(t => t.Holds(ctx)),
      _ => !tests[0].Holds(ctx),
    };

  public override string Describe(ReadingContext ctx) =>
    string.Join(", ", tests.Select(t => t.Describe(ctx)));
}

/// <summary>Parses tests (setups.md, Readings): a reading's keys beside its test keys.</summary>
internal static class ReadingTests {
  private static readonly string[] TestKeys =
  [
    "equals",
    "matches",
    "min",
    "max",
  ];

  /// <exception cref="SetupFileException">The test is malformed.</exception>
  public static ReadingTest Parse(
    JToken? token,
    SetupJson.Where where,
    ReadingScope scope
  ) {
    JObject node = SetupJson.Object(token, where);
    foreach (string op in new[] { "all", "any", "not" }) {
      if (node[op] == null)
        continue;
      SetupJson.RefuseUnknown(node, where, op);
      if (op == "not")
        return new CombinedTest(op, [Parse(node[op], where.In(op), scope)]);
      return new CombinedTest(op, ParseList(node[op], where.In(op), scope));
    }
    if (!TestKeys.Any(k => node[k] != null))
      return new TruthTest(SetupReadings.Parse(node, where, scope));
    var reading = (JObject)node.DeepClone();
    foreach (string key in TestKeys)
      reading.Remove(key);
    Regex? matches = null;
    if (node["matches"] != null) {
      string pattern = SetupJson.String(node["matches"], where.In("matches"));
      try {
        matches = new Regex(pattern, RegexOptions.CultureInvariant);
      } catch (ArgumentException e) {
        throw where.In("matches").Fail(e.Message);
      }
    }
    return new CompareTest(
      SetupReadings.Parse(reading, where, scope),
      node["equals"] == null
        ? null
        : SetupJson.Literal(node["equals"], where.In("equals")),
      matches,
      Bound(node, "min", where, scope),
      Bound(node, "max", where, scope)
    );
  }

  /// <summary>One test, or an array of tests that must all hold.</summary>
  public static IReadOnlyList<ReadingTest> ParseList(
    JToken? token,
    SetupJson.Where where,
    ReadingScope scope
  ) =>
    token is JArray a
      ? [.. a.Select((t, i) => Parse(t, where.At(i), scope))]
      : [Parse(token, where, scope)];

  private static Reading? Bound(
    JObject node,
    string key,
    SetupJson.Where where,
    ReadingScope scope
  ) =>
    node[key] == null
      ? null
      : SetupReadings.Parse(node[key], where.In(key), scope);
}
