using ExpandedLib.Helpers;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The elapsed-game-time cursor.</summary>
public class GameClockCursorTests {
  [Fact]
  public void FirstCallReportsNothing() {
    var cursor = new GameClockCursor();
    Assert.Equal(0d, cursor.Advance(120d));
    Assert.Equal(120d, cursor.LastHours);
  }

  [Fact]
  public void LaterCallsReportTheSpan() {
    var cursor = new GameClockCursor();
    cursor.Advance(10d);
    Assert.Equal(2.5d, cursor.Advance(12.5d));
    Assert.Equal(0.5d, cursor.Advance(13d));
  }

  [Fact]
  public void BackClockReportsNothingAndRebases() {
    var cursor = new GameClockCursor();
    cursor.Advance(10d);
    Assert.Equal(0d, cursor.Advance(4d));
    Assert.Equal(4d, cursor.LastHours);
    Assert.Equal(1d, cursor.Advance(5d));
  }

  [Fact]
  public void MissingCalendarReportsNothingAndForgets() {
    var cursor = new GameClockCursor();
    cursor.Advance(10d);
    Assert.Equal(0d, cursor.Advance(-1d));
    Assert.Equal(0d, cursor.Advance(20d));
    cursor.Advance(double.NaN);
    Assert.Equal(-1d, cursor.LastHours);
  }

  [Fact]
  public void ResetForgetsTheLastHour() {
    var cursor = new GameClockCursor();
    cursor.Advance(10d);
    cursor.Reset();
    Assert.Equal(0d, cursor.Advance(50d));
  }

  [Fact]
  public void SaveAndLoadCarryTheLastHour() {
    var cursor = new GameClockCursor();
    cursor.Advance(10d);
    var tree = new TreeAttribute();
    cursor.Save(tree, "h");

    var loaded = new GameClockCursor();
    loaded.Load(tree, "h");
    Assert.Equal(3d, loaded.Advance(13d));
  }

  [Fact]
  public void LoadFromATreeWithoutTheKeyLeavesItEmpty() {
    var cursor = new GameClockCursor();
    cursor.Advance(10d);
    cursor.Load(new TreeAttribute(), "h");
    Assert.Equal(0d, cursor.Advance(30d));
  }
}
