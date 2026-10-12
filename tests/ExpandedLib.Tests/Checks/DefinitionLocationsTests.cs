using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="DefinitionLocations"/> over planted definitions.</summary>
public class DefinitionLocationsTests {
  [Fact]
  [PlantedDefect(
    typeof(DefinitionLocations),
    nameof(DefinitionLocations.Check)
  )]
  public void Two_definitions_on_one_location_are_reported() {
    DefinitionLocations.Result result = DefinitionLocations.Check([
      ExBlockDef.Create("planted", "pipe"),
      ExBlockDef.Create("planted", "valve"),
      ExBlockDef.Create("planted", "pipe").Resistance(9f),
    ]);

    Assert.Equal(3, result.Definitions);
    Assert.Equal(
      ["planted/blocktypes/pipe.json: 2 definitions share this location"],
      result.Findings
    );
  }

  [Fact]
  public void A_location_differing_by_case_is_shared() =>
    Assert.Single(
      DefinitionLocations
        .Check([
          ExBlockDef.Create("planted", "Pipe"),
          ExBlockDef.Create("planted", "pipe"),
        ])
        .Findings
    );

  [Fact]
  public void A_block_and_an_item_of_one_name_do_not_share() =>
    Assert.Empty(
      DefinitionLocations
        .Check([
          ExBlockDef.Create("planted", "pipe"),
          ExItemDef.Create("planted", "pipe"),
        ])
        .Findings
    );
}
