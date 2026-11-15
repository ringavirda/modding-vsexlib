using ExpandedLib.Assembling;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>What <see cref="ExBlockDef.HandAssembly"/> writes into a def.</summary>
public class HandAssemblyDefTests {
  public HandAssemblyDefTests() => TestModDomain.Register();

  // Fails when the behaviour is appended after the def's others instead of put first.
  [Fact]
  public void The_behaviour_runs_before_every_other_the_def_declares_and_the_attribute_is_written() {
    JObject json = ExBlockDef
      .Create("test", "c")
      .Behavior("Lockable")
      .HandAssembly("game:wrench-*", "iiex:bolt", 2)
      .Behavior("Another")
      .ToJson();

    var behaviors = (JArray)json["behaviors"]!;
    Assert.Equal("HandAssembly", (string?)behaviors[0]!["name"]);
    Assert.Equal("Lockable", (string?)behaviors[1]!["name"]);
    var spec = json["attributes"]!["handAssembly"]!;
    Assert.Equal("game:wrench-*", (string?)spec["tool"]);
    Assert.Equal("iiex:bolt", (string?)spec["fastener"]);
    Assert.Equal(2, (int?)spec["perJoint"]);
  }

  // Fails when the cap argument is dropped from the written attribute, or a def with no cap is given a cap key.
  [Fact]
  public void A_cap_is_written_beside_the_tool_and_left_out_when_not_given() {
    JObject capped = ExBlockDef
      .Create("test", "c")
      .HandAssembly("game:wrench-*", "iiex:bolt", 2, "iiex:pipecap-cast")
      .ToJson();
    JObject bare = ExBlockDef
      .Create("test", "b")
      .HandAssembly("game:wrench-*", "iiex:bolt", 2)
      .ToJson();

    Assert.Equal(
      "iiex:pipecap-cast",
      (string?)capped["attributes"]!["handAssembly"]!["cap"]
    );
    Assert.Null(bare["attributes"]!["handAssembly"]!["cap"]);
  }

  // Fails when a def that is not a node is left with no block entity to carry the record.
  [Fact]
  public void A_def_with_no_block_entity_gets_the_record_entity_and_behaviour() {
    JObject json = ExBlockDef
      .Create("test", "plain")
      .HandAssembly("game:wrench-*", "iiex:bolt", 1)
      .ToJson();

    Assert.Equal(
      EntityRegistry.KeyFor("test", typeof(BlockEntityAssembled)),
      (string?)json["entityClass"]
    );
    Assert.Equal(
      EntityRegistry.KeyFor("test", typeof(BEBehaviorAssembled)),
      (string?)json["entityBehaviors"]![0]!["name"]
    );
  }

  // Fails when the def's own block entity is replaced by the record entity.
  [Fact]
  public void A_def_with_its_own_block_entity_keeps_it_and_gains_the_behaviour() {
    JObject json = ExBlockDef
      .Create("test", "node")
      .EntityClass("test.NodeEntity")
      .HandAssembly("game:wrench-*", "iiex:bolt", 2)
      .ToJson();

    Assert.Equal("test.NodeEntity", (string?)json["entityClass"]);
    Assert.Single((JArray)json["entityBehaviors"]!);
  }

  // Fails when a count below one is accepted.
  [Fact]
  public void A_joint_must_spend_at_least_one_fastener() {
    Assert.Throws<System.ArgumentOutOfRangeException>(() =>
      ExBlockDef.Create("test", "c").HandAssembly("t", "f", 0)
    );
  }
}
