using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Xunit;
using static ExpandedLib.Tests.MultiblockCellRolesFixtures;

namespace ExpandedLib.Tests;

public class MultiblockCellRolesHandEditedAttributeTests {
  /// <summary>Reads a raw <c>multiblockRoles</c> body as a hand-edited JSON patch presents it.</summary>
  private static MultiblockCellRoles ReadRoles(string rolesBody) =>
    MultiblockCellRoles.FromAttributes(
      new Vintagestory.API.Datastructures.JsonObject(
        JObject.Parse("{\"multiblockRoles\":" + rolesBody + "}")
      )
    );

  [Fact]
  public void A_cell_whose_coordinate_is_not_an_int_is_skipped_rather_than_thrown_on() {
    // SetStructureAngle runs on the server tick and a client GetBlockInfo; a throw there repeats live.
    MultiblockCellRoles roles = ReadRoles(
      """
      {
        "Flue": [ {"x":"two","y":0,"z":0},
                  {"x":{"nested":1},"y":0,"z":0},
                  {"x":true,"y":0,"z":0},
                  {"x":null,"y":0,"z":0},
                  {"x":1.5,"y":0,"z":0},
                  {"x":9999999999999,"y":0,"z":0},
                  {"y":0,"z":0},
                  {"x":2,"y":0,"z":1} ]
      }
      """
    );

    // Seven unreadable cells dropped, the one well-formed cell kept.
    Assert.Equal((2, 0, 1), Assert.Single(roles.CellsOf(Flue)));
  }

  [Fact]
  public void A_role_whose_every_cell_is_malformed_reads_as_no_roles_at_all() {
    // Nothing readable means no roles at all, not a role present but empty.
    Assert.True(
      ReadRoles("""{ "Flue": [ {"x":"two","y":0,"z":0} ] }""").IsEmpty
    );
  }

  [Fact]
  public void Any_non_blank_key_reads_as_a_role() {
    // CellRole is an open string key; "99" and "Nonsense" are both valid roles.
    Assert.NotEmpty(
      ReadRoles("""{ "99": [ {"x":0,"y":0,"z":0} ] }""")
        .CellsOf(CellRole.Of("99"))
    );
    Assert.NotEmpty(
      ReadRoles("""{ "Nonsense": [ {"x":0,"y":0,"z":0} ] }""")
        .CellsOf(CellRole.Of("Nonsense"))
    );

    // A key is read back exactly, not case-insensitively: "flue" and "Flue" are different roles.
    Assert.Empty(
      ReadRoles("""{ "flue": [ {"x":0,"y":0,"z":0} ] }""").CellsOf(Flue)
    );
  }
}
