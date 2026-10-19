#if GAME_GE_1_22
using System;
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
}
#endif
