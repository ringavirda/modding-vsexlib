#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>How a <c>complete</c> on a multiblock anchor pays the anchor's own construction and the
/// staged parts of its layout before it waits for the structure.</summary>
[Collection("Setups")]
public class SetupCompleteStagedTests
{
  private static SetupScene Build(string anchor, string part) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupstaged\"], \"steps\": ["
          + "{\"place\": {\"id\": \"anchor\", \"code\": \"setupstaged:"
          + anchor
          + "\", \"at\": [0, 0, 0]}}, "
          + "{\"place\": {\"id\": \"part\", \"code\": \"setupstaged:"
          + part
          + "\", \"at\": [1, 0, 0]}}, "
          + "{\"complete\": {\"of\": \"anchor\"}}"
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
          "SetupStaged"
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

  private static ExRightClickConstructable ConstructionAt(
    SetupScene scene,
    string id
  ) =>
    scene
      .World.GetBlockEntity(scene.PositionOf(id))!
      .GetBehavior<ExRightClickConstructable>();

  // Fails when complete raises the layout before it pays the anchor's construction, so the anchor
  // reads unfinished when the wait for the structure runs.
  [Fact]
  public void Complete_StagedAnchorFirst()
  {
    using SetupScene scene = Build("stagedanchor", "finishedpart");

    var anchor = (BlockEntityMultiblockStructure)
      scene.World.GetBlockEntity(scene.PositionOf("anchor"))!;
    Assert.True(ConstructionAt(scene, "anchor").IsComplete);
    Assert.True(anchor.StructureComplete);
  }

  // Fails when the cells the layout holds are not completed after the raise, so the staged part
  // keeps the structure incomplete.
  [Fact]
  public void Complete_PaysRaisedStagedPart()
  {
    using SetupScene scene = Build("plainanchor", "stagedpart");

    var anchor = (BlockEntityMultiblockStructure)
      scene.World.GetBlockEntity(scene.PositionOf("anchor"))!;
    Assert.True(ConstructionAt(scene, "part").IsComplete);
    Assert.True(anchor.StructureComplete);
  }

  // Fails when the missing-cell report of a staged part that is not finished leaves out its block code
  // and stage.
  [Fact]
  public void Complete_UnfinishedPartNamed()
  {
    var world = new TestWorld();
    var machine = new TestMegablock();
    Block core = TestBlocks.Configure(new Block(), "exlib:testmega-n", 1);
    var pos = new BlockPos(12000, 10, 0);
    world.Place(pos, core, machine);
    world.Attach(machine);
    machine.Block = core;
    Block brick = TestBlocks.Configure(new Block(), "exlib:testbrick-n", 2);
    var part = new TestPlainBlockEntity { Block = brick };
    var rcc = new ExRightClickConstructable(part);
    ReflectionHelpers.SetField(
      rcc,
      "rcc",
      new ExRightClickConstruction
      {
        Stages =
        [
          .. Enumerable.Range(0, 3).Select(_ => new ExConstructionStage()),
        ],
        CurrentCompletedStage = 1,
      }
    );
    part.Behaviors.Add(rcc);
    var def = ExpandedLib
      .Definitions.ExBlockDef.Create("exlib", "testmega")
      .MultiblockLayout(s =>
        s.Origin(0, 0)
          .Legend('C', "exlib:testmega*")
          .Legend('#', "exlib:testbrick*")
          .Layer(0, "C #")
      );

    StructureRig rig = StructureRig
      .Around(world, machine, def)
      .Occupy(pos.AddCopy(1, 0, 0), brick, part)
      .Raise();

    Assert.Contains("exlib:testbrick-n is at stage 1 of 2", rig.MissingReport);
  }

  // Fails when StructureRig.Complete leaves an occupied part's construction at its
  // stage, so the footprint never completes.
  [Fact]
  public void RigComplete_FinishesOccupiedStagedPart()
  {
    var world = new TestWorld();
    var machine = new TestMegablock();
    Block core = TestBlocks.Configure(new Block(), "exlib:testmega-n", 1);
    var pos = new BlockPos(12100, 10, 0);
    world.Place(pos, core, machine);
    world.Attach(machine);
    machine.Block = core;
    Block brick = TestBlocks.Configure(new Block(), "exlib:testbrick-n", 2);
    var part = new TestPlainBlockEntity { Block = brick };
    ExRightClickConstructable brickPart = Stage(part, 3);
    var def = ExpandedLib
      .Definitions.ExBlockDef.Create("exlib", "testmega")
      .MultiblockLayout(s =>
        s.Origin(0, 0)
          .Legend('C', "exlib:testmega*")
          .Legend('#', "exlib:testbrick*")
          .Layer(0, "C #")
      );

    StructureRig
      .Around(world, machine, def)
      .Occupy(pos.AddCopy(1, 0, 0), brick, part)
      .Complete();

    Assert.True(brickPart.IsComplete);
    Assert.True(machine.StructureComplete);
  }

  private static ExRightClickConstructable Stage(BlockEntity owner, int stages)
  {
    var rcc = new ExRightClickConstructable(owner);
    ReflectionHelpers.SetField(
      rcc,
      "rcc",
      new ExRightClickConstruction
      {
        Stages =
        [
          .. Enumerable.Range(0, stages).Select(_ => new ExConstructionStage()),
        ],
      }
    );
    owner.Behaviors.Add(rcc);
    return rcc;
  }
}
#endif
