#if GAME_GE_1_22
using System;
using System.IO;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

internal sealed class SetupControlBlock : Block
{
  public string? Seen;

  public override Cuboidf[] GetSelectionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    [
      new CuboidfWithId(new Cuboidf(0.25f, 0.5f, 0.25f, 0.75f, 1f, 0.5f))
      {
        Id = "handle",
      },
      new Cuboidf(0f, 0f, 0f, 1f, 0.5f, 1f),
    ];

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    Seen = blockSel.SelectionBoxId;
    return true;
  }

  public override string GetPlacedBlockInfo(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer forPlayer
  ) => $"Reads: {forPlayer.CurrentBlockSelection?.SelectionBoxId ?? "body"}";
}

/// <summary>The <c>control</c> key of the <c>interact</c> and <c>expect</c> <c>info</c> steps: the click lands on
/// the named control's box, and the info reads that box (setups.md, What the harness runs).</summary>
[Collection("Setups")]
public class SetupControlTests
{
  private static readonly string FixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupControl"
  );

  private const string Valve =
    "{\"place\": {\"id\": \"valve\", \"code\": \"setupcontrol:valve\", \"at\": [0, 0, 0]}}";

  private static SetupScene Build(string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupcontrol\"], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [FixturePath],
      null,
      w =>
      {
        SetupHoldTests.Register(w);
        w.RegisterClass("SetupControlBlock", typeof(SetupControlBlock));
      }
    );

  // Fails when the interact step drops the control key, so the click lands on the body with no id.
  [Fact]
  public void A_setup_interact_on_a_control_hands_its_id_to_the_block()
  {
    using SetupScene scene = Build(
      "["
        + Valve
        + ", {\"interact\": {\"at\": {\"of\": \"valve\"}, \"control\": \"handle\", \"returns\": true}}]"
    );

    Assert.Equal(
      "handle",
      Assert
        .IsType<SetupControlBlock>(
          scene.World.GetBlock(scene.PositionOf("valve"))
        )
        .Seen
    );
  }

  // Fails when the info step reads the body's line for a control, or the control's line for the body.
  [Fact]
  public void A_setup_expect_info_with_a_control_reads_that_controls_line()
  {
    using SetupScene scene = Build(
      "["
        + Valve
        + ", {\"expect\": {\"at\": [0, 0, 0], \"info\": \"^Reads: handle$\", \"control\": \"handle\"}}]"
    );

    Assert.NotNull(scene);
  }

  // Fails when an unknown control id is not refused with the ids of the block named.
  [Fact]
  public void An_unknown_control_fails_the_interact_step()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      Build(
        "["
          + Valve
          + ", {\"interact\": {\"at\": {\"of\": \"valve\"}, \"control\": \"nope\"}}]"
      )
    );
    Assert.Contains("has no control \"nope\"", e.Message);
  }
}
#endif
