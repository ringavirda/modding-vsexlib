using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="StageWildcardsCheck"/>'s rules, each over a planted stage table that breaks
/// that rule alone, and <see cref="StageWildcards"/> over definitions read from this assembly.</summary>
public class StageWildcardsTests {
  private readonly ITestOutputHelper output;

  public StageWildcardsTests(ITestOutputHelper output) {
    this.output = output;
    TestModDomain.Register();
  }

  private const string Domain = "plantedstages";

  /// <summary>A pipe whose <c>*</c> spans orientation and metal, and a crate grouped by metal
  /// alone, as the catalogue family wildcards are decided against.</summary>
  private static IEnumerable<ExBlockDef> Catalogue(string domain) =>
    [
      ExBlockDef
        .Create(domain, "pipe")
        .VariantGroup("orientation", "ns", "we")
        .VariantGroup("metal", "iron", "steel"),
      ExBlockDef.Create(domain, "crate").VariantGroup("metal", "iron"),
    ];

  /// <summary>A block <c>rig</c>, grouped by <c>brick</c>, whose stage table is
  /// <paramref name="stages"/>, one JSON array of ingredients per stage, stage 0 first.</summary>
  private static ExBlockDef Rig(string domain, params string[] stages) =>
    ExBlockDef
      .Create(domain, "rig")
      .VariantGroup("brick", "red", "tan")
      .EntityBehavior(
        "ExRightClickConstructable",
        new JObject {
          ["stages"] = new JArray(
            stages.Select(s => new JObject {
              ["requireStacks"] = JArray.Parse(s),
            })
          ),
        }
      );

  private sealed class StubSource(
    IEnumerable<ExBlockDef> defs,
    params (AssetLocation, JObject)[] types
  ) : ICheckSource {
    public IEnumerable<string> Domains => [Domain];
    public IEnumerable<AssetLocation> BlockCodes => [];
    public IEnumerable<AssetLocation> ItemCodes => [];

    public IEnumerable<(AssetLocation File, JObject Json)> Recipes(
      string domain
    ) => [];

    public IEnumerable<(string Locale, JObject Json)> Lang(string domain) => [];

    public IEnumerable<ExBlockDef> BlockDefinitions(string domain) =>
      domain == Domain ? defs : [];

    public IEnumerable<(AssetLocation File, JObject Json)> BlockTypes(
      string domain
    ) => domain == Domain ? types : [];
  }

  // The findings for a rig of the given stages; each is written to the test output.
  private IReadOnlyList<string> Findings(params string[] stages) {
    IReadOnlyList<string> found = StageWildcardsCheck
      .Run(new StubSource([.. Catalogue(Domain), Rig(Domain, stages)]), Domain)
      .Errors;
    foreach (string line in found)
      output.WriteLine(line);
    return found;
  }

  private const string Paid =
    """[{ "type": "item", "code": "game:metalplate-*", "storeWildCard": "metal", "quantity": 2 }]""";

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Rule_a_a_wildcard_without_storeWildCard_is_reported() =>
    Assert.Equal(
      [
        "plantedstages:rig stage 1 game:plank-*: (a) a wildcard without storeWildCard",
      ],
      Findings(
        "[]",
        """[{ "type": "item", "code": "game:plank-*", "quantity": 4 }]"""
      )
    );

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Rule_b_a_star_over_orientation_and_metal_is_reported() =>
    Assert.Equal(
      [
        "plantedstages:rig stage 1 plantedstages:pipe-*: (b) * spans "
          + "[metal, orientation], stores metal",
      ],
      Findings(
        "[]",
        """[{ "type": "block", "code": "plantedstages:pipe-*", "storeWildCard": "metal", "quantity": 2 }]"""
      )
    );

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Rule_c_a_key_that_is_no_group_of_the_match_is_reported() =>
    Assert.Equal(
      [
        "plantedstages:rig stage 1 plantedstages:crate-*: (c) wood is no variant group of "
          + "plantedstages:crate-iron",
      ],
      Findings(
        "[]",
        """[{ "type": "block", "code": "plantedstages:crate-*", "storeWildCard": "wood", "quantity": 1 }]"""
      )
    );

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Rule_c_a_wildcard_matching_nothing_is_reported() =>
    Assert.Equal(
      ["plantedstages:rig stage 1 plantedstages:barrel-*: (c) matches nothing"],
      Findings(
        "[]",
        """[{ "type": "block", "code": "plantedstages:barrel-*", "storeWildCard": "wood", "quantity": 1 }]"""
      )
    );

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Rules_b_and_c_report_a_family_item_wildcard_undecidable() =>
    Assert.Equal(
      [
        "plantedstages:rig stage 1 plantedstages:gear-*: (b, c) undecidable: no item "
          + "variant groups",
      ],
      Findings(
        "[]",
        """[{ "type": "item", "code": "plantedstages:gear-*", "storeWildCard": "metal", "quantity": 1 }]"""
      )
    );

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Rule_d_a_key_the_creative_build_never_stores_is_reported() =>
    Assert.Equal(
      [
        "plantedstages:rig stage 1 game:rock-*: (d) key rock is not seeded by the creative build",
      ],
      Findings(
        "[]",
        """[{ "type": "block", "code": "game:rock-*", "storeWildCard": "rock", "quantity": 1 }]"""
      )
    );

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Rule_e_a_placeholder_no_earlier_paid_stage_stores_is_reported() =>
    Assert.Equal(
      [
        "plantedstages:rig stage 1 game:metalplate-{metal}: (e) {metal} is stored by no "
          + "earlier paid stage",
      ],
      Findings(
        "[]",
        """[{ "type": "item", "code": "game:metalplate-{metal}", "quantity": 1 }]"""
      )
    );

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Rule_e_a_key_stored_only_by_the_same_stage_is_reported() =>
    Assert.Equal(
      [
        "plantedstages:rig stage 1 game:rod-{metal}: (e) {metal} is stored by no earlier "
          + "paid stage",
      ],
      Findings(
        "[]",
        """[{ "type": "item", "code": "game:metalplate-*", "storeWildCard": "metal", "quantity": 2 }, { "type": "item", "code": "game:rod-{metal}", "quantity": 1 }]"""
      )
    );

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Rule_f_a_stage_0_key_stage_1_never_stores_is_reported() =>
    Assert.Equal(
      [
        "plantedstages:rig stage 0 game:plank-*: (f) stage 0 key wood is not stored by stage 1",
      ],
      Findings(
        """[{ "type": "item", "code": "game:plank-*", "storeWildCard": "wood", "quantity": 4 }]""",
        "[]"
      )
    );

  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void A_stage_0_rock_key_is_reported_under_rules_d_and_f() =>
    Assert.Equal(
      [
        "plantedstages:rig stage 0 game:rock-*: (d) key rock is not seeded by the creative build",
        "plantedstages:rig stage 0 game:rock-*: (f) stage 0 key rock is not stored by stage 1",
      ],
      Findings(
        """[{ "type": "block", "code": "game:rock-*", "storeWildCard": "rock", "quantity": 1 }]""",
        Paid
      )
    );

  [Fact]
  public void Stored_keys_and_the_blocks_own_groups_fill_placeholders() =>
    Assert.Empty(
      Findings(
        """[{ "type": "item", "code": "game:burnedbrick-{brick}", "quantity": 4 }, { "type": "item", "code": "game:nails-{metal}", "quantity": 1 }]""",
        Paid,
        """[{ "type": "item", "code": "game:rod-{metal}", "quantity": 1 }, { "type": "item", "code": "game:ingot-{metal}", "quantity": 1 }]"""
      )
    );

  [Fact]
  public void Allowed_variants_narrow_a_family_star_to_the_keys_group() =>
    Assert.Empty(
      Findings(
        "[]",
        """[{ "type": "block", "code": "plantedstages:pipe-ns-*", "storeWildCard": "metal", "quantity": 1 }, { "type": "block", "code": "plantedstages:pipe-*", "allowedVariants": ["ns-iron", "ns-steel"], "storeWildCard": "metal", "quantity": 1 }]"""
      )
    );

  // Fails when Run stops reading JSON blocktypes, reads a definition's own asset again, or reads
  // one of two definitions sharing a code.
  [Fact]
  [PlantedDefect(typeof(StageWildcardsCheck), nameof(StageWildcardsCheck.Run))]
  public void Json_blocktypes_and_definitions_sharing_a_code_are_each_read_once() {
    JObject rig = Rig(
        Domain,
        "[]",
        """[{ "type": "item", "code": "game:plank-*", "quantity": 4 }]"""
      )
      .ToJson();
    JObject json = (JObject)rig.DeepClone();
    json["code"] = "jsonrig";

    IReadOnlyList<string> found = StageWildcardsCheck
      .Run(
        new StubSource(
          [
            ExBlockDef
              .Create(Domain, "rig")
              .EntityBehavior(
                "ExRightClickConstructable",
                (JObject)rig["entityBehaviors"]![0]!["properties"]!
              ),
            Rig(
              Domain,
              "[]",
              """[{ "type": "block", "code": "game:rock-*", "storeWildCard": "rock", "quantity": 1 }]"""
            ),
          ],
          (new AssetLocation(Domain, "blocktypes/rig.json"), rig),
          (new AssetLocation(Domain, "blocktypes/jsonrig.json"), json)
        ),
        Domain
      )
      .Errors;

    Assert.Equal(
      [
        "plantedstages:rig stage 1 game:plank-*: (a) a wildcard without storeWildCard",
        "plantedstages:rig stage 1 game:rock-*: (d) key rock is not seeded by the creative build",
        "plantedstages:jsonrig stage 1 game:plank-*: (a) a wildcard without storeWildCard",
      ],
      found
    );
  }

  /// <summary>Declares the catalogue and a rig with a keyless wildcard only under
  /// <see cref="Planted"/>, so other scans of this assembly never see them.</summary>
  private sealed class PlantedRig : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain == Planted
        ?
        [
          .. Catalogue(domain),
          Rig(
            domain,
            "[]",
            """[{ "type": "item", "code": "game:plank-*", "quantity": 4 }]"""
          ),
        ]
        : [];
  }

  private const string Planted = "plantedstagedefs";

  [Fact]
  [PlantedDefect(typeof(StageWildcards), nameof(StageWildcards.Check))]
  public void Check_counts_the_stages_it_reads_and_reports_their_findings() {
    StageWildcards.Result result = StageWildcards.Check(
      Planted,
      (Planted, typeof(StageWildcardsTests).Assembly)
    );

    Assert.Equal(1, result.Blocks);
    Assert.Equal(2, result.Stages);
    Assert.Equal(
      [
        "plantedstagedefs:rig stage 1 game:plank-*: (a) a wildcard without storeWildCard",
      ],
      result.Findings
    );
  }

  [Fact]
  public void Check_refuses_a_family_without_the_domain() =>
    Assert.Throws<ArgumentException>(() =>
      StageWildcards.Check(
        Planted,
        ("other", typeof(StageWildcardsTests).Assembly)
      )
    );
}
