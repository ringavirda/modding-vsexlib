using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The build outline draws a missing megablock part as its whole footprint, the principal cell flagged.</summary>
public class PartProjectionTests {
  private const string Footprint =
    """[{ "x": 0, "y": 1, "z": 0 }, { "x": 1, "y": 0, "z": 0 }]""";

  private static ExBlockDef Def() =>
    ExBlockDef
      .Create("exlib", "testmega")
      .MultiblockLayout(s =>
        s.Origin(0, 0)
          .Legend('C', "exlib:testmega*")
          .Legend('#', "exlib:testpart*")
          .Layer(0, "C #")
      );

  private sealed class ColouredPart : BlockFilledMegastructure {
    public int Angle;

    public override int StructureAngle => Angle;

    public override int GetColor(ICoreClientAPI capi, BlockPos pos) => 0x336699;
  }

  private static BlockFilledMegastructure Part(int angle = 0) =>
    TestBlocks.Configure(
      new ColouredPart {
        Angle = angle,
        Attributes = new JsonObject(
          JToken.Parse($$"""{"fillerOffsets":{{Footprint}}}""")
        ),
      },
      "exlib:testpart-north",
      2
    );

  private static (TestMegablock Machine, BlockPos Part) Stand(
    BlockPos anchor,
    bool placePart,
    bool unfinished = false,
    int partAngle = 0
  ) {
    var world = new TestWorld();
    var machine = new TestMegablock();
    Block core = TestBlocks.Configure(new Block(), "exlib:testmega-n", 1);
    world.Place(anchor, core, machine);
    world.Attach(machine);
    machine.Block = core;
    BlockFilledMegastructure part = Part(partAngle);
    world.Register(part);
    BlockPos at = anchor.AddCopy(1, 0, 0);
    StructureRig rig = StructureRig.Around(world, machine, Def());
    if (placePart) {
      var entity = new TestPlainBlockEntity { Block = part };
      if (unfinished) {
        var rcc = new ExRightClickConstructable(entity);
        ReflectionHelpers.SetField(
          rcc,
          "rcc",
          new ExRightClickConstruction {
            Stages = [new ExConstructionStage(), new ExConstructionStage()],
            CurrentCompletedStage = 0,
          }
        );
        entity.Behaviors.Add(rcc);
      }
      rig.Occupy(at, part, entity);
    }
    return (machine, at);
  }

  // Fails when a missing part is outlined by its principal cell alone.
  [Fact]
  public void A_missing_part_is_outlined_as_its_whole_footprint() {
    var (machine, at) = Stand(new BlockPos(12000, 10, 0), placePart: false);

    var outline = machine.IncompleteOutline();

    Assert.Equal(
      new[] { at, at.UpCopy(), at.EastCopy() }
        .Select(p => p.ToString())
        .Order(),
      outline.Select(c => c.At.ToString()).Order()
    );
    Assert.Equal(at, Assert.Single(outline, c => c.Principal).At);
    Assert.Equal(
      ProjectionColors.Of(0x336699),
      Assert.Single(outline.Select(c => c.Color).Distinct())
    );
  }

  // Fails when an unfinished standing part is drawn red like a wrong block, or only at its principal cell.
  [Fact]
  public void An_unfinished_part_is_outlined_whole_in_the_open_colour() {
    var (machine, at) = Stand(
      new BlockPos(12200, 10, 0),
      placePart: true,
      unfinished: true
    );

    var outline = machine.IncompleteOutline();

    Assert.Equal(3, outline.Count);
    Assert.All(outline, c => Assert.Equal(ProjectionColors.Open, c.Color));
    Assert.Equal(at, Assert.Single(outline, c => c.Principal).At);
  }

  // Fails when a standing unfinished part is turned by the layout's angle instead of its own.
  [Fact]
  public void An_unfinished_part_turns_by_its_own_angle() {
    var (machine, at) = Stand(
      new BlockPos(12300, 10, 0),
      placePart: true,
      unfinished: true,
      partAngle: 90
    );

    var outline = machine.IncompleteOutline();

    Assert.Equal(
      new[] { at, at.UpCopy(), at.AddCopy(0, 0, -1) }
        .Select(p => p.ToString())
        .Order(),
      outline.Select(c => c.At.ToString()).Order()
    );
  }

  // Fails when the footprint is not turned to the layout's angle.
  [Fact]
  public void The_footprint_cells_turn_with_the_angle() {
    BlockPos at = new(12100, 10, 0);

    var cells = StructureFillers
      .PartCells(Part(), at, 90)
      .Select(c => c.At.ToString())
      .Order();

    Assert.Equal(
      new[] { at, at.UpCopy(), at.AddCopy(0, 0, -1) }
        .Select(p => p.ToString())
        .Order(),
      cells
    );
  }

  /// <summary>An unfinished part standing alone at <paramref name="at"/>, turned 90 degrees, its filler cells
  /// linked to it, with the player making the projection gesture empty-handed.</summary>
  private static (
    IWorldAccessor World,
    BlockFilledMegastructure Part,
    ExRightClickConstruction Construction,
    IPlayer Player
  ) Alone(BlockPos at) {
    var world = new TestWorld();
    BlockFilledMegastructure part = Part(90);
    world.Register(part);
    var entity = new TestPlainBlockEntity { Block = part };
    var construction = new ExRightClickConstruction {
      Stages = [new ExConstructionStage(), new ExConstructionStage()],
      CurrentCompletedStage = 0,
    };
    var rcc = new ExRightClickConstructable(entity);
    ReflectionHelpers.SetField(rcc, "rcc", construction);
    entity.Behaviors.Add(rcc);
    world.Place(at, part, entity);
    Block filler = TestBlocks.Configure(
      new BlockStructureFiller(),
      StructureFillers.FillerCode.ToString(),
      90
    );
    world.Register(filler);
    foreach (var (cell, principal) in StructureFillers.PartCells(part, at, 90))
      if (!principal)
        world.Place(
          cell,
          filler,
          new BlockEntityStructureFiller { Principal = at.Copy() }
        );

    TestPlayer player = world.Player();
    player.CtrlHeld = true;
    player.Entity.Controls.ShiftKey = true;
    // The client twin keeps no entities of its own; this view reads the server's and records the outlines.
    var client = Substitute.For<IWorldAccessor>();
    client.Side.Returns(EnumAppSide.Client);
    client.BlockAccessor.Returns(world.Accessor);
    return (client, part, construction, player.Player);
  }

  private static bool Gesture(
    IWorldAccessor world,
    BlockFilledMegastructure part,
    IPlayer player,
    BlockPos at
  ) {
    var handling = EnumHandling.PassThrough;
    bool consumed = new BlockBehaviorMultiblockStructure(
      part
    ).OnBlockInteractStart(
      world,
      player,
      new BlockSelection { Position = at },
      ref handling
    );
    return consumed && handling == EnumHandling.PreventSubsequent;
  }

  private static List<(List<BlockPos> Positions, List<int> Colors)> Drawn(
    IWorldAccessor world
  ) =>
    world
      .ReceivedCalls()
      .Where(c =>
        c.GetMethodInfo().Name == nameof(IWorldAccessor.HighlightBlocks)
      )
      .Select(c =>
        ((List<BlockPos>)c.GetArguments()[2]!, (List<int>)c.GetArguments()[3]!)
      )
      .ToList();

  [Theory]
  [InlineData(false, 13000)]
  [InlineData(true, 13200)]
  public void A_gesture_on_an_unfinished_part_with_no_anchor_outlines_its_footprint(
    bool onFiller,
    int x
  ) {
    BlockPos at = new(x, 10, 0);
    var (world, part, _, player) = Alone(at);
    BlockPos clicked = onFiller ? at.UpCopy() : at;

    bool consumed = Gesture(world, part, player, clicked);

    Assert.True(consumed);
    var (positions, colors) = Assert.Single(Drawn(world));
    var expected = StructureFillers.PartCells(part, at, 90);
    Assert.Equal(
      expected.Select(c => c.At.ToString()).Order(),
      positions.Select(p => p.ToString()).Order()
    );
    for (int i = 0; i < positions.Count; i++)
      Assert.Equal(
        positions[i].Equals(at)
          ? ProjectionColors.Marked(ProjectionColors.Open)
          : ProjectionColors.Open,
        colors[i]
      );
  }

  [Fact]
  public void The_second_gesture_clears_the_part_outline() {
    BlockPos at = new(13400, 10, 0);
    var (world, part, _, player) = Alone(at);

    Gesture(world, part, player, at);
    Gesture(world, part, player, at);
    Gesture(world, part, player, at);

    var drawn = Drawn(world);
    Assert.Equal(3, drawn.Count);
    Assert.Equal(3, drawn[0].Positions.Count);
    Assert.Empty(drawn[1].Positions);
    Assert.Equal(3, drawn[2].Positions.Count);
  }

  [Fact]
  public void A_finished_part_drops_its_outline() {
    BlockPos at = new(13500, 10, 0);
    var (world, part, construction, player) = Alone(at);
    Gesture(world, part, player, at);
    var behavior = (ExRightClickConstructable)
      world
        .BlockAccessor.GetBlockEntity(at)!
        .GetBehavior<ExRightClickConstructable>()!;
    construction.CurrentCompletedStage = 1;
    var tree = new TreeAttribute();
    construction.ToTreeAttributes(tree);

    behavior.FromTreeAttributes(tree, world);

    var drawn = Drawn(world);
    Assert.Equal(2, drawn.Count);
    Assert.Empty(drawn[1].Positions);
    Assert.Null(BlockBehaviorMultiblockStructure.ShownPart);
  }
}
