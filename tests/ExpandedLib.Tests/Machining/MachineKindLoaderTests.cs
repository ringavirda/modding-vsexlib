using System.Linq;
using System.Numerics;
using ExpandedLib.Industry.Machining;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The machine kind catalogue: JSON declarations, validation, step order, the merge rule and the block attribute.</summary>
public class MachineKindLoaderTests {
  private const string Lathe = """
    {
      "schema": 1,
      "kinds": [
        { "code": "test:lathe", "ratedSpeed": 4, "standing": 2.5, "fixture": true,
          "anchors": { "chuck": { "origin": [1, 2, 3], "rotation": [0, 90, 0], "scale": 0.5 } },
          "steps": {
            "turn": { "motion": "rotary", "power": 360, "tooling": "cutter", "anchor": "chuck",
                      "envelope": { "across": 12, "length": 48 } },
            "bore": { "motion": "rotary", "power": 300, "tooling": "boringhead" },
            "plane": { "motion": "reciprocating", "power": 200, "tooling": "cutter" }
          } }
      ]
    }
    """;

  private static MachineKindRegistry Registry(params string[] files) {
    var registry = new MachineKindRegistry();
    MachineKindLoader.Load(
      files.Select((json, i) => ($"test:config/machinekinds/f{i}.json", json)),
      registry
    );
    return registry;
  }

  private static string Bad(string kindBody) =>
    $$"""{ "schema": 1, "kinds": [ { "code": "test:m", "ratedSpeed": 4, {{kindBody}} } ] }""";

  // Fails when the kinds' steps are sorted by name after load: turn, bore, plane is not alphabetical.
  [Fact]
  public void A_kinds_steps_keep_their_declaration_order() {
    MachineKindRegistry registry = Registry(Lathe);

    Assert.True(registry.TryGet("test:lathe", out MachineKind? kind));
    Assert.Equal(["turn", "bore", "plane"], kind!.Steps.Select(s => s.Step));
  }

  [Fact]
  public void A_kind_declared_in_json_carries_its_figures() {
    Assert.True(Registry(Lathe).TryGet("test:lathe", out MachineKind? kind));

    Assert.Equal(4f, kind!.RatedSpeed);
    Assert.Equal(2.5f, kind.StandingNm);
    Assert.True(kind.Fixture);
    MachineKindStep turn = kind.StepOf("turn")!;
    Assert.Equal(CutMotion.Rotary, turn.Motion);
    Assert.Equal(360f, turn.Power);
    Assert.Equal("cutter", turn.Tooling);
    Assert.Equal(new MachineEnvelope(12f, 48f), turn.Envelope);
    Assert.Equal(CutMotion.Reciprocating, kind.StepOf("plane")!.Motion);
    MachineAnchor anchor = Assert.Single(kind.Anchors);
    Assert.Equal(new Vector3(1, 2, 3), anchor.Origin);
    Assert.Equal(new Vector3(0, 90, 0), anchor.Rotation);
    Assert.Equal(0.5f, anchor.Scale);
  }

  [Fact]
  public void Offering_lists_the_kinds_that_declare_a_step() {
    MachineKindRegistry registry = Registry(
      Lathe,
      """
      { "schema": 1, "kinds": [ { "code": "test:drill", "ratedSpeed": 3,
        "steps": { "drill": { "motion": "rotary", "power": 100, "tooling": "bit" } } } ] }
      """
    );

    Assert.Equal(["test:lathe"], registry.Offering("bore").Select(k => k.Code));
    Assert.Equal(
      ["test:drill"],
      registry.Offering("drill").Select(k => k.Code)
    );
    Assert.Empty(registry.Offering("slot"));
  }

  // Fails when a step may name an anchor the kind does not declare.
  [Fact]
  public void A_step_naming_an_undeclared_anchor_is_rejected() {
    Assert.Equal(
      0,
      Registry(
        Bad(
          "\"steps\": { \"turn\": { \"motion\": \"rotary\", \"power\": 1, \"tooling\": \"c\", \"anchor\": \"nowhere\" } }"
        )
      ).All.Count
    );
  }

  // Each input is rejected by one guard only.
  [Theory]
  [InlineData(
    "\"steps\": { \"turn\": { \"motion\": \"rotary\", \"power\": 0, \"tooling\": \"c\" } }"
  )]
  [InlineData(
    "\"steps\": { \"turn\": { \"motion\": \"spinning\", \"power\": 1, \"tooling\": \"c\" } }"
  )]
  [InlineData(
    "\"steps\": { \"turn\": { \"motion\": \"7\", \"power\": 1, \"tooling\": \"c\" } }"
  )]
  [InlineData(
    "\"steps\": { \"turn\": { \"motion\": \"1\", \"power\": 1, \"tooling\": \"c\" } }"
  )]
  [InlineData(
    "\"steps\": { \"turn\": { \"motion\": \"rotary, reciprocating\", \"power\": 1, \"tooling\": \"c\" } }"
  )]
  [InlineData(
    "\"steps\": { \"turn\": { \"motion\": \"rotary\", \"power\": 1 } }"
  )]
  public void A_malformed_step_rejects_the_file(string body) {
    Assert.Equal(0, Registry(Bad(body)).All.Count);
  }

  [Fact]
  public void A_code_without_a_domain_and_a_speed_of_zero_each_reject_the_file() {
    Assert.Equal(
      0,
      Registry(
        """{ "schema": 1, "kinds": [ { "code": "lathe", "ratedSpeed": 4 } ] }"""
      ).All.Count
    );
    Assert.Equal(
      0,
      Registry(
        """{ "schema": 1, "kinds": [ { "code": "test:m", "ratedSpeed": 0 } ] }"""
      ).All.Count
    );
  }

  [Fact]
  public void A_kind_redeclared_with_other_figures_is_reported_and_the_first_stands() {
    MachineKindRegistry registry = Registry(Lathe);
    var other = new MachineKindSet(
      1,
      [new MachineKind("test:lathe", 9f, 0f, false, [], [])]
    );

    Assert.Contains("test:lathe", Assert.Single(registry.Contribute(other)));
    Assert.Empty(registry.Contribute(new MachineKindSet(1, [registry.All[0]])));
    Assert.Equal(4f, registry.All[0].RatedSpeed);
    Assert.Single(registry.All);
  }

  [Fact]
  public void A_block_names_its_kind_in_the_machinekind_attribute() {
    MachineKindRegistry registry = Registry(Lathe);
    var block = new Block {
      Attributes = new JsonObject(
        new JObject { [MachineKind.AttributeKey] = "test:lathe" }
      ),
    };

    Assert.Equal("test:lathe", MachineKind.Of(block, registry)!.Code);
    Assert.Null(MachineKind.Of(new Block(), registry));
  }
}
