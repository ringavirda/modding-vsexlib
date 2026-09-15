using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Xunit;
using static ExpandedLib.Tests.MultiblockCellRolesFixtures;

namespace ExpandedLib.Tests;

public class MultiblockCellRolesCachingTests {
  [Fact]
  public void The_role_answer_is_computed_once_and_handed_back() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, RoledDef()).Complete();

    // The footprint is not re-walked per read: the same instance comes back, not merely equal contents.
    Assert.Same(machine.CellsWithRole(Flue), machine.CellsWithRole(Flue));
  }

  [Fact]
  public void A_structure_asked_before_its_layout_arrives_answers_properly_afterwards() {
    var (world, machine) = Stand();

    // The block carries no attributes yet: a client-side block entity awaiting its layout.
    Assert.Empty(machine.CellsWithRole(Flue));

    StructureRig.Around(world, machine, RoledDef()).Complete();

    // The early read does not poison the cache: the late-arriving layout is answered in full.
    Assert.Equal(ExpectedAt(FlueCells, 0), Render(machine.CellsWithRole(Flue)));
  }
}
