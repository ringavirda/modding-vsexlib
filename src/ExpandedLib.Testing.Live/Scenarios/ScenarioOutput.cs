using System;
using System.Globalization;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary>The fixed English lines a run logs; extools parses them (scenarios.md, Output).</summary>
internal static class ScenarioOutput {
  public const string Prefix = "[scenario]";

  public static string Run(
    int scenarios,
    int cases,
    double budgetSeconds,
    string gameVersion
  ) =>
    $"{Prefix} run: {scenarios} scenario(s), {cases} case(s), budget {Seconds(Math.Ceiling(budgetSeconds), 0)} s, game {gameVersion}";

  /// <summary>The case's plot origin, absolute.</summary>
  public static string Case(string name, BlockPos origin) =>
    $"{Prefix} case {name} plot {origin.X} {origin.Y} {origin.Z}";

  public static string StepOk(
    string label,
    string kind,
    string what,
    string? value
  ) =>
    $"{Prefix}   {label} {kind} {what} ok"
    + (value == null ? "" : $": {value}");

  public static string StepFail(
    string label,
    string kind,
    string what,
    string reason
  ) => $"{Prefix}   {label} {kind} {what} FAIL: {reason}";

  public static string Skipped(string label, string kind, string what) =>
    $"{Prefix}   {label} {kind} {what} skipped";

  public static string Detail(string line) =>
    $"{Prefix}   | {line.Replace('\r', ' ').Replace('\n', ' ')}";

  public static string Pass(string name, double seconds) =>
    $"{Prefix} pass {name} ({Seconds(seconds)} s)";

  public static string Fail(
    string name,
    string step,
    string suspect,
    double seconds
  ) =>
    $"{Prefix} fail {name} step {step} suspect {suspect} ({Seconds(seconds)} s)";

  public static string Error(string name, string reason) =>
    $"{Prefix} error {name}: {reason}";

  public static string Done(int run, int passed, int failed) =>
    $"{Prefix} done: {run} case(s) run, {passed} passed, {failed} failed.";

  /// <summary>The line as chat shows it: angle brackets escaped, since chat's markup cuts a
  /// line at one.</summary>
  public static string ForChat(string line) =>
    line.Replace("<", "&lt;").Replace(">", "&gt;");

  private static string Seconds(double value, int digits = 1) =>
    value.ToString("F" + digits, CultureInfo.InvariantCulture);
}
