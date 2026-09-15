using System.Linq;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Xunit;
using static ExpandedLib.Tests.MultiblockCellRolesFixtures;

namespace ExpandedLib.Tests;

public class MultiblockCellRolesManyGlyphsOneCodeTests {
  [Fact]
  public void Two_glyphs_on_one_code_share_a_block_number_and_emit_one_entry() {
    // `blockNumbers` is keyed by code, not by glyph: six glyphs, four codes, four numbers.
    var numbers = (JObject)
      Attributes(RoledDef())["multiblockStructure"]!["blockNumbers"]!;

    Assert.Equal(
      """{"exlib:testmega*":1,"exlib:testbrick*":2,"game:air":3,"exlib:testtuyere*":4}""",
      numbers.ToString(Newtonsoft.Json.Formatting.None)
    );

    // Every offset resolves to one of those four.
    var offsets = (JArray)
      Attributes(RoledDef())["multiblockStructure"]!["offsets"]!;
    Assert.NotEmpty(offsets);
    Assert.All(
      offsets,
      o => Assert.InRange((int)o["w"]!, 1, numbers.Properties().Count())
    );
  }

  [Fact]
  public void One_code_two_glyphs_one_role_answers_only_the_role_glyphs_cells() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, RoledDef()).Complete();

    // Both `a` and `v` are `game:air`: five cells accept air, four of them (the `v` cells) are the flue.
    Assert.Equal(
      5,
      machine.CellsAccepting(new AssetLocation("game:air")).Count
    );
    Assert.Equal(ExpectedAt(FlueCells, 0), Render(machine.CellsWithRole(Flue)));
    Assert.DoesNotContain(
      Anchor.AddCopy(1, 0, 0), // the `a` cell - air, not flue
      machine.CellsWithRole(Flue)
    );
  }

  [Fact]
  public void Two_glyphs_sharing_a_role_both_contribute_their_cells() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, RoledDef()).Complete();

    // Two glyphs, one role: a role is a set, not a glyph alias.
    Assert.Equal(
      ExpectedAt(TuyereCells, 0),
      Render(machine.CellsWithRole(Tuyere))
    );
  }

  [Fact]
  public void A_cell_that_is_two_things_at_once_answers_to_both_roles_at_runtime() {
    var (world, machine) = Stand();
    StructureRig.Around(world, machine, OverlappingDef()).Complete();

    // Both roles answer the same cell and are cached independently.
    Assert.Equal(
      Render([Anchor.AddCopy(2, 0, 0)]),
      Render(machine.CellsWithRole(Flue))
    );
    Assert.Equal(
      Render(machine.CellsWithRole(Flue)),
      Render(machine.CellsWithRole(Damper))
    );
    Assert.NotSame(machine.CellsWithRole(Flue), machine.CellsWithRole(Damper));

    // The overlap belongs to the glyph, not the code: the neighbouring `a` cell carries neither role.
    Assert.Equal(
      2,
      machine.CellsAccepting(new AssetLocation("game:air")).Count
    );
  }
}
