using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld"/>'s calendar running at the default speed a machine tool reads.</summary>
public class CalendarSpeedTests {
  // Fails when the calendar's speed fields go back to NSubstitute's 0: a held machine tool then credits no cut.
  [Fact]
  public void Calendar_runs_thirty_game_seconds_to_a_second_by_default() {
    var w = new TestWorld();

    Assert.Equal(60f, w.Calendar.SpeedOfTime);
    Assert.Equal(0.5f, w.Calendar.CalendarSpeedMul);
  }
}
