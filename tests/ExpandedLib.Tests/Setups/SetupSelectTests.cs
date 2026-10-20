#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The <c>select</c> step in the harness (setups.md, What the harness runs).</summary>
[Collection("Setups")]
public class SetupSelectTests
{
  private static SetupScene Build(string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [],
      null,
      _ => { }
    );

  // Fails when the step does not reach TestPlayer.Select.
  [Fact]
  public void Select_MakesTheSlotActive()
  {
    using SetupScene scene = Build("[{\"select\": {\"slot\": 4}}]");

    Assert.Equal(4, scene.Player.ActiveNumber);
  }

  // Fails when a slot past the hotbar is taken.
  [Fact]
  public void Select_PastTheHotbar_FailsTheStep()
  {
    string message = Assert
      .Throws<InvalidOperationException>(() =>
        Build("[{\"select\": {\"slot\": 10}}]")
      )
      .Message;

    Assert.Contains("steps[0]", message);
  }

  // Fails when the stand step leaves the player's feet where they were, so a select spills elsewhere.
  [Fact]
  public void Stand_ThenSelect_ActsFromTheStandCell()
  {
    using SetupScene scene = Build("[{\"stand\": {\"at\": [3, 2, 4]}}]");
    var seen = new List<(double X, double Y, double Z)>();
    scene.World.Api.Event.AfterActiveSlotChanged += (_, _) =>
      seen.Add(
        (
          scene.Player.Entity.Pos.X,
          scene.Player.Entity.Pos.Y,
          scene.Player.Entity.Pos.Z
        )
      );

    scene.Player.Select(2);

    Assert.Equal([(3.5, 2.0, 4.5)], seen);
  }

  // Fails when a stand step without a cell is accepted.
  [Fact]
  public void Stand_WithoutACell_FailsTheFile()
  {
    string message = Assert
      .Throws<InvalidOperationException>(() => Build("[{\"stand\": {}}]"))
      .Message;

    Assert.Contains("at", message);
  }
}
#endif
