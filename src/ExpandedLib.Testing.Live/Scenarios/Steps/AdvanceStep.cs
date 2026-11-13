using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ExpandedLib.FastForwarding;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>advance</c>: moves the server calendar forward by <c>hours</c> of game time, one
/// <see cref="HoursPerTick"/> at a time with a runner tick between, so a block entity reads the new time
/// before the next chunk and a span of <c>n</c> hours ends in <c>n</c> ticks. After each chunk it waits, up to
/// <see cref="MaxSettleTicks"/> ticks, for the fast-forward groups to replay the time it moved, with the replay's
/// per-call budget lifted, and then for every production machine of the plot that takes calendar time on its own
/// tick listener to tick past the new calendar time, up to <see cref="MaxMachineTicks"/> ticks. A machine
/// that does not fails the step. Block entity listeners with no stamp (a machine in a fast-forward group, which the
/// replay wait covers, and any other listener) run on the server's own ticks between chunks.</summary>
internal sealed class AdvanceStep : ScenarioStep {
  /// <summary>The most game hours the calendar moves per runner tick.</summary>
  public const double HoursPerTick = 1.0;

  /// <summary>The most runner ticks one chunk waits for the groups to catch up with the calendar.</summary>
  public const int MaxSettleTicks = 2000;

  /// <summary>The most runner ticks one chunk waits for each stamped machine to tick past the new calendar time.</summary>
  public const int MaxMachineTicks = 2000;

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
    List<BlockPos> machines = hours > 0 ? MachineCells(ctx) : [];
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
        double reached = calendar.TotalHours;
        for (int wait = 0; ; wait++) {
          List<string> late = Late(ctx, machines, reached);
          if (late.Count == 0)
            break;
          if (wait >= MaxMachineTicks)
            throw new StepFailure(
              $"{string.Join(", ", late)} did not tick to {Format(reached)} h in {MaxMachineTicks} ticks."
            );
          yield return true;
        }
      }
    } finally {
      if (driver != null)
        driver.BudgetMs = budget;
    }
    report.Value =
      $"calendar {Format(before)} h to {Format(calendar.TotalHours)} h";
  }

  // Absolute cells of the plot that hold a production machine on its own listener; none when the run has no plot.
  private static List<BlockPos> MachineCells(ScenarioContext ctx) {
    List<BlockPos> cells = [];
    if (ctx.PlotBounds is not var (min, max))
      return cells;
    IBlockAccessor blocks = ctx.World.BlockAccessor;
    for (int x = min.X; x <= max.X; x++)
      for (int z = min.Z; z <= max.Z; z++)
        for (int y = min.Y; y <= max.Y; y++) {
          var at = new BlockPos(x, y, z);
          if (Machines(blocks.GetBlockEntity(at)).Any())
            cells.Add(at);
        }
    return cells;
  }

  private static IEnumerable<BEBehaviorProductionMachine> Machines(
    BlockEntity? be
  ) => ProductionProcess.ProcessesOn(be).Where(m => m.TicksOnCalendar);

  private static List<string> Late(
    ScenarioContext ctx,
    List<BlockPos> cells,
    double reached
  ) =>
    [
      .. cells
        .Where(at =>
          Machines(ctx.World.BlockAccessor.GetBlockEntity(at))
            .Any(m => m.LastTickHours < reached - 1e-9)
        )
        .Select(at =>
        {
          BlockPos local = ctx.Setup.Local(at);
          return $"{ctx.World.BlockAccessor.GetBlockEntity(at)!.GetType().Name} at [{local.X}, {local.Y}, {local.Z}]";
        }),
    ];

  private static BlockNetworkModSystem? Networks(ScenarioContext ctx) =>
    ctx.World.Api.ModLoader.GetModSystem<BlockNetworkModSystem>();

  private static string Format(double hours) =>
    hours.ToString("0.00", CultureInfo.InvariantCulture);
}
