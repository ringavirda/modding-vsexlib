using System.Linq;
using ExpandedLib.Testing.Live;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>One case from first step to verdict: the skipped lines, the settle wait and the
/// timeout.</summary>
public class CaseRunTests {
  private const string Place =
    """{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } }""";
  private const string Holds =
    """{ "expect": { "at": { "of": "a" }, "block": "game:stone" } }""";
  private const string Fails =
    """{ "expect": { "at": { "of": "a" }, "block": "game:air" } }""";

  private static (LiveFixture Fixture, ScenarioCaseRun Run, int Ticks) Go(
    string steps,
    string extra = "{}",
    System.Action<LiveFixture>? onTick = null
  ) {
    var fixture = new LiveFixture();
    ScenarioCase scenario = LiveFixture.Case(steps, extra);
    var run = new ScenarioCaseRun(scenario, fixture.Context);
    int ticks = 0;
    foreach (bool _ in run.Run()) {
      ticks++;
      fixture.Clock += 0.5;
      onTick?.Invoke(fixture);
      if (ticks > 1000)
        break;
    }
    return (fixture, run, ticks);
  }

  [Fact]
  public void A_case_whose_steps_hold_passes_after_the_settle_wait() {
    (LiveFixture fixture, ScenarioCaseRun run, int ticks) = Go(
      $"[{Place}, {Holds}]"
    );
    Assert.True(run.Passed);
    Assert.Equal(6, ticks);
    Assert.DoesNotContain(fixture.Lines, l => l.EndsWith("skipped"));
  }

  /// <summary>Fails when the settle wait is skipped (V46).</summary>
  [Fact]
  public void The_settle_wait_runs_the_clock_for_settle_seconds() {
    (LiveFixture _, ScenarioCaseRun run, int ticks) = Go(
      $"[{Place}]",
      "{ settle: 2 }"
    );
    Assert.True(run.Passed);
    Assert.Equal(4, ticks);
    Assert.Equal(2, run.Seconds);
  }

  /// <summary>Fails when the steps after the failed one are not printed as skipped, or the ones
  /// before it are (V47).</summary>
  [Fact]
  public void A_failed_step_stops_the_case_and_the_rest_print_as_skipped() {
    (LiveFixture fixture, ScenarioCaseRun run, int _) = Go(
      $"[{Place}, {Fails}, {Holds}, {Holds}]"
    );
    Assert.False(run.Passed);
    Assert.Equal(2, run.FailedStep);
    Assert.Equal(Suspect.Mod, run.Suspect);
    string[] skipped =
    [
      .. fixture
        .Lines.Where(l => l.EndsWith("skipped"))
        .Select(l =>
          l.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)[1]
        ),
    ];
    Assert.Equal(["3", "4"], skipped);
  }

  /// <summary>Fails when an error logged during the settle wait is not caught (V48).</summary>
  [Fact]
  public void An_error_logged_while_the_case_settles_fails_it_after_its_last_step() {
    (LiveFixture fixture, ScenarioCaseRun run, int _) = Go(
      $"[{Place}]",
      "{ settle: 3 }",
      f => {
        if (f.Clock >= 1.5)
          f.Errors.Add(EnumLogType.Error, "late tick threw");
      }
    );
    Assert.False(run.Passed);
    Assert.Equal(2, run.FailedStep);
    Assert.Contains(
      fixture.Lines,
      l =>
        l.Contains(" settle ")
        && l.Contains("FAIL: server log error: late tick threw")
    );
  }

  /// <summary>Fails when a case that outlasts its timeout keeps running (V49).</summary>
  [Fact]
  public void A_case_still_running_at_its_timeout_fails() {
    (LiveFixture fixture, ScenarioCaseRun run, int _) = Go(
      $"[{Place}, {{ \"wait\": 200 }}]",
      "{ timeout: 130 }"
    );
    Assert.False(run.Passed);
    Assert.Equal(2, run.FailedStep);
    Assert.Contains(
      fixture.Lines,
      l => l.Contains("FAIL: still running after 130 s")
    );
  }
}
