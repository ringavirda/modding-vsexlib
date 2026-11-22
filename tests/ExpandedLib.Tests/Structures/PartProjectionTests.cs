using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
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
}
