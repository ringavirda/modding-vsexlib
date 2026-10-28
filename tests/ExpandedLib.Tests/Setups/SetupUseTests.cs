#if GAME_GE_1_22
using System;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The <c>use</c> step: the held item's own start, steps and stop (setups.md, What the harness
/// runs).</summary>
[Collection("Setups")]
public class SetupUseTests
{
  private const string Dial =
    "{\"place\": {\"id\": \"dial\", \"code\": \"setuphold:dial\", \"at\": [0, 0, 0]}}";

  private static string Use(string body) =>
    "[" + Dial + ", {\"use\": {\"at\": {\"of\": \"dial\"}, " + body + "}}]";

  private static SetupHoldWand Wand(SetupScene scene) =>
    Assert.IsType<SetupHoldWand>(
      scene.World.World.GetItem(new AssetLocation("setuphold:wand"))
    );

  // Fails when the step loop is skipped and the stop follows the start.
  [Fact]
  public void Use_runs_the_held_items_start_steps_and_stop()
  {
    using SetupScene scene = SetupHoldTests.Build(
      Use("\"with\": {\"stack\": \"setuphold:wand\"}, \"seconds\": 1")
    );

    SetupHoldWand wand = Wand(scene);
    Assert.Equal(
      ["start", .. Enumerable.Repeat("step", 10), "stop"],
      wand.Calls
    );
    Assert.Equal(
      Enumerable.Range(1, 10).Select(i => i * 0.1f).Append(1f),
      wand.Seconds
    );
    Assert.Equal([EnumHandInteract.HeldItemInteract], wand.HandUses);
    Assert.Equal(EnumHandInteract.None, scene.Player.Entity.Controls.HandUse);
  }

  // Fails when the hit is handed to the item undivided.
  [Fact]
  public void Use_hit_is_sixteenths_of_the_cell()
  {
    using SetupScene scene = SetupHoldTests.Build(
      Use(
        "\"with\": {\"stack\": \"setuphold:wand\"}, \"hit\": [8, 16, 4], \"seconds\": 0"
      )
    );

    Vintagestory.API.MathTools.Vec3d hit = Assert.Single(Wand(scene).Hits);
    Assert.Equal((0.5, 1.0, 0.25), (hit.X, hit.Y, hit.Z));
  }

  // Fails when the Required row is dropped and a use with no cell reaches the scene.
  [Fact]
  public void A_use_without_at_is_refused()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      SetupHoldTests.Build("[{\"use\": {\"seconds\": 1}}]")
    );

    Assert.Contains("takes \"at\"", e.Message);
  }
}
#endif
