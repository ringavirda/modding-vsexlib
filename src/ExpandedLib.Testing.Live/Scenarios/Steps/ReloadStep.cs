using System.Collections.Generic;
using System.Globalization;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary><c>reload</c>: unloads the plot's chunk columns, moves the calendar forward by <c>hours</c> of
/// game time (default 0) with them away, then loads them back, so every block entity comes back as a new
/// instance that finds the calendar <c>hours</c> on.</summary>
internal sealed class ReloadStep : ScenarioStep {
  /// <summary>The server seconds the columns get to unload, and again to load back, before the step fails.</summary>
  public const double LoadSeconds = 30;

  public override string Kind => "reload";

  public override string What(SetupStep step) => $"{Format(Hours(step))} h";

  public override string? Refuse(SetupStep step) =>
    Hours(step) >= 0 ? null : "hours is negative.";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    IPlotColumns columns =
      ctx.Columns
      ?? throw new StepFailure(
        "\"reload\" needs the plot's chunk columns.",
        Suspect.Scenario
      );
    double hours = Hours(step);
    IGameCalendar calendar = ctx.World.Calendar;
    double before = calendar.TotalHours;
    columns.Unload();
    double end = ctx.Now + LoadSeconds;
    while (!columns.Unloaded) {
      if (ctx.Now >= end)
        throw new StepFailure(
          $"the plot's columns did not unload in {Format(LoadSeconds)} s."
        );
      yield return true;
    }
    if (hours > 0)
      calendar.Add((float)hours);
    columns.Load();
    end = ctx.Now + LoadSeconds;
    while (!columns.Loaded) {
      if (ctx.Now >= end)
        throw new StepFailure(
          $"the plot's columns did not load back in {Format(LoadSeconds)} s."
        );
      yield return true;
    }
    report.Value =
      $"calendar {Format(before)} h to {Format(calendar.TotalHours)} h";
  }

  private static double Hours(SetupStep step) =>
    (double?)step.Body["hours"] ?? 0;

  private static string Format(double value) =>
    value.ToString("0.00", CultureInfo.InvariantCulture);
}
