using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A layout cell whose block stands but whose <see cref="ExRightClickConstructable"/> has
/// stages left is reported once as unfinished, and the in-game count is the report's list.</summary>
public class UnfinishedPartTests {
  private const int FillerCells = 12;

  /// <summary>The anchor <c>C</c>, a wall part <c>#</c>, and <see cref="FillerCells"/> filler cells <c>F</c>.</summary>
  private static ExBlockDef Def() =>
    ExBlockDef
      .Create("exlib", "testmega")
      .MultiblockLayout(s =>
        s.Origin(0, 0)
          .Legend('C', "exlib:testmega*")
          .Legend('#', "exlib:testbrick*")
          .Legend('F', StructureFillers.FillerCode.ToString())
          .Layer(
            0,
            "C # " + string.Join(' ', Enumerable.Repeat('F', FillerCells))
          )
      );

  /// <summary>Gives <paramref name="owner"/> a construction of <paramref name="stages"/> stages
  /// (placing included) with <paramref name="completed"/> built.</summary>
  private static ExRightClickConstructable Construction(
    BlockEntity owner,
    int stages,
    int completed
  ) {
    var rcc = new ExRightClickConstructable(owner);
    ReflectionHelpers.SetField(
      rcc,
      "rcc",
      new ExRightClickConstruction {
        Stages =
        [
          .. Enumerable.Range(0, stages).Select(_ => new ExConstructionStage()),
        ],
        CurrentCompletedStage = completed,
      }
    );
    owner.Behaviors.Add(rcc);
    return rcc;
  }

  /// <summary>A structure at <paramref name="anchor"/> with every cell satisfied, the wall cell holding
  /// a part of <paramref name="wallStages"/> stages with <paramref name="wallCompleted"/> built, and the
  /// anchor holding one of <paramref name="anchorStages"/> stages with <paramref name="anchorCompleted"/> built.</summary>
  private static (TestWorld World, TestMegablock Machine, BlockPos Wall) Stand(
    BlockPos anchor,
    int wallStages = 1,
    int wallCompleted = 0,
    int anchorStages = 1,
    int anchorCompleted = 0
  ) {
    var world = new TestWorld();
    var machine = new TestMegablock();
    Block core = TestBlocks.Configure(new Block(), "exlib:testmega-n", 1);
    world.Place(anchor, core, machine);
    world.Attach(machine);
    machine.Block = core;
    Construction(machine, anchorStages, anchorCompleted);

    Block brick = TestBlocks.Configure(new Block(), "exlib:testbrick-n", 2);
    BlockPos wall = anchor.AddCopy(1, 0, 0);
    var wallEntity = new TestPlainBlockEntity { Block = brick };
    Construction(wallEntity, wallStages, wallCompleted);

    StructureRig
      .Around(world, machine, Def())
      .Occupy(wall, brick, wallEntity)
      .Raise();
    return (world, machine, wall);
  }

  private static List<BlockEntityMultiblockStructure.MissingCell> Missing(
    TestMegablock machine
  ) {
    var cells = new List<BlockEntityMultiblockStructure.MissingCell>();
    machine.IncompleteBlockCount(cells.Add);
    return cells;
  }

  // Fails when IncompleteBlockCount does not read the construction of a cell whose block matches.
  [Fact]
  public void UnfinishedPart_KeepsStructureIncomplete() {
    var (_, machine, wall) = Stand(
      new BlockPos(11000, 10, 0),
      wallStages: 3,
      wallCompleted: 1
    );

    Assert.Equal(1, machine.IncompleteBlockCount());
    var cell = Assert.Single(Missing(machine));
    Assert.True(cell.Unfinished);
    Assert.Equal(wall, cell.At);
    Assert.Null(cell.OutwardFace);
  }

  // Fails when a part reads as finished from CurrentCompletedStage > 0 instead of its last stage built.
  [Fact]
  public void FinishedPart_Satisfies() {
    var (_, machine, _) = Stand(
      new BlockPos(11100, 10, 0),
      wallStages: 1,
      wallCompleted: 0
    );

    Assert.Equal(0, machine.IncompleteBlockCount());
    Assert.Empty(Missing(machine));
  }

  // Fails when the cell loop skips the anchor's own offset.
  [Fact]
  public void UnfinishedAnchor_IsMissing() {
    var anchor = new BlockPos(11200, 10, 0);
    var (_, machine, _) = Stand(anchor, anchorStages: 2, anchorCompleted: 0);

    var cell = Assert.Single(Missing(machine));
    Assert.True(cell.Unfinished);
    Assert.Equal(anchor, cell.At);
  }

  // Fails when Interact passes the whole IncompleteBlockCount to the message, which also counts the cells
  // that are not player-gathered (the filler cells).
  [Fact]
  public void InteractCount_IsTheListCount() {
    var (world, machine, wall) = Stand(
      new BlockPos(11300, 10, 0),
      wallStages: 2,
      wallCompleted: 0
    );
    for (int i = 0; i < FillerCells; i++)
      world.Accessor.SetBlock(0, wall.AddCopy(1 + i, 0, 0));
    Assert.Equal(1 + FillerCells, machine.IncompleteBlockCount());
    machine.Api = world.ClientApi;
    // The client accessor answers only twins; the part's entity is the server's.
    world
      .ClientApi.World.BlockAccessor.GetBlockEntity(Arg.Any<BlockPos>())
      .Returns(ci => world.Accessor.GetBlockEntity(ci.Arg<BlockPos>()));

    machine.Interact(world.Player().Player);

    world
      .ClientApi.Received(1)
      .TriggerIngameError(machine, "incomplete", "missing 1");
  }

  // Fails when StageCount reads Stages.Length, counting the placing stage.
  [Fact]
  public void StageCount_ExcludesPlacing() {
    var rcc = Construction(
      new TestPlainBlockEntity { Block = new Block() },
      3,
      2
    );

    Assert.Equal(2, rcc.StageCount);
    Assert.True(rcc.IsComplete);
  }
}
