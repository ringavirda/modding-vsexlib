using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Xunit;
using static ExpandedLib.Tests.MultiblockCellRolesFixtures;

namespace ExpandedLib.Tests;

public class MultiblockCellRolesRotationTests {
  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void Every_facing_puts_the_role_cells_where_that_rotation_says(
    int angle
  ) {
    var (world, machine) = Stand(angle);
    StructureRig.Around(world, machine, RoledDef(), angle).Complete();

    // The chiral L discriminates a wrong-direction turn; all four angles rule out a mapping that is only
    // correct at 0 and 180.
    Assert.Equal(
      ExpectedAt(FlueCells, angle),
      Render(machine.CellsWithRole(Flue))
    );
    Assert.Equal(
      ExpectedAt(TuyereCells, angle),
      Render(machine.CellsWithRole(Tuyere))
    );
  }

  [Fact]
  public void The_four_facings_are_four_different_role_footprints() {
    var sets = new List<string>();
    foreach (int angle in new[] { 0, 90, 180, 270 }) {
      var (world, machine) = Stand(angle);
      StructureRig.Around(world, machine, RoledDef(), angle).Complete();
      sets.Add(Render(machine.CellsWithRole(Flue)));
    }

    // Four distinct sets rules out a mapping that ignores the angle.
    Assert.Equal(4, sets.Distinct().Count());
  }

  [Fact]
  public void A_role_cell_is_always_a_cell_the_structure_owns() {
    foreach (int angle in new[] { 0, 90, 180, 270 }) {
      var (world, machine) = Stand(angle);
      StructureRig.Around(world, machine, RoledDef(), angle).Complete();

      // Roles resolve against vanilla's transformed offset table; the negative assertion above proves
      // OwnsCell actually discriminates.
      Assert.False(
        machine.OwnsCell(Anchor.AddCopy(0, -1, 0)),
        $"the cell under the anchor is below layer 0, so it is not owned at {angle} deg"
      );
      Assert.All(
        machine.CellsWithRole(Flue).Concat(machine.CellsWithRole(Tuyere)),
        cell => Assert.True(machine.OwnsCell(cell), $"{cell} at {angle} deg")
      );
    }
  }

  [Fact]
  public void Turning_the_structure_moves_the_role_cells_rather_than_answering_out_of_the_old_facing() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, RoledDef()).Complete();
    Assert.Equal(ExpectedAt(FlueCells, 0), Render(machine.CellsWithRole(Flue)));

    // A wrench turn: the machine re-derives its angle and reloads on the next monitor tick.
    machine.Angle = 90;
    world.AdvanceBlockEntityTime(3000);

    Assert.Equal(
      ExpectedAt(FlueCells, 90),
      Render(machine.CellsWithRole(Flue))
    );
  }
}
