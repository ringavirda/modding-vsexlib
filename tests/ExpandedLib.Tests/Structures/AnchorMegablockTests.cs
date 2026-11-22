using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using static ExpandedLib.Tests.MultiblockCellRolesFixtures;

namespace ExpandedLib.Tests;

/// <summary>A <see cref="BlockFilledMegastructure"/> that is also a layout's anchor: its reserved
/// footprint is the layout's filler glyph cells and a role drawn on a filler glyph resolves to its world cell, at
/// every facing.</summary>
public class AnchorMegablockTests {
  private static readonly BlockPos At = new(13000, 10, 0);

  private static readonly int[] Angles = [0, 90, 180, 270];

  /// <summary>The filler glyph cells of <see cref="Def"/> in structure-local offsets; <see cref="TapCell"/> is the one
  /// carrying a role.</summary>
  private static readonly FillerCellSpec[] FillerCells =
  [
    new(1, 0, 0),
    new(2, 0, 0),
    new(0, 0, 1),
    new(2, 0, 1),
    new(2, 1, 0),
  ];

  private static readonly Vec3i TapCell = new(2, 0, 1);

  private sealed class AnchorBlock : BlockFilledMegastructure {
    public int Angle;

    public override int StructureAngle => Angle;

    public List<FillerCell> Reserved(IWorldAccessor world, BlockPos pos) =>
      ReservedCellsAt(world, pos);
  }

  /// <summary>The anchor <c>C</c>, plain filler cells <c>F</c>, and the filler cell <c>T</c> that is the
  /// metal tap.</summary>
  private static ExBlockDef Def() =>
    ExBlockDef
      .Create("exlib", "testmega")
      .FillerOffsets(FillerCells)
      .MultiblockLayout(s =>
        s.Origin(0, 0)
          .Legend('C', "exlib:testmega*")
          .Legend('F', StructureFillers.FillerCode.ToString())
          .Legend('T', StructureFillers.FillerCode.ToString())
          .Role('T', MetalTap)
          .Layer(
            0,
            """
            C F F
            F . T
            """
          )
          .Layer(
            1,
            """
            . . F
            . . .
            """
          )
      );

  /// <summary>The anchor placed at <see cref="At"/> facing <paramref name="angle"/>, holding a construction of its
  /// placing stage and one more.</summary>
  private static (
    TestWorld World,
    TestMegablock Machine,
    AnchorBlock Block,
    StructureRig Rig
  ) Stand(int angle) {
    var world = new TestWorld();
    var machine = new TestMegablock { Angle = angle };
    var block = TestBlocks.Configure(
      new AnchorBlock { Angle = angle },
      "exlib:testmega-n",
      1
    );
    world.Place(At, block, machine);
    world.Attach(machine);
    machine.Block = block;
    var rcc = new ExRightClickConstructable(machine);
    ReflectionHelpers.SetField(
      rcc,
      "rcc",
      new ExRightClickConstruction {
        Stages = [new ExConstructionStage(), new ExConstructionStage()],
        CurrentCompletedStage = 0,
      }
    );
    machine.Behaviors.Add(rcc);
    StructureRig rig = StructureRig.Around(world, machine, Def(), angle);
    return (world, machine, block, rig);
  }

  // Fails when ExOrientation.RotateOffset turns a quarter the wrong way: the reserved cells leave the
  // cells vanilla's transform gives the layout's filler glyphs.
  [Fact]
  public void AnchorMegablock_FootprintEqualsLayout() {
    foreach (int angle in Angles) {
      var (world, machine, block, rig) = Stand(angle);

      List<BlockPos> footprint = block
        .Reserved(world.World, At)
        .ConvertAll(c => c.Pos);

      Assert.Equal(FillerCells.Length, footprint.Count);
      Assert.Equal(
        Render(machine.CellsAccepting(StructureFillers.FillerCode)),
        Render(footprint)
      );
      Assert.All(
        footprint,
        cell => Assert.True(machine.OwnsCell(cell), $"{cell} at {angle} deg")
      );
      rig.Raise();
      Assert.Equal(1, machine.IncompleteBlockCount());
    }
  }

  // Fails when MultiblockLayoutBuilder.Build keeps roles only for non-filler codes: the tap drawn on a
  // filler glyph resolves to nothing.
  [Fact]
  public void AnchorMegablock_RoleOnFiller() {
    foreach (int angle in Angles) {
      var (world, machine, block, _) = Stand(angle);

      BlockPos tap = Assert.Single(machine.CellsWithRole(MetalTap));

      Vec3i turned = ExOrientation.RotateOffset(TapCell, angle);
      Assert.Equal(At.AddCopy(turned.X, turned.Y, turned.Z), tap);
      Assert.Contains(tap, block.Reserved(world.World, At).Select(c => c.Pos));
      Assert.True(machine.OwnsCell(tap));
    }
  }
}
