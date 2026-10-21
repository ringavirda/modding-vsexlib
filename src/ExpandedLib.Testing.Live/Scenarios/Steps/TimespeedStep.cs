using System.Collections.Generic;
using System.Globalization;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary><c>timespeed</c>: sets the calendar's time speed modifier to <c>speed</c> for <c>seconds</c> of
/// server clock, then removes it, so a block entity ticks through the game time the faster calendar
/// covers. The modifier is removed when the case ends inside the step as well.</summary>
internal sealed class TimespeedStep : ScenarioStep {
  /// <summary>The name the step's modifier goes by on the calendar.</summary>
  public const string Modifier = "exmod-scenario";

  public override string Kind => "timespeed";

  public override string What(SetupStep step) =>
    $"x{Format((double)step.Body["speed"]!)} for {Format((double)step.Body["seconds"]!)} s";

  public override string? Refuse(SetupStep step) {
    if ((double)step.Body["speed"]! < 0)
      return "speed is negative.";
    if ((double)step.Body["seconds"]! < 0)
      return "seconds is negative.";
    return null;
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    double speed = (double)step.Body["speed"]!;
    double seconds = (double)step.Body["seconds"]!;
    IGameCalendar calendar = ctx.World.Calendar;
    double before = calendar.TotalHours;
    double end = ctx.Now + seconds;
    calendar.SetTimeSpeedModifier(Modifier, (float)speed);
    try {
      while (ctx.Now < end)
        yield return true;
    } finally {
      calendar.RemoveTimeSpeedModifier(Modifier);
    }
    report.Value =
      $"calendar {Format(before)} h to {Format(calendar.TotalHours)} h";
  }

  private static string Format(double value) =>
    value.ToString("0.00", CultureInfo.InvariantCulture);
}
