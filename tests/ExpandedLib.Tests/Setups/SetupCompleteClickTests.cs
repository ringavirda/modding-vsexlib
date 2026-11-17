#if GAME_GE_1_22
using System;
using System.IO;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Setups;

internal sealed class SetupFitFrame : BlockEntity
{
  /// <summary>Whether a click fitted the frame on the stage that completed it.</summary>
  public bool Fitted { get; set; }
}

// Fits its frame on the click that completes the construction, as the long flask fits its cope;
// with the "refuses" attribute it turns every click away before the construction sees it.
internal sealed class SetupFitBlock : Block
{
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (Attributes?["refuses"].AsBool() == true)
      return false;
    bool handled = base.OnBlockInteractStart(world, byPlayer, blockSel);
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position) is SetupFitFrame frame
      && frame.GetBehavior<ExRightClickConstructable>()?.IsComplete == true
    )
      frame.Fitted = true;
    return handled;
  }
}

/// <summary>How a <c>complete</c> pays a construction: by the player's click on the block, so what
/// the block does on a paid stage happens as it does in game (setups.md, What the harness runs).</summary>
[Collection("Setups")]
public class SetupCompleteClickTests
{
  private static SetupScene Build(string code) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupfit\"], \"steps\": ["
          + "{\"place\": {\"id\": \"frame\", \"code\": \"setupfit:"
          + code
          + "\", \"at\": [0, 0, 0]}}, {\"complete\": {\"of\": \"frame\"}}"
          + "], \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Fixtures",
          "SetupFit"
        ),
      ],
      w => w.RegisterItem("game:stick"),
      w =>
        w.RegisterClass(
            "ExRightClickConstructable",
            typeof(ExRightClickConstructable)
          )
          .RegisterClass(
            "BlockEntityInteract",
            typeof(Vintagestory.GameContent.BlockBehaviorBlockEntityInteract)
          )
          .RegisterClass("SetupFitBlock", typeof(SetupFitBlock))
          .RegisterClass("SetupFitFrame", typeof(SetupFitFrame))
    );

  // Fails when complete writes the completed stage into the construction instead of clicking the
  // block, so the frame is built but never fitted.
  [Fact]
  public void Complete_pays_the_last_stage_by_a_click_the_block_acts_on()
  {
    using SetupScene scene = Build("fitframe");

    var frame = (SetupFitFrame)
      scene.World.GetBlockEntity(scene.PositionOf("frame"))!;
    Assert.True(frame.GetBehavior<ExRightClickConstructable>()!.IsComplete);
    Assert.True(frame.Fitted);
  }

  // Fails when complete goes on after a click that built nothing, so a block that refuses the
  // click reads as built.
  [Fact]
  public void Complete_fails_when_the_click_does_not_build_its_stage()
  {
    var e = Assert.Throws<InvalidOperationException>(() =>
      Build("stuckframe").Dispose()
    );

    Assert.Equal(
      "t.json: steps[1]: stage 1 of (0, 0, 0) was not built: OnBlockInteractStart answered False with game:stick x 2 in the hotbar.",
      e.Message
    );
  }
}
#endif
