using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Covers <see cref="BlockEntityMultiblockStructure.CellsAccepting"/>: which footprint cells
/// admit a given block code.</summary>
public class MultiblockCellsAcceptingTests {
  #region Fixture

  private static readonly BlockPos Anchor = new(0, 10, 0);

  /// <summary>The domain-wildcarded shaft glyph the furnaces ship; the <c>*:</c> is required, or a bare
  /// alternation is implicitly <c>game:</c>.</summary>
  private const string ShaftGlyph = "*:@(air|coalpile|furnace-chargepile)";

  /// <summary>The domainless fuel glyph a firebox layout ships.</summary>
  private const string FireboxGlyph = "@(air|coalpile)";

  private static readonly AssetLocation ChargePile = new(
    "iiex:furnace-chargepile"
  );
  private static readonly AssetLocation CoalPile = new("game:coalpile");
  private static readonly AssetLocation Unrelated = new(
    "iiex:furnace-tuyere-n"
  );

  /// <summary>The chiral charge volume: three cells make an L in <c>(x, z)</c>, the fourth sits a level
  /// up.</summary>
  private static readonly Vec3i[] Chargeable =
  [
    new(2, 0, 0),
    new(2, 0, 1),
    new(3, 0, 0),
    new(2, 1, 0),
  ];

  /// <summary>The anchor, one brick, one firebox fuel slot, and the chiral charge volume.</summary>
  private static ExBlockDef Def() =>
    ExBlockDef
      .Create("exlib", "testmega")
      .Multiblock(m => {
        m.Number("exlib:testmega*", 1)
          .Number("exlib:testbrick*", 2)
          .Number(ShaftGlyph, 3)
          .Number(FireboxGlyph, 4)
          .At(0, 0, 0, 1)
          .At(1, 0, 0, 2)
          .At(-1, 0, 0, 4);
        foreach (Vec3i cell in Chargeable)
          m.At(cell.X, cell.Y, cell.Z, 3);
      });

  /// <summary>A layout with one oriented part in it.</summary>
  private static ExBlockDef OrientedDef() =>
    ExBlockDef
      .Create("exlib", "testmega")
      .MultiblockLayout(s =>
        s.Origin(0, 0)
          .Legend('C', "exlib:testmega*")
          .Legend('D', "exlib:testdoor-n")
          .Layer(
            0,
            """
            C D
            """
          )
      );

  private static (TestWorld world, TestMegablock machine) Stand(int angle = 0) {
    var world = new TestWorld();
    var machine = new TestMegablock { Angle = angle };
    world.Place(
      Anchor,
      TestBlocks.Configure(new Block(), "exlib:testmega-n", 1),
      machine
    );
    world.Attach(machine);
    return (world, machine);
  }

  /// <summary>Where <see cref="Chargeable"/> lands in the world at <paramref name="angle"/>, computed
  /// through the shared rotation math.</summary>
  private static string ExpectedAt(int angle) =>
    Render(
      Chargeable.Select(c => {
        Vec3i r = ExOrientation.RotateOffset(c, angle);
        return Anchor.AddCopy(r.X, r.Y, r.Z);
      })
    );

  /// <summary>Cells as one ordered, printable string.</summary>
  private static string Render(IEnumerable<BlockPos> cells) =>
    string.Join(
      ", ",
      cells
        .Select(p => $"({p.X},{p.Y},{p.Z})")
        .OrderBy(s => s, System.StringComparer.Ordinal)
    );

  #endregion

  #region Which cells come back

  [Fact]
  public void Only_the_slots_whose_glyph_admits_the_code_come_back() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, Def()).Complete();

    Assert.Equal(ExpectedAt(0), Render(machine.CellsAccepting(ChargePile)));
  }

  [Fact]
  public void The_same_footprint_answers_a_different_set_for_a_different_block() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, Def()).Complete();

    Assert.Equal(Chargeable.Length + 1, machine.CellsAccepting(CoalPile).Count);
    Assert.Equal(Chargeable.Length, machine.CellsAccepting(ChargePile).Count);
    Assert.Contains(Anchor.AddCopy(-1, 0, 0), machine.CellsAccepting(CoalPile));
    Assert.DoesNotContain(
      Anchor.AddCopy(-1, 0, 0),
      machine.CellsAccepting(ChargePile)
    );
  }

  [Fact]
  public void A_code_no_slot_admits_comes_back_empty() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, Def()).Complete();

    Assert.Empty(machine.CellsAccepting(Unrelated));
    Assert.NotEmpty(machine.CellsAccepting(ChargePile));
  }

  #endregion

  #region Rotation

  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void Every_facing_puts_the_cells_where_that_rotation_says(int angle) {
    var (world, machine) = Stand(angle);
    StructureRig.Around(world, machine, Def(), angle).Complete();

    Assert.Equal(ExpectedAt(angle), Render(machine.CellsAccepting(ChargePile)));
  }

  [Fact]
  public void The_four_facings_are_four_different_footprints() {
    var sets = new List<string>();
    foreach (int angle in new[] { 0, 90, 180, 270 }) {
      var (world, machine) = Stand(angle);
      StructureRig.Around(world, machine, Def(), angle).Complete();
      sets.Add(Render(machine.CellsAccepting(ChargePile)));
    }

    Assert.Equal(4, sets.Distinct().Count());
  }

  [Fact]
  public void Turning_the_structure_moves_the_cells_rather_than_answering_out_of_the_old_facing() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, Def()).Complete();
    Assert.Equal(ExpectedAt(0), Render(machine.CellsAccepting(ChargePile)));

    machine.Angle = 90;
    world.AdvanceBlockEntityTime(3000);

    Assert.Equal(ExpectedAt(90), Render(machine.CellsAccepting(ChargePile)));
  }

  #endregion

  #region Caching

  [Fact]
  public void The_answer_is_computed_once_and_handed_back() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, Def()).Complete();

    // Same instance, not merely equal contents.
    Assert.Same(
      machine.CellsAccepting(ChargePile),
      machine.CellsAccepting(ChargePile)
    );
  }

  [Fact]
  public void A_structure_asked_before_its_layout_arrives_answers_properly_afterwards() {
    var (world, machine) = Stand();

    Assert.Empty(machine.CellsAccepting(ChargePile));

    StructureRig.Around(world, machine, Def()).Complete();

    Assert.Equal(ExpectedAt(0), Render(machine.CellsAccepting(ChargePile)));
  }

  #endregion

  #region Oriented slots

  [Theory]
  [InlineData(0, "n")]
  [InlineData(90, "w")]
  [InlineData(180, "s")]
  [InlineData(270, "e")]
  public void An_oriented_slot_admits_the_variant_the_placed_structure_wants(
    int angle,
    string facing
  ) {
    var (world, machine) = Stand(angle);
    StructureRig.Around(world, machine, OrientedDef(), angle).Complete();

    Assert.Single(
      machine.CellsAccepting(new AssetLocation($"exlib:testdoor-{facing}"))
    );
    if (angle != 0)
      Assert.Empty(
        machine.CellsAccepting(new AssetLocation("exlib:testdoor-n"))
      );
  }

  #endregion
}
