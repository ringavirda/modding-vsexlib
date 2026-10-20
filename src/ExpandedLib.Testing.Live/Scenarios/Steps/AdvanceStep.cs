using System;
using System.Collections.Generic;
using System.Globalization;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary><c>advance</c>: moves the server calendar forward by <c>hours</c> of game time, one
/// <see cref="HoursPerTick"/> at a time with a runner tick between, so a block entity reads the new time
/// before the next chunk and a span of <c>n</c> hours ends in <c>n</c> ticks.</summary>
internal sealed class AdvanceStep : ScenarioStep {
  /// <summary>The most game hours the calendar moves per runner tick.</summary>
  public const double HoursPerTick = 1.0;

  public override string Kind => "advance";

  public override string What(SetupStep step) =>
    $"{Format((double)step.Body["hours"]!)} h";

  public override string? Refuse(SetupStep step) =>
    (double)step.Body["hours"]! >= 0 ? null : "hours is negative.";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    double hours = (double)step.Body["hours"]!;
    IGameCalendar calendar = ctx.World.Calendar;
    double before = calendar.TotalHours;
    double left = hours;
    while (left > 1e-9) {
      double chunk = Math.Min(HoursPerTick, left);
      calendar.Add((float)chunk);
      left -= chunk;
      yield return true;
    }
    report.Value =
      $"calendar {Format(before)} h to {Format(calendar.TotalHours)} h";
  }

  private static string Format(double hours) =>
    hours.ToString("0.00", CultureInfo.InvariantCulture);
}
