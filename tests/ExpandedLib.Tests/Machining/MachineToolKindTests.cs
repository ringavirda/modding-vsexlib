using ExpandedLib.Catalogues;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The machine tool's optional kind and spent drop, and the schema each form writes.</summary>
public class MachineToolKindTests {
  private static JsonObject Attr(object tier) =>
    new JsonObject(JObject.FromObject(tier))[MachineTool.AttributeKey];

  private static ItemStack Tool(object tier) =>
    new(new Item { Attributes = new JsonObject(JObject.FromObject(tier)) });

  // Fails when schema 1 reads a default kind instead of null.
  [Fact]
  public void A_schema_1_tool_reads_a_null_kind() {
    Assert.True(
      MachineTool.TryParse(
        Attr(MachineTool.Tier(2)),
        out MachineToolSpec spec,
        out _
      )
    );

    Assert.Equal(2, spec.Tier);
    Assert.Null(spec.Kind);
    Assert.Null(spec.SpentCode);
    Assert.Null(MachineTool.KindOf(Tool(MachineTool.Tier(2))));
  }

  // Fails when Tier(int) writes the kinded schema.
  [Fact]
  public void Tier_without_a_kind_still_writes_schema_1() {
    Assert.Equal(1, Attr(MachineTool.Tier(2))["schema"].AsInt());
    Assert.Equal(MachineTool.CurrentSchema, 1);
  }

  [Fact]
  public void A_kinded_tool_writes_schema_2_and_reads_back_with_its_spent_drop() {
    object tier = MachineTool.Tier(3, "bit", "test:ironbits", 4);

    Assert.Equal(MachineTool.KindedSchema, Attr(tier)["schema"].AsInt());
    Assert.True(
      MachineTool.TryParse(Attr(tier), out MachineToolSpec spec, out _)
    );
    Assert.Equal(new MachineToolSpec(3, "bit", "test:ironbits", 4), spec);
    Assert.Equal("bit", MachineTool.KindOf(Tool(tier)));
    Assert.Equal(3, MachineTool.TierOf(Tool(tier)));
    Assert.True(MachineTool.IsTool(Tool(tier)));
  }

  [Fact]
  public void A_kinded_tool_with_no_spent_drop_reads_none() {
    Assert.True(
      MachineTool.TryParse(
        Attr(MachineTool.Tier(1, "cutter")),
        out MachineToolSpec spec,
        out _
      )
    );

    Assert.Equal("cutter", spec.Kind);
    Assert.Null(spec.SpentCode);
  }

  [Fact]
  public void A_schema_newer_than_the_kinded_one_is_rejected() {
    var node = Attr(new { machinetool = new { schema = 3, tier = 1 } });

    Assert.False(MachineTool.TryParse(node, out int _, out string? error));
    Assert.Contains("schema", error);
  }

  [Fact]
  public void A_spent_drop_with_no_count_above_zero_is_rejected() {
    var node = Attr(
      new {
        machinetool = new {
          schema = 2,
          tier = 1,
          kind = "bit",
          spent = new { code = "test:bits", count = 0 },
        },
      }
    );

    Assert.False(
      MachineTool.TryParse(node, out MachineToolSpec _, out string? error)
    );
    Assert.Contains("spent", error);
  }

  [Fact]
  public void The_kinded_itemtype_carries_the_kind_on_every_variant() {
    var def = MachineTool.Itemtype(
      "test",
      "cutter",
      "cutter",
      new System.Collections.Generic.Dictionary<string, int> { ["iron"] = 2 },
      "game:item/ingot",
      "type",
      null,
      null,
      1
    );

    Assert.Equal(
      "cutter",
      (string?)
        def.ToJson()["attributesByType"]?["*-iron"]?["machinetool"]?["kind"]
    );
  }
}
