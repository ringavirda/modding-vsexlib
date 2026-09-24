using System.Collections.Generic;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="GridOutputVariantCheck"/> over planted grid recipes whose block output is
/// or is not the creative default of an oriented block.</summary>
public class GridOutputVariantCheckTests {
  private readonly ITestOutputHelper output;

  public GridOutputVariantCheckTests(ITestOutputHelper output) {
    this.output = output;
    TestModDomain.Register();
  }

  private const string File = "stub:recipes/grid/planted.json";

  /// <summary>A grid recipe outputting <paramref name="code"/>, named for it with its
  /// <c>{metal}</c> placeholder filled.</summary>
  private static string Crafts(string code, string type = "block") =>
    $$"""
      {
        "name": "{{code.Replace("{metal}", "iron")}}", "ingredientPattern": "I",
        "width": 1, "height": 1,
        "ingredients": {
          "I": {
            "type": "item", "code": "game:ingot-*", "name": "metal", "allowedVariants": ["iron"]
          }
        },
        "output": { "type": "{{type}}", "code": "{{code}}" }
      }
      """;

  private IReadOnlyList<string> Findings(RecipeStubSource source) {
    IReadOnlyList<string> found = GridOutputVariantCheck
      .Run(source, "stub")
      .Errors;
    foreach (string line in found)
      output.WriteLine(line);
    return found;
  }

  /// <summary>A player-oriented tap listed in creative facing south.</summary>
  private static ExBlockDef Tap(string domain = "stub") =>
    ExBlockDef
      .Create(domain, "tap")
      .VariantGroup("type", "iron", "slag")
      .VariantGroup("side", "n", "e", "s", "w")
      .Behavior("ExOrientable")
      .CreativeTab("general", "*-s");

  /// <summary>A network-oriented tee <paramref name="code"/> of the one type <c>tjunction</c>,
  /// its material group <paramref name="material"/> added by the caller, listed in creative facing
  /// east-south-west.</summary>
  private static ExBlockDef Tee(
    string code,
    System.Func<ExBlockDef, ExBlockDef> material
  ) =>
    material(ExBlockDef.Create("stub", code).VariantGroup("type", "tjunction"))
      .VariantGroup("orientation", "nes", "esw", "swn", "wne")
      .Behavior("ExOrientable", new { mode = "network", scheme = "CanalTee" })
      .CreativeTab("general", "*-tjunction-*-esw");

  private static string Finding(string code, string fallback) =>
    $"{File}#0 ({code}): output {code} is not the creative default {fallback}";

  #region ExOrientable

  // Fails when Run stops reporting an oriented output creative does not list, names another
  // state as the default, or reads an output code without a domain as game's.
  [Fact]
  [PlantedDefect(
    typeof(GridOutputVariantCheck),
    nameof(GridOutputVariantCheck.Run)
  )]
  public void A_non_default_orientation_is_reported() =>
    Assert.Equal(
      [
        Finding("stub:tap-iron-n", "stub:tap-iron-s"),
        $"{File}#2 (tap-iron-n): output stub:tap-iron-n is not the creative default "
          + "stub:tap-iron-s",
      ],
      Findings(
        new RecipeStubSource()
          .Definition(Tap())
          .Recipe(File, Crafts("stub:tap-iron-n"))
          .Recipe(File, Crafts("stub:tap-iron-s"))
          .Recipe(File, Crafts("tap-iron-n"))
      )
    );

  // Fails when Run reads the network group as the player's, stops expanding a named output
  // placeholder, or stops reading an unexpanded one or a worldproperty group as any state.
  [Fact]
  [PlantedDefect(
    typeof(GridOutputVariantCheck),
    nameof(GridOutputVariantCheck.Run)
  )]
  public void A_network_oriented_output_is_held_to_its_orientation_group() =>
    Assert.Equal(
      [
        Finding(
          "stub:canal-tjunction-iron-nes",
          "stub:canal-tjunction-iron-esw"
        ),
        $"{File}#1 (stub:cobble-tjunction-{{rock}}-nes): output "
          + "stub:cobble-tjunction-*-nes is not the creative default "
          + "stub:cobble-tjunction-*-esw",
        $"{File}#2 (stub:canal-tjunction-{{alloy}}-nes): output "
          + "stub:canal-tjunction-*-nes is not the creative default "
          + "stub:canal-tjunction-*-esw",
      ],
      Findings(
        new RecipeStubSource()
          .Definition(Tee("canal", d => d.VariantGroup("metal", "iron")))
          .Definition(
            Tee(
              "cobble",
              d => d.VariantGroupFromProperties("rock", "block/rock")
            )
          )
          .Recipe(File, Crafts("stub:canal-tjunction-{metal}-nes"))
          .Recipe(File, Crafts("stub:cobble-tjunction-{rock}-nes"))
          .Recipe(File, Crafts("stub:canal-tjunction-{alloy}-nes"))
      )
    );

  // Fails when Run holds a non-orientation group, an unlisted block, an item, an uncovered domain,
  // a group the behaviour writes but the block lacks, or throws on an output with no code.
  [Fact]
  public void Only_the_orientation_group_of_a_family_block_output_is_held() =>
    Assert.Empty(
      Findings(
        new RecipeStubSource()
          .Definition(Tap())
          .Definition(
            ExBlockDef
              .Create("stub", "hearth")
              .VariantGroup("tier", "tier1", "tier2")
              .VariantGroup("side", "n", "e", "s", "w")
              .Behavior("ExOrientable")
              .CreativeTab("general", "*-tier1-n")
          )
          .Definition(
            ExBlockDef
              .Create("stub", "hopper")
              .VariantGroup("side", "n", "e", "s", "w")
              .Behavior("ExOrientable")
              .CreativeTab("general", "*")
          )
          .Definition(
            ExBlockDef
              .Create("stub", "bevel")
              .VariantGroup("side", "n", "e", "s", "w")
              .Behavior("ExOrientable")
          )
          .Definition(
            ExBlockDef
              .Create("stub", "rail")
              .VariantGroup("orientation", "ns", "we")
              .Behavior("ExOrientable")
              .CreativeTab("general", "*-ns")
          )
          .Definition(Tap("game"))
          .Covering("stub")
          .Recipe(
            File,
            """{ "ingredientPattern": "I", "width": 1, "height": 1 }"""
          )
          .Recipe(
            File,
            """
            { "ingredientPattern": "I", "width": 1, "height": 1, "output": { "type": "block" } }
            """
          )
          .Recipe(File, Crafts("stub:hearth-tier2-n"))
          .Recipe(File, Crafts("stub:hopper-e"))
          .Recipe(File, Crafts("stub:bevel-e"))
          .Recipe(File, Crafts("stub:rail-we"))
          .Recipe(File, Crafts("stub:tap-iron-n", "item"))
          .Recipe(File, Crafts("game:tap-iron-n"))
      )
    );

  #endregion

  #region Vanilla orientation behaviours

  // Fails when Run stops reading HorizontalOrientable's side fallback, the JSON blocktypes, or a
  // {group} placeholder in a creative entry.
  [Fact]
  public void A_json_blocktype_under_HorizontalOrientable_is_held_by_its_side_group() =>
    Assert.Equal(
      [
        Finding(
          "stub:blastfurnacetap-tier1-north",
          "stub:blastfurnacetap-tier1-south"
        ),
      ],
      Findings(
        new RecipeStubSource()
          .BlockType(
            "stub:blocktypes/tap.json",
            """
            {
              "code": "blastfurnacetap",
              "behaviors": [{ "name": "HorizontalOrientable" }],
              "variantgroups": [
                { "code": "refractory", "states": ["tier1", "tier2"] },
                { "code": "side", "states": ["north", "east", "south", "west"] }
              ],
              "creativeinventory": { "general": ["*-{refractory}-south"] }
            }
            """
          )
          .Recipe(File, Crafts("stub:blastfurnacetap-tier1-north"))
      )
    );

  // Fails when Run drops the group NWOrientable, Pillar or OmniRotatable writes, or reads a
  // block whose groups add rather than multiply.
  [Theory]
  [InlineData("NWOrientable", "orientation", "ns", "we", "{}")]
  [InlineData(
    "Pillar",
    "axis",
    "ud",
    "ns",
    """{ "rotationVariantCode": "axis" }"""
  )]
  [InlineData("OmniRotatable", "rot", "up", "down", "{}")]
  public void Each_vanilla_orientation_behaviour_names_its_group(
    string behavior,
    string group,
    string listed,
    string other,
    string properties
  ) {
    string type = $$"""
      {
        "code": "column",
        "behaviors": [{ "name": "{{behavior}}", "properties": {{properties}} }],
        "variantgroups": [{ "code": "{{group}}", "states": ["{{listed}}", "{{other}}"] }],
        "creativeinventory": { "general": ["*-{{listed}}"] }
      }
      """;
    Assert.Equal(
      [Finding($"stub:column-{other}", $"stub:column-{listed}")],
      Findings(
        new RecipeStubSource()
          .BlockType("stub:blocktypes/column.json", type)
          .Recipe(File, Crafts($"stub:column-{other}"))
      )
    );
    Assert.Empty(
      Findings(
        new RecipeStubSource()
          .BlockType(
            "stub:blocktypes/column.json",
            type.Replace(
              $"\"code\": \"{group}\",",
              $"\"code\": \"{group}\", \"combine\": \"add\","
            )
          )
          .Recipe(File, Crafts($"stub:column-{other}"))
      )
    );
  }

  #endregion
}
