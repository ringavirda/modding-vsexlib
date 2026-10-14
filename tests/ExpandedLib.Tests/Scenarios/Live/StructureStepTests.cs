using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The steps that read or write block entities, megablocks and multiblocks, over a
/// <see cref="TestWorld"/> with the plot at (100, 50, 100).</summary>
public class StructureStepTests {
  private class ProbeEntity : BlockEntity, IBlockEntityContainer {
    public int Count;
    public string Name = "a";
    public int Charge;
    public bool Armed;
    public InventoryGeneric Slots { get; set; } = null!;

    public IInventory Inventory => Slots;

    public string InventoryClassName => "probe";

    public void DropContents(Vec3d atPos) { }

    public void CheckInventoryClearedMidTick() { }

    public int Add(int a, int b) => a + b;

    public override void ToTreeAttributes(ITreeAttribute tree) {
      if (Armed)
        throw new System.InvalidOperationException("tree armed");
      base.ToTreeAttributes(tree);
      tree.SetInt("count", Count);
      tree.SetString("name", Name);
      tree["nested"] = new TreeAttribute();
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    ) {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      Count = tree.GetInt("count");
      Name = tree.GetString("name");
    }
  }

  private sealed class Construction : ProbeEntity {
    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      Behaviors.Add(new ExRightClickConstructable(this));
    }
  }

  private sealed class Scene {
    public LiveFixture Fixture { get; } = new();

    public Block Brick { get; } =
      TestBlocks.Configure(new Block(), "exlib:brick", 20);

    public Block Node { get; } =
      TestNetworkBlock.Create("test", "n", 900, "exlib:probe-node-n");

    public Block Probe { get; } =
      TestBlocks.Configure(
        new Block { EntityClass = "probe" },
        "exlib:probe",
        21
      );

    public Block Construct { get; } =
      TestBlocks.Configure(
        new Block { EntityClass = "construct" },
        "exlib:construct",
        23
      );

    public Block Machine { get; } =
      TestBlocks.Configure(
        new Block { EntityClass = "machine" },
        "exlib:probe-n",
        22
      );

    public Scene() {
      TestWorld world = Fixture.Test;
      world.Filler.EntityClass = "structurefiller";
      world
        .Register(Brick)
        .Register(Node)
        .Register(Probe)
        .Register(Construct)
        .Register(Machine)
        .Register(world.Filler)
        .RegisterNetwork("test", sys => new StubNetwork(sys))
        .RegisterBlockEntityFactory(
          "probe",
          () => {
            var be = new ProbeEntity();
            be.Slots = new InventoryGeneric(2, "probe", null, world.Api);
            return be;
          }
        )
        .RegisterBlockEntityFactory("construct", () => new Construction())
        .RegisterBlockEntityFactory(
          "structurefiller",
          () => new BlockEntityStructureFiller()
        )
        .RegisterBlockEntityFactory(
          "machine",
          () => new TestMegablock { Angle = 0 }
        );
      world.RegisterItem("exlib:ingot");
      Machine.Attributes = new JsonObject(
        (JObject)
          ExBlockDef
            .Create("exlib", "probe")
            .MultiblockLayout(l =>
              l.Legend('M', "exlib:probe-*")
                .Legend('B', "exlib:brick")
                .Legend('Y', "exlib:probe-node-*")
                .Connector('Y', BlockFacing.NORTH)
                .Legend('A', "game:air")
                .Layer(0, "MBB\nYA")
            )
            .ToJson()["attributes"]!
      );
    }

    public IReadOnlyList<SetupStep> Steps(string steps) =>
      LiveFixture.Case(steps).Model.Steps;

    public void Drive(string steps) => Run(Steps(steps));

    public void Run(IEnumerable<SetupStep> steps) {
      foreach (bool _ in ScenarioSteps.Run(Fixture.Context, [.. steps], "")) {
        Fixture.Test.AdvanceBlockEntityTime(3000);
        Fixture.Clock += 3;
      }
    }

    public Block Block(int x, int y, int z) =>
      Fixture.Test.GetBlock(new BlockPos(100 + x, 50 + y, 100 + z));
  }

  private static BlockFilledMegastructure Mega(Block filler) =>
    TestBlocks.Configure(
      new BlockFilledMegastructure {
        Attributes = new JsonObject(
          JObject.Parse(
            """{ "fillerOffsets": [{ "x": 1, "y": 0, "z": 0, "portFace": "n", "portNetwork": "test" }] }"""
          )
        ),
      },
      "exlib:mega-west",
      31,
      ("side", "west")
    );

  private static SetupPlacement Frame(Scene scene, string id, BlockPos local) {
    SetupFrame frame = scene.Fixture.Context.Setup.FrameAt(local)!;
    var placement = new SetupPlacement(local, [local], frame);
    scene.Fixture.Context.Setup.Place(id, placement);
    return placement;
  }

  private static BlockPos Resolve(Scene scene, string position) =>
    SetupPosition
      .Parse(
        JToken.Parse(position),
        new SetupJson.Where("case.json", "position")
      )
      .Resolve(scene.Fixture.Context.Setup);

  #region Frames

  /// <summary>Fails when a plain block gets a frame (V56).</summary>
  [Fact]
  public void A_plain_block_has_no_frame() {
    var scene = new Scene();
    scene.Fixture.Test.Place(new BlockPos(103, 53, 104), scene.Fixture.Stone);
    Assert.Null(scene.Fixture.Context.Setup.FrameAt(new BlockPos(3, 3, 4)));
  }

  /// <summary>Fails when a megablock's footprint is left out of its frame (V57), turned by the wrong
  /// angle (V58), or kept absolute (V62).</summary>
  [Fact]
  public void A_megablocks_frame_turns_its_footprint_and_port_with_the_side() {
    var scene = new Scene();
    var mega = Mega(scene.Machine);
    scene.Fixture.Test.Register(mega);
    var local = new BlockPos(2, 3, 4);
    scene.Fixture.Test.Place(new BlockPos(102, 53, 104), mega);

    SetupFrame frame = scene.Fixture.Context.Setup.FrameAt(local)!;
    Assert.Equal(local, frame.Principal);
    Assert.Equal(90, frame.Angle);
    Assert.Null(frame.Layout);
    Assert.Equal([new BlockPos(2, 3, 3)], frame.Footprint.Select(c => c.Pos));

    Frame(scene, "m", local);
    Assert.Equal(
      new BlockPos(2, 3, 3),
      Resolve(scene, """{ "of": "m", "cell": [1, 0, 0] }""")
    );
    Assert.Equal(
      new BlockPos(1, 3, 3),
      Resolve(scene, """{ "of": "m", "port": [1, 0, 0] }""")
    );
  }

  /// <summary>Fails when a multiblock's frame carries no angle (V59), its layout cell is left
  /// absolute (V60), or its layout is dropped (V61).</summary>
  [Fact]
  public void A_multiblocks_frame_resolves_a_layout_offset_at_its_facing_in_plot_cells() {
    var scene = new Scene();
    var machine = new TestMegablock { Angle = 90 };
    scene.Fixture.Test.Place(
      new BlockPos(100, 60, 100),
      scene.Machine,
      machine
    );
    scene.Fixture.Test.Attach(machine);

    SetupFrame frame = scene.Fixture.Context.Setup.FrameAt(
      new BlockPos(0, 10, 0)
    )!;
    Assert.Equal(90, frame.Angle);
    Assert.Empty(frame.Footprint);
    Frame(scene, "m", new BlockPos(0, 10, 0));
    Assert.Equal(
      new BlockPos(0, 10, -1),
      Resolve(scene, """{ "of": "m", "layout": [1, 0, 0] }""")
    );
  }

  #endregion

  #region Place

  /// <summary>Fails when a megablock's place records only its own cell (V63).</summary>
  [Fact]
  public void A_placed_megablock_records_its_footprint_cells() {
    var scene = new Scene();
    var mega = Mega(scene.Machine);
    scene.Fixture.Test.Register(mega);
    scene.Fixture.Run(
      scene.Steps(
        """[{ "place": { "id": "m", "code": "exlib:mega-west", "at": [2, 3, 4] } }]"""
      )
    );
    SetupPlacement placed = scene.Fixture.Context.Setup.Placed("m")!;
    Assert.Equal([new BlockPos(2, 3, 4), new BlockPos(2, 3, 3)], placed.Cells);
    Assert.NotNull(placed.Frame);
    Assert.Contains(new BlockPos(2, 3, 3), scene.Fixture.Context.Placed);
  }

  /// <summary>Fails when a place with a tree leaves the entity unwritten (V64).</summary>
  [Fact]
  public void A_place_with_a_tree_writes_it_into_the_new_entity() {
    var scene = new Scene();
    scene.Fixture.Run(
      scene.Steps(
        """[{ "place": { "id": "p", "code": "exlib:probe", "at": [0, 0, 0], "tree": { "count": 4 } } }]"""
      )
    );
    Assert.Equal(
      4,
      (
        (ProbeEntity)
          scene.Fixture.Context.Setup.BlockEntityAt(new BlockPos(0, 0, 0))!
      ).Count
    );
  }

  // Fails when a drawn entry's complete: all is dropped, so the layout walk leaves the structure unbuilt.
  [Fact]
  public void The_layout_walk_completes_a_drawn_structure_and_waits_for_the_monitor() {
    var scene = new Scene();
    SetupLayout layout = LiveFixture
      .Case(
        "[]",
        """
        { layout: { legend: {
            "Y": { "id": "n", "code": "exlib:probe-node-n" },
            "M": { "id": "m", "code": "exlib:probe-n", "complete": "all" },
            "B": { "cells": "m" } },
          layers: { "0": ["MBB", "Y.."] } } }
        """
      )
      .Model.Drawn!;
    foreach (
      bool _ in SetupLayoutWalk.Run(scene.Fixture.Context, layout, null)
    ) {
      scene.Fixture.Test.AdvanceBlockEntityTime(3000);
      scene.Fixture.Clock += 3;
    }
    Assert.Equal(scene.Brick.Id, scene.Block(1, 0, 0).Id);
    Assert.Equal(scene.Brick.Id, scene.Block(2, 0, 0).Id);
    Assert.True(
      (
        (TestMegablock)
          scene.Fixture.Context.Setup.BlockEntityAt(new BlockPos(0, 0, 0))!
      ).StructureComplete
    );
  }

  /// <summary>Fails when the place step does not hand a multiblock to complete (V65).</summary>
  [Fact]
  public void A_place_with_complete_raises_the_missing_cells_and_waits_for_the_monitor() {
    var scene = new Scene();
    scene.Drive(
      """
      [
        { "place": { "id": "n", "code": "exlib:probe-node-n", "at": [0, 10, 1] } },
        { "place": { "id": "m", "code": "exlib:probe-n", "at": [0, 10, 0], "complete": "all" } }
      ]
      """
    );
    Assert.Equal(scene.Brick.Id, scene.Block(1, 10, 0).Id);
    Assert.Equal(scene.Brick.Id, scene.Block(2, 10, 0).Id);
    Assert.True(
      (
        (TestMegablock)
          scene.Fixture.Context.Setup.BlockEntityAt(new BlockPos(0, 10, 0))!
      ).StructureComplete
    );
    Assert.Contains(
      scene.Fixture.Lines,
      l => l.EndsWith("ok: raised exlib:brick x2")
    );
  }

  #endregion

  #region Complete

  /// <summary>Fails when the air cell (V66), the cell the case placed (V67) or the connector cell
  /// (V68) is raised.</summary>
  [Fact]
  public void Complete_leaves_air_placed_and_connector_cells_alone_and_fails_naming_them() {
    var scene = new Scene();
    IReadOnlyList<SetupStep> steps = scene.Steps(
      """
      [
        { "place": { "id": "m", "code": "exlib:probe-n", "at": [0, 10, 0] } },
        { "complete": { "of": "m" } }
      ]
      """
    );
    scene.Run(steps.Take(1));
    scene.Fixture.Test.Place(new BlockPos(101, 60, 101), scene.Fixture.Stone);
    scene.Fixture.Context.Placed.Add(new BlockPos(2, 10, 0));

    StepFailure failure = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(1))
    );

    Assert.Contains("3 cell(s) missing", failure.Message);
    Assert.Equal(scene.Brick.Id, scene.Block(1, 10, 0).Id);
    Assert.Equal(0, scene.Block(2, 10, 0).Id);
    Assert.Equal(0, scene.Block(0, 10, 1).Id);
    Assert.Equal(scene.Fixture.Stone.Id, scene.Block(1, 10, 1).Id);
  }

  /// <summary>Fails when a whole structure reports a raise (V70), or the step ends before the
  /// monitor has seen the structure whole (V69).</summary>
  [Fact]
  public void Complete_of_a_whole_structure_raises_nothing_and_waits_for_the_monitor() {
    var scene = new Scene();
    scene.Drive(
      """
      [
        { "place": { "id": "n", "code": "exlib:probe-node-n", "at": [0, 10, 1] } },
        { "place": { "id": "b", "code": "exlib:brick", "at": [1, 10, 0] } },
        { "place": { "id": "c", "code": "exlib:brick", "at": [2, 10, 0] } },
        { "place": { "id": "m", "code": "exlib:probe-n", "at": [0, 10, 0] } },
        { "complete": { "of": "m" } }
      ]
      """
    );
    Assert.Contains(
      scene.Fixture.Lines,
      l => l.Contains(" complete ") && l.EndsWith("ok: nothing to raise")
    );
    Assert.True(
      (
        (TestMegablock)
          scene.Fixture.Context.Setup.BlockEntityAt(new BlockPos(0, 10, 0))!
      ).StructureComplete
    );
  }

  /// <summary>Fails when a block with no layout reports ok (V72), or a construction is told it has
  /// nothing to complete (V71).</summary>
  [Fact]
  public void Complete_of_a_block_that_is_neither_a_multiblock_nor_a_construction_fails() {
    var scene = new Scene();
    IReadOnlyList<SetupStep> steps = scene.Steps(
      """
      [
        { "place": { "id": "s", "code": "game:stone", "at": [5, 5, 5] } },
        { "place": { "id": "p", "code": "exlib:construct", "at": [6, 5, 5] } },
        { "complete": { "of": "s" } },
        { "complete": { "of": "p" } }
      ]
      """
    );
    scene.Run(steps.Take(2));
    StepFailure plain = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(2).Take(1))
    );
    Assert.Equal(Suspect.Scenario, plain.Suspect);
    Assert.Contains("nothing to complete", plain.Message);
    StepFailure construction = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(3))
    );
    Assert.Equal(Suspect.Scenario, construction.Suspect);
    Assert.Contains("scenario player", construction.Message);
  }

  #endregion

  #region Entities

  private static ProbeEntity Probe(Scene scene) =>
    (ProbeEntity)
      scene.Fixture.Context.Setup.BlockEntityAt(new BlockPos(0, 0, 0))!;

  private const string PlaceProbe =
    """{ "place": { "id": "p", "code": "exlib:probe", "at": [0, 0, 0] } }""";

  /// <summary>Fails when set writes no value into the tree (V73) or loads it back without the entity
  /// (V74).</summary>
  [Fact]
  public void Set_tree_writes_each_key_with_the_type_it_has_and_loads_it_back() {
    var scene = new Scene();
    scene.Drive(
      $$"""[{{PlaceProbe}}, { "set": { "at": { "of": "p" }, "tree": { "count": 7, "name": "b" } } }]"""
    );
    Assert.Equal(7, Probe(scene).Count);
    Assert.Equal("b", Probe(scene).Name);
    Assert.Contains(
      scene.Fixture.Lines,
      l => l.EndsWith("ok: count=7, name=b")
    );
  }

  /// <summary>Fails when a key the tree lacks is written anyway (V75), or a nested tree is written
  /// as a scalar (V76).</summary>
  [Fact]
  public void Set_tree_refuses_a_key_the_entity_does_not_have_and_a_type_it_cannot_write() {
    var scene = new Scene();
    IReadOnlyList<SetupStep> steps = scene.Steps(
      $$"""
      [
        {{PlaceProbe}},
        { "set": { "at": { "of": "p" }, "tree": { "gone": 1 } } },
        { "set": { "at": { "of": "p" }, "tree": { "nested": 1 } } }
      ]
      """
    );
    scene.Run(steps.Take(1));
    StepFailure missing = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(1).Take(1))
    );
    Assert.Contains("no tree key \"gone\"", missing.Message);
    StepFailure nested = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(2))
    );
    Assert.Contains("which set cannot write", nested.Message);
  }

  /// <summary>Fails when set member writes nothing (V77).</summary>
  [Fact]
  public void Set_member_writes_the_field_of_the_entity() {
    var scene = new Scene();
    scene.Drive(
      $$"""[{{PlaceProbe}}, { "set": { "at": { "of": "p" }, "member": { "Charge": 9 } } }]"""
    );
    Assert.Equal(9, Probe(scene).Charge);
    Assert.Contains(scene.Fixture.Lines, l => l.EndsWith("ok: Charge=9"));
  }

  /// <summary>Fails when call passes its arguments in the wrong order (V78), a wrong return passes
  /// (V79), or a method of another argument count is called (V80).</summary>
  [Fact]
  public void Call_invokes_the_method_and_checks_what_it_returns() {
    var scene = new Scene();
    IReadOnlyList<SetupStep> steps = scene.Steps(
      $$"""
      [
        {{PlaceProbe}},
        { "call": { "at": { "of": "p" }, "method": "Add", "args": [2, 3], "returns": 5 } },
        { "call": { "at": { "of": "p" }, "method": "Add", "args": [2, 3], "returns": 6 } },
        { "call": { "at": { "of": "p" }, "method": "Add", "args": [2] } }
      ]
      """
    );
    scene.Run(steps.Take(2));
    Assert.Contains(
      scene.Fixture.Lines,
      l => l.Contains(" call Add ") && l.EndsWith("ok: 5")
    );
    StepFailure wrong = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(2).Take(1))
    );
    Assert.Contains("returned 5, expected 6", wrong.Message);
    StepFailure missing = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(3))
    );
    Assert.Equal(Suspect.Scenario, missing.Suspect);
    Assert.Contains(
      "no public method Add taking 1 argument(s)",
      missing.Message
    );
  }

  /// <summary>Fails when insert takes the wrong count (V81) or the wrong slot (V82).</summary>
  [Fact]
  public void Insert_puts_the_stack_in_the_inventory_through_the_slot_rules() {
    var scene = new Scene();
    scene.Drive(
      $$"""[{{PlaceProbe}}, { "insert": { "at": { "of": "p" }, "stack": "exlib:ingot", "count": 3 } }]"""
    );
    ItemSlot slot = Probe(scene).Slots[0];
    Assert.Equal("exlib:ingot", slot.Itemstack.Collectible.Code.ToString());
    Assert.Equal(3, slot.StackSize);
    Assert.Contains(
      scene.Fixture.Lines,
      l => l.EndsWith("ok: 3 x exlib:ingot")
    );
  }

  #endregion

  #region Expect

  private const string PlaceMega =
    """{ "place": { "id": "m", "code": "exlib:mega-west", "at": [2, 3, 4] } }""";

  private static Scene Megas() {
    var scene = new Scene();
    scene.Fixture.Test.Register(Mega(scene.Machine));
    return scene;
  }

  /// <summary>Fails when a footprint counts the wrong cells (V85), or reads a filler of another
  /// principal as filled (V83).</summary>
  [Fact]
  public void Expect_footprint_filled_holds_for_fillers_of_this_principal_only() {
    Scene scene = Megas();
    IReadOnlyList<SetupStep> steps = scene.Steps(
      $$"""
      [
        {{PlaceMega}},
        { "expect": { "footprint": "filled", "of": "m" } },
        { "expect": { "footprint": "filled", "of": "m" } }
      ]
      """
    );
    scene.Run(steps.Take(2));
    Assert.Contains(
      scene.Fixture.Lines,
      l => l.Contains(" expect ") && l.EndsWith("ok: 2 cell(s) filled")
    );

    scene.Fixture.Test.PlaceFiller(
      new BlockPos(102, 53, 103),
      principal: new BlockPos(150, 53, 104)
    );
    StepFailure foreign = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(2))
    );
    Assert.Contains("footprint cell [2, 3, 3]", foreign.Message);
  }

  /// <summary>Fails when a standing principal reads as air (V84) or a footprint of another word is
  /// refused without naming the two (V86).</summary>
  [Fact]
  public void Expect_footprint_air_fails_on_a_standing_principal_and_refuses_other_words() {
    Scene scene = Megas();
    IReadOnlyList<SetupStep> steps = scene.Steps(
      $$"""
      [
        {{PlaceMega}},
        { "expect": { "footprint": "air", "of": "m" } },
        { "expect": { "footprint": "half", "of": "m" } }
      ]
      """
    );
    scene.Run(steps.Take(1));
    StepFailure standing = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(1).Take(1))
    );
    Assert.Contains(
      "footprint cell [2, 3, 4] holds exlib:mega-west, expected air",
      standing.Message
    );
    StepFailure word = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(2))
    );
    Assert.Contains("\"filled\" or \"air\"", word.Message);
  }

  /// <summary>Fails when two cells of one network read as apart (V88), or two networks as one (V87).</summary>
  [Fact]
  public void Expect_same_holds_for_cells_of_one_network_and_fails_across_two() {
    var scene = new Scene();
    TestWorld world = scene.Fixture.Test;
    world.PlaceNode(new BlockPos(100, 50, 100), "test", "ns");
    world.PlaceNode(new BlockPos(100, 50, 101), "test", "ns");
    world.PlaceNode(new BlockPos(100, 50, 105), "test", "ns");
    IReadOnlyList<SetupStep> steps = scene.Steps(
      """
      [
        { "expect": { "at": [0, 0, 0], "same": [0, 0, 1], "network": "test" } },
        { "expect": { "at": [0, 0, 0], "same": [0, 0, 5], "network": "test" } }
      ]
      """
    );
    scene.Run(steps.Take(1));
    Assert.Contains(
      scene.Fixture.Lines,
      l => l.EndsWith("ok: one test network")
    );
    StepFailure apart = Assert.Throws<StepFailure>(() =>
      scene.Run(steps.Skip(1))
    );
    Assert.Contains("are not in one test network", apart.Message);
  }

  #endregion

  /// <summary>Fails when a throw inside the failure detail's tree section escapes instead of
  /// printing a detail failed line (V91).</summary>
  [Fact]
  public void A_block_entity_that_throws_in_the_detail_still_fails_the_step_with_its_lines() {
    var scene = new Scene();
    var failure = Assert.Throws<StepFailure>(() =>
      scene.Drive(
        $$"""
        [
          {{PlaceProbe}},
          { "set": { "at": { "of": "p" }, "member": { "Armed": true } } },
          { "expect": { "at": { "of": "p" }, "tree": "count", "equals": 0 } }
        ]
        """
      )
    );
    Assert.Contains("tree armed", failure.Message);
    Assert.Contains(
      scene.Fixture.Lines,
      l => l.Contains("| detail failed: ") && l.Contains("tree armed")
    );
    Assert.Contains(scene.Fixture.Lines, l => l.Contains("| suspect mod"));
  }
}
