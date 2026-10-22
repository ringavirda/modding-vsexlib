using System;
using System.Collections.Generic;
using System.Globalization;
using ExpandedLib.FastForwarding;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary><c>advance</c>: moves the server calendar forward by <c>hours</c> of game time, one
/// <see cref="HoursPerTick"/> at a time with a runner tick between, so a block entity reads the new time
/// before the next chunk and a span of <c>n</c> hours ends in <c>n</c> ticks. After each chunk it waits, up to
/// <see cref="MaxSettleTicks"/> ticks, for the fast-forward groups to replay the time it moved, with the replay's
/// per-call budget lifted.</summary>
internal sealed class AdvanceStep : ScenarioStep {
  /// <summary>The most game hours the calendar moves per runner tick.</summary>
  public const double HoursPerTick = 1.0;

  /// <summary>The most runner ticks one chunk waits for the groups to catch up with the calendar.</summary>
  public const int MaxSettleTicks = 2000;

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
    FastForwardDriver? driver = Networks(ctx)?.FastForward;
    int? budget = driver?.BudgetMs;
    if (driver != null)
      driver.BudgetMs = 0;
    try {
      double left = hours;
      while (left > 1e-9) {
        double chunk = Math.Min(HoursPerTick, left);
        calendar.Add((float)chunk);
        left -= chunk;
        yield return true;
        for (
          int wait = 0;
          wait < MaxSettleTicks && driver?.CaughtUp() == false;
          wait++
        )
          yield return true;
      }
    } finally {
      if (driver != null)
        driver.BudgetMs = budget;
    }
    report.Value =
      $"calendar {Format(before)} h to {Format(calendar.TotalHours)} h";
  }

  private static BlockNetworkModSystem? Networks(ScenarioContext ctx) =>
    ctx.World.Api.ModLoader.GetModSystem<BlockNetworkModSystem>();

  private static string Format(double hours) =>
    hours.ToString("0.00", CultureInfo.InvariantCulture);
}
