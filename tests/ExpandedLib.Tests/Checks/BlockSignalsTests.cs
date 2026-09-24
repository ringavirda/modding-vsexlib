using System;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Each signal <see cref="BlockSignals"/> reads off a registered block, planted on a
/// block that has it beside one that does not.</summary>
public class BlockSignalsTests {
  public BlockSignalsTests() => TestModDomain.Register();

  #region Fixture

  private readonly TestWorld _world = new();

  private BlockSignals Read(Block block) =>
    BlockSignals.Of(block, _world.Api.ClassRegistry);

  private static Block Plain(string code = "exlib:probe") =>
    TestBlocks.Configure(new Block(), code, 1);

  private static BlockEntityBehaviorType Behavior(
    string name,
    string? properties = null
  ) =>
    new() {
      Name = name,
      properties =
        properties != null ? new JsonObject(JToken.Parse(properties)) : null,
    };

  private static JsonObject Attributes(string json) => new(JToken.Parse(json));

  private sealed class SubBlock : Block { }

  private sealed class PlainEntity : BlockEntity { }

  private sealed class ThrowingEntity : BlockEntity {
    public ThrowingEntity() => throw new ArgumentException("no entity here");
  }

  /// <summary>Adds a network membership of type <c>pipe</c> in its constructor.</summary>
  private sealed class MemberEntity : BlockEntity {
    public MemberEntity() => Behaviors.Add(new PipeMember(this));
  }

  private sealed class PipeMember(BlockEntity be) : BEBehaviorNetworkMember(be) {
    public override string NetworkType => "pipe";
  }

  private sealed class ContainerEntity : BlockEntityContainer {
    public override InventoryBase Inventory => null!;
    public override string InventoryClassName => "probe";
  }

  private static JObject LayoutAttributes() =>
    (JObject)
      ExBlockDef
        .Create("exlib", "probe")
        .MultiblockLayout(l =>
          l.Legend('M', "exlib:probe-*")
            .Legend('F', "game:cobblestone-*")
            .Role('F', CellRoles.NoSnow)
            .Layer(0, "MF")
        )
        .ToJson()["attributes"]!;

  #endregion

  #region Block class

  // Fails when BlockClass answers Block for every block.
  [Fact]
  public void A_block_of_its_own_class_carries_the_block_class_signal() {
    Block block = TestBlocks.Configure(new SubBlock(), "exlib:probe", 1);

    Assert.Equal(typeof(SubBlock), Read(block).BlockClass);
    Assert.Contains("block class", Read(block).Names());
    Assert.DoesNotContain("block class", Read(Plain()).Names());
  }

  #endregion

  #region Entity class

  // Fails when EntityType is not resolved through the registry.
  [Fact]
  public void An_entity_class_resolves_to_the_type_the_registry_holds() {
    _world.RegisterClass("exlib:probeentity", typeof(PlainEntity));
    Block block = Plain();
    block.EntityClass = "exlib:probeentity";

    Assert.Equal(typeof(PlainEntity), Read(block).EntityType);
    Assert.Contains("entity class", Read(block).Names());
  }

  // Fails when an entity class the registry lacks is looked up by construction and throws.
  [Fact]
  public void An_entity_class_the_registry_lacks_keeps_its_key_and_no_type() {
    _world.RegisterClass("exlib:probeentity", typeof(PlainEntity));
    Block block = Plain();
    block.EntityClass = "exlib:nosuchentity";

    BlockSignals signals = Read(block);

    Assert.Equal("exlib:nosuchentity", signals.EntityClass);
    Assert.Null(signals.EntityType);
  }

  // Fails when Of lets the constructor's exception out unwrapped.
  [Fact]
  public void An_entity_that_throws_when_built_is_reported_with_the_block() {
    _world.RegisterClass("exlib:throwingentity", typeof(ThrowingEntity));
    Block block = Plain("exlib:thrower");
    block.EntityClass = "exlib:throwingentity";

    var thrown = Assert.Throws<InvalidOperationException>(() => Read(block));

    Assert.Contains("exlib:thrower", thrown.Message);
    Assert.Contains("no entity here", thrown.Message);
  }

  #endregion

  #region Behaviours

  // Fails when the behaviours an entity's constructor adds are not read.
  [Fact]
  public void Declared_and_constructor_added_behaviours_are_both_read() {
    _world.RegisterClass("exlib:memberentity", typeof(MemberEntity));
    Block block = Plain();
    block.EntityClass = "exlib:memberentity";
    block.BlockEntityBehaviors = [Behavior("Animatable")];

    Assert.Equal(["Animatable", "PipeMember"], Read(block).EntityBehaviors);
    Assert.Contains("behaviours", Read(block).Names());
  }

  // Fails when the block's own behaviours are not read.
  [Fact]
  public void The_blocks_own_behaviours_are_read_by_type_name() {
    Block block = Plain();
    block.BlockBehaviors = [new BlockBehaviorHorizontalOrientable(block)];

    Assert.Equal(
      ["BlockBehaviorHorizontalOrientable"],
      Read(block).BlockBehaviors
    );
    Assert.DoesNotContain("behaviours", Read(Plain()).Names());
  }

  #endregion

  #region Placed groups

  // Fails when a network node's orientation is not among the groups its placement writes.
  [Fact]
  public void A_network_nodes_orientation_is_a_placed_group() {
    Block node = TestNetworkBlock.Create("pipe", "ns", 5);

    Assert.Equal(["orientation"], Read(node).PlacedGroups);
    Assert.Contains("placed groups", Read(node).Names());
  }

  // Fails when an orientation behaviour's group is not read.
  [Fact]
  public void An_orientation_behaviours_group_is_a_placed_group() {
    Block block = TestBlocks.Configure(
      new Block(),
      "exlib:probe-north",
      1,
      ("side", "north")
    );
    block.BlockBehaviors = [new BlockBehaviorHorizontalOrientable(block)];

    Assert.Equal(["side"], Read(block).PlacedGroups);
    Assert.Empty(Read(Plain()).PlacedGroups);
  }

  #endregion

  #region Footprint

  private const string TwoCells =
    """{ "fillerOffsets": [ { "x": 1, "y": 0, "z": 0 }, { "x": 0, "y": 1, "z": 0 } ] }""";

  // Fails when a filler host's fillerOffsets are not read.
  [Fact]
  public void A_filler_hosts_offsets_are_its_footprint() {
    Block host = TestBlocks.Configure(
      new BlockFilledMegastructure(),
      "exlib:probe",
      1
    );
    host.Attributes = Attributes(TwoCells);

    Assert.Equal(2, Read(host).Footprint.Count);
    Assert.Contains("footprint", Read(host).Names());
  }

  // Fails when fillerOffsets are read off a block that is no filler host.
  [Fact]
  public void Offsets_on_a_block_that_hosts_no_fillers_are_no_footprint() {
    Block block = Plain();
    block.Attributes = Attributes(TwoCells);

    Assert.Empty(Read(block).Footprint);
  }

  #endregion

  #region Layout and nosnow cells

  // Fails when a layout is read without a multiblock entity.
  [Fact]
  public void A_layout_needs_a_multiblock_entity() {
    _world.RegisterClass("exlib:plainentity", typeof(PlainEntity));
    Block block = Plain();
    block.EntityClass = "exlib:plainentity";
    block.Attributes = new JsonObject(LayoutAttributes());

    Assert.Null(Read(block).Layout);
    Assert.Empty(Read(block).NoSnowCells);
  }

  // Fails when the layout or its nosnow cells are not read off a multiblock entity's block.
  [Fact]
  public void A_multiblocks_layout_and_nosnow_cells_are_read() {
    _world.RegisterClass("exlib:megaentity", typeof(TestMegablock));
    Block block = Plain();
    block.EntityClass = "exlib:megaentity";
    block.Attributes = new JsonObject(LayoutAttributes());

    BlockSignals signals = Read(block);

    Assert.NotNull(signals.Layout);
    Assert.Equal([(1, 0, 0)], signals.NoSnowCells);
    Assert.Contains("layout", signals.Names());
    Assert.Contains("nosnow cells", signals.Names());
  }

  // Fails when a multiblock entity counts as a layout without the attribute.
  [Fact]
  public void A_multiblock_entity_without_the_attribute_has_no_layout() {
    _world.RegisterClass("exlib:megaentity", typeof(TestMegablock));
    Block block = Plain();
    block.EntityClass = "exlib:megaentity";

    Assert.Null(Read(block).Layout);
  }

  #endregion

  #region Stages

  // Fails when the stage table of the construction behaviour is not counted.
  [Fact]
  public void A_constructions_stages_are_counted() {
    Block block = Plain();
    block.BlockEntityBehaviors =
    [
      Behavior("Animatable", """{ "stages": [ {} ] }"""),
      Behavior(
        BlockSignals.ConstructionBehavior,
        """{ "stages": [ {}, {}, {} ] }"""
      ),
    ];

    Assert.Equal(3, Read(block).Stages);
    Assert.Contains("stages", Read(block).Names());
    Assert.Equal(0, Read(Plain()).Stages);
  }

  #endregion

  #region Networks

  // Fails when a network node's own type is not read.
  [Fact]
  public void A_network_node_joins_its_own_type() {
    Assert.Equal(
      ["steam"],
      Read(TestNetworkBlock.Create("steam", "ns", 5)).Networks
    );
  }

  // Fails when a membership with no type of its own does not fall back to the declared one.
  [Fact]
  public void A_declared_membership_joins_the_type_it_declares() {
    _world.RegisterClass("NetworkMember", typeof(BEBehaviorNetworkMember));
    Block block = Plain();
    block.BlockEntityBehaviors =
    [
      Behavior("NetworkMember", """{ "networkType": "molten" }"""),
    ];

    Assert.Equal(["molten"], Read(block).Networks);
    Assert.Contains("network", Read(block).Names());
  }

  // Fails when a membership the entity adds in its constructor is not read.
  [Fact]
  public void A_constructor_added_membership_joins_its_type() {
    _world.RegisterClass("exlib:memberentity", typeof(MemberEntity));
    Block block = Plain();
    block.EntityClass = "exlib:memberentity";

    Assert.Equal(["pipe"], Read(block).Networks);
  }

  // Fails when a hosted cell's membership or a footprint port is not counted as the block's.
  [Fact]
  public void A_hosted_membership_and_a_port_are_the_blocks_networks() {
    _world.RegisterClass("NetworkMember", typeof(BEBehaviorNetworkMember));
    Block host = TestBlocks.Configure(
      new BlockFilledMegastructure(),
      "exlib:probe",
      1
    );
    host.Attributes = Attributes(
      """
      { "fillerOffsets": [
        { "x": 1, "y": 0, "z": 0, "behaviors": [
          { "code": "NetworkMember", "face": "n", "properties": { "networkType": "mpenergy" } } ] },
        { "x": 0, "y": 1, "z": 0, "portFace": "up", "portNetwork": "gas" } ] }
      """
    );

    BlockSignals signals = Read(host);

    Assert.Equal(["mpenergy", "gas"], signals.Networks);
    Assert.True(signals.MpEnergyMember);
    Assert.Contains("mpenergy", signals.Names());
  }

  // Fails when MpEnergyMember answers for a run of another type.
  [Fact]
  public void A_pipe_node_is_no_mpenergy_member() {
    Assert.False(Read(TestNetworkBlock.Create("pipe", "ns", 5)).MpEnergyMember);
  }

  #endregion

  #region Mechanical power

  // Fails when a vanilla mechanical power block is not read as a connector.
  [Fact]
  public void A_mechanical_power_block_is_a_connector() {
    Block axle = TestBlocks.Configure(new BlockAxle(), "game:woodenaxle-ns", 1);

    Assert.True(Read(axle).MpConnector);
    Assert.Contains("mp connector", Read(axle).Names());
  }

  // Fails when the structure filler, which answers only for a hosted behaviour, counts as one.
  [Fact]
  public void The_structure_filler_is_no_connector_of_its_own() {
    Block filler = TestBlocks.Configure(
      new BlockStructureFiller(),
      "exlib:structurefiller",
      1
    );

    Assert.False(Read(filler).MpConnector);
  }

  // Fails when a hosted mechanical power behaviour is not counted.
  [Fact]
  public void A_hosted_mechanical_power_behaviour_is_a_connector() {
    _world.RegisterClass("ProbeAxle", typeof(BEBehaviorMPAxle));
    Block host = TestBlocks.Configure(
      new BlockFilledMegastructure(),
      "exlib:probe",
      1
    );
    host.Attributes = Attributes(
      """
      { "fillerOffsets": [
        { "x": 1, "y": 0, "z": 0, "behaviors": [ { "code": "ProbeAxle" } ] } ] }
      """
    );

    Assert.True(Read(host).MpConnector);
    Assert.False(Read(Plain()).MpConnector);
  }

  #endregion

  #region Inventory

  // Fails when a container entity is not read as holding an inventory.
  [Fact]
  public void A_container_entity_holds_an_inventory() {
    _world.RegisterClass("exlib:containerentity", typeof(ContainerEntity));
    _world.RegisterClass("exlib:plainentity", typeof(PlainEntity));
    Block container = Plain();
    container.EntityClass = "exlib:containerentity";
    Block plain = Plain();
    plain.EntityClass = "exlib:plainentity";

    Assert.True(Read(container).Inventory);
    Assert.Contains("inventory", Read(container).Names());
    Assert.False(Read(plain).Inventory);
  }

  #endregion

  #region Census order

  // Fails when Names yields a name AllNames does not list.
  [Fact]
  public void Every_name_a_block_carries_is_a_census_name() {
    Block host = TestBlocks.Configure(
      new BlockFilledMegastructure(),
      "exlib:probe",
      1
    );
    host.Attributes = Attributes(TwoCells);

    Assert.All(
      Read(host).Names(),
      n => Assert.Contains(n, BlockSignals.AllNames())
    );
    Assert.Equal(
      BlockSignals.AllNames().Count,
      BlockSignals.AllNames().Distinct().Count()
    );
  }

  #endregion
}
