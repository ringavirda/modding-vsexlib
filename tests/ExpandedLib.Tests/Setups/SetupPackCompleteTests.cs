#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Setups;

internal sealed class SetupFrame : BlockEntity
{
  private ExpandedLib.Blocks.ExRightClickConstructable? _rcc;

  /// <summary>Whether the frame was marked dirty with its construction already complete.</summary>
  public bool DirtiedBuilt { get; private set; }

  public override void MarkDirty(
    bool redrawOnClient = false,
    IPlayer? skipPlayer = null
  )
  {
    base.MarkDirty(redrawOnClient, skipPlayer);
    DirtiedBuilt |=
      GetBehavior<ExpandedLib.Blocks.ExRightClickConstructable>()?.IsComplete
      == true;
  }

  public void Stock(IPlayer who, int count) =>
    who.InventoryManager.GetHotbarInventory()[0].Itemstack = new ItemStack(
      Api.World.GetItem(new AssetLocation("game:stick")),
      count
    );
}

/// <summary>What a <c>complete</c> draws from a setup's pack: the cells it raises and every stage
/// the construction still needs (setups.md, The pack).</summary>
[Collection("Setups")]
public class SetupPackCompleteTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private const string Frame =
    "{\"place\": {\"id\": \"frame\", \"code\": \"setupbuild:frame\", \"at\": [0, 0, 0], \"complete\": \"all\"}}";
  private const string Stack =
    "{\"place\": {\"id\": \"stack\", \"code\": \"smokestack:smokestack-intake-tier3-n\", \"at\": [1, 1, 0], \"complete\": \"all\"}}";

  private static readonly (string, string)[] Mods =
  [
    ("exlib", "0.8.4"),
    ("setupbuild", "1.0.0"),
    ("smokestack", "1.0.0"),
  ];

  private static string ModPath(string id) =>
    id == "smokestack"
      ? Path.Combine(RepoPaths.Root, "samples", "SmokeStack")
      : Path.Combine(
        RepoPaths.Root,
        "tests",
        "ExpandedLib.Tests",
        "Setups",
        "Fixtures",
        "SetupBuild"
      );

  private static SetupScene Build(
    string mod,
    string pack,
    string steps,
    System.Func<string, SetupFileModel?>? stages = null
  ) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\""
          + mod
          + "\"], \"steps\": ["
          + steps
          + "], \"seconds\": 10, \"record\": {}, \"pack\": "
          + pack
          + "}",
        "t.json"
      ),
      [ModPath(mod)],
      w =>
      {
        w.RegisterItem("game:stick");
        w.RegisterItem("game:flint");
      },
      w =>
        w.RegisterClass(
            "ExRightClickConstructable",
            typeof(ExpandedLib.Blocks.ExRightClickConstructable)
          )
          .RegisterClass("SetupFrame", typeof(SetupFrame))
          .RegisterClass(
            "BlockEntityInteract",
            typeof(Vintagestory.GameContent.BlockBehaviorBlockEntityInteract)
          ),
      stages
    );

  private static SetupScene BuildWithBox(string pack, string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"smokestack\", \"setupbox\"], \"steps\": ["
          + steps
          + "], \"seconds\": 10, \"record\": {}, \"pack\": "
          + pack
          + "}",
        "t.json"
      ),
      [
        ModPath("smokestack"),
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Fixtures",
          "SetupBox"
        ),
      ],
      null,
      w =>
        w.RegisterClass(
            "ExRightClickConstructable",
            typeof(ExpandedLib.Blocks.ExRightClickConstructable)
          )
          .RegisterClass("SetupBox", typeof(BlockEntity))
          .RegisterBlockEntityFactory("SetupBox", () => new SetupBox())
    );

  private static string Refused(
    string mod,
    string pack,
    string steps,
    System.Func<string, SetupFileModel?>? stages = null
  ) =>
    Assert
      .Throws<InvalidOperationException>(() => Build(mod, pack, steps, stages))
      .Message;

  private static string Gathered(string scope, string gathered) =>
    "{\"scope\": \"" + scope + "\", \"gathered\": " + gathered + "}";

  private const string Paid =
    "{\"setupbuild:frame\": 1, \"game:stick\": 2, \"game:plank-oak\": 3}";

  // Fails when complete takes no stage requirement from the pack, so a construction short of its
  // planks completes.
  [Fact]
  public void A_construction_short_of_a_stage_requirement_fails_at_complete()
  {
    string message = Refused(
      "setupbuild",
      Gathered(
        "all",
        "{\"setupbuild:frame\": 1, \"game:stick\": 2, \"game:plank-oak\": 2}"
      ),
      Frame
    );

    Assert.Equal(
      "t.json: steps[0]: the pack holds 2 of game:plank-*; the step takes 3.",
      message
    );
  }

  private static readonly SetupFileModel FrameStage = SetupFileModel.Parse(
    "{\"setup\": \"stage\", \"description\": \"d\", \"mods\": [], \"steps\": [], \"seconds\": 10, \"record\": {}, \"pack\": {\"scope\": \"all\", \"ends\": {\"stacks\": {}, \"standing\": [\"setupbuild:frame\"]}}}",
    "stage.json"
  );

  private static SetupFileModel? FrameStages(string id) =>
    id == "exlib/setups/stage" ? FrameStage : null;

  private const string Carried =
    "{\"scope\": \"all\", \"after\": {\"exlib/setups/stage\": 1}}";

  // Fails when DrawStages ignores the recorded principal, so the carried frame's sticks and planks
  // are drawn from an empty pack.
  [Fact]
  public void A_carried_principal_raises_its_stages_free()
  {
    using SetupScene scene = Build("setupbuild", Carried, Frame, FrameStages);

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Empty(pack.Draws);
  }

  // Fails when complete plants the finished construction without marking the entity dirty, so a
  // machine that joins its run on a fitted stage never re-walks the cells placed around it.
  [Fact]
  public void A_completed_construction_marks_its_entity_dirty_once_built()
  {
    using SetupScene scene = Build("setupbuild", Carried, Frame, FrameStages);

    var frame = (SetupFrame)
      scene.World.GetBlockEntity(scene.PositionOf("frame"))!;

    Assert.True(frame.DirtiedBuilt);
  }

  // Fails when the free branch is taken for any code, so a frame nobody carried completes from an
  // empty pack.
  [Fact]
  public void An_uncarried_principal_still_pays()
  {
    string message = Refused(
      "setupbuild",
      Gathered("all", "{\"setupbuild:frame\": 1}"),
      Frame
    );

    Assert.Equal(
      "t.json: steps[0]: the pack holds 0 of game:stick; the step takes 2.",
      message
    );
  }

  private const string TwoFrames =
    "{\"place\": {\"id\": \"carried\", \"code\": \"setupbuild:frame\", \"at\": [0, 0, 0], \"complete\": \"all\"}},"
    + "{\"place\": {\"id\": \"built\", \"code\": \"setupbuild:frame\", \"at\": [2, 0, 0], \"complete\": \"all\"}}";

  // Fails when the record is keyed on the code, so a second frame of the carried one's code
  // completes free too.
  [Fact]
  public void A_built_principal_beside_a_carried_one_still_pays()
  {
    string message = Refused(
      "setupbuild",
      "{\"scope\": \"all\", \"gathered\": {\"setupbuild:frame\": 1}, \"after\": {\"exlib/setups/stage\": 1}}",
      TwoFrames,
      FrameStages
    );

    Assert.Equal(
      "t.json: steps[1]: the pack holds 0 of game:stick; the step takes 2.",
      message
    );
  }

  private const string ToolFrame =
    "{\"place\": {\"id\": \"frame\", \"code\": \"setupbuild:toolframe\", \"at\": [0, 0, 0], \"complete\": \"all\"}}";

  // Fails when complete passes a tool stage whose tool the pack does not hold.
  [Fact]
  public void A_tool_stage_without_its_tool_in_the_pack_fails_at_complete()
  {
    string message = Refused(
      "setupbuild",
      Gathered("all", "{\"setupbuild:toolframe\": 1, \"game:plank-oak\": 1}"),
      ToolFrame
    );

    Assert.Equal(
      "t.json: steps[0]: the pack holds no game:stick for stage 1 of (0, 0, 0).",
      message
    );
  }

  // Fails when the tool stage draws the tool from the pack as it does a material, so the tool is
  // spent by a complete.
  [Fact]
  public void A_tool_stage_with_its_tool_in_the_pack_completes_and_keeps_the_tool()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered(
        "all",
        "{\"setupbuild:toolframe\": 1, \"game:plank-oak\": 1, \"game:stick\": 1}"
      ),
      ToolFrame
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(
      [
        ("steps[0]", "setupbuild:toolframe", 1),
        ("steps[0]", "game:plank-*", 1),
      ],
      pack.Draws
    );
    Assert.Equal(
      new System.Collections.Generic.Dictionary<string, int>
      {
        ["game:stick"] = 1,
      },
      pack.End
    );
  }

  // Fails when complete checks only the first tool of a two-step stage against the pack.
  [Fact]
  public void A_two_step_stage_without_its_second_tool_in_the_pack_fails_at_complete()
  {
    string message = Refused(
      "setupbuild",
      Gathered(
        "all",
        "{\"setupbuild:toolframe2\": 1, \"game:plank-oak\": 1, \"game:stick\": 1}"
      ),
      ToolFrame.Replace("toolframe", "toolframe2")
    );

    Assert.Equal(
      "t.json: steps[0]: the pack holds no game:flint for stage 1 of (0, 0, 0).",
      message
    );
  }

  // Fails when a step's tool is drawn from the pack as a material is, so a complete spends it.
  [Fact]
  public void A_two_step_stage_completes_with_both_tools_in_the_pack()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered(
        "all",
        "{\"setupbuild:toolframe2\": 1, \"game:plank-oak\": 1, \"game:stick\": 1, \"game:flint\": 1}"
      ),
      ToolFrame.Replace("toolframe", "toolframe2")
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(
      new System.Collections.Generic.Dictionary<string, int>
      {
        ["game:stick"] = 1,
        ["game:flint"] = 1,
      },
      pack.End
    );
  }

  // Fails when a stage's requirement is drawn once however many the stage needs.
  [Fact]
  public void A_paid_construction_draws_every_stage_requirement_in_stage_order()
  {
    using SetupScene scene = Build("setupbuild", Gathered("all", Paid), Frame);

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(
      [
        ("steps[0]", "setupbuild:frame", 1),
        ("steps[0]", "game:stick", 2),
        ("steps[0]", "game:plank-*", 3),
      ],
      pack.Draws
    );
    Assert.Empty(pack.End);
  }

  // Fails when complete draws a stage the hand already paid, so the sticks of stage 1 are drawn
  // again at the complete and the pack runs short.
  [Fact]
  public void A_stage_paid_before_complete_is_not_drawn_again()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("all", Paid),
      "{\"place\": {\"id\": \"frame\", \"code\": \"setupbuild:frame\", \"at\": [0, 0, 0]}}"
        + ", {\"call\": {\"at\": {\"of\": \"frame\"}, \"method\": \"Stock\", \"args\": [\"player\", 2]}}"
        + ", {\"interact\": {\"at\": {\"of\": \"frame\"}, \"with\": {\"stack\": \"game:stick\", \"count\": 2}}}"
        + ", {\"complete\": {\"of\": \"frame\"}}"
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(
      [
        ("steps[0]", "setupbuild:frame", 1),
        ("steps[2]", "game:stick", 2),
        ("steps[3]", "game:plank-*", 3),
      ],
      pack.Draws
    );
  }

  // Fails when a construction's planks are drawn across two woods, so a stage needing three planks
  // completes with two oak and one pine.
  [Fact]
  public void A_construction_whose_planks_are_split_across_woods_fails_at_complete()
  {
    string message = Refused(
      "setupbuild",
      Gathered(
        "all",
        "{\"setupbuild:frame\": 1, \"game:stick\": 2, \"game:plank-oak\": 2, \"game:plank-pine\": 1}"
      ),
      Frame
    );

    Assert.Equal(
      "t.json: steps[0]: the pack holds 3 of game:plank-* under no one key; the step takes 3.",
      message
    );
  }

  // Fails when complete draws under scope "stacks", where a construction is built free.
  [Fact]
  public void A_construction_under_scope_stacks_draws_nothing()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("stacks", "{}"),
      Frame
    );

    Assert.Empty(scene.Run(Mods).Pack!.Draws);
  }

  private const string PlacedFrame =
    "{\"place\": {\"id\": \"frame\", \"code\": \"setupbuild:frame\", \"at\": [0, 0, 0]}}";

  private static string CompleteTo(string to) =>
    PlacedFrame + ", {\"complete\": {\"of\": \"frame\", \"to\": " + to + "}}";

  private static int StageOf(SetupScene scene) =>
    scene
      .World.GetBlockEntity(scene.PositionOf("frame"))!
      .GetBehavior<ExpandedLib.Blocks.ExRightClickConstructable>()!
      .Construction.CurrentCompletedStage;

  // Fails when the walk to stage 1 draws a stage past it (DrawStages loops to the last stage, so
  // the planks are drawn from a pack that holds none), or leaves the construction at its last stage.
  [Fact]
  public void A_complete_to_a_stage_draws_the_stages_up_to_it_and_stops_there()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("all", "{\"setupbuild:frame\": 1, \"game:stick\": 2}"),
      CompleteTo("1")
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(
      [("steps[0]", "setupbuild:frame", 1), ("steps[1]", "game:stick", 2)],
      pack.Draws
    );
    Assert.Empty(pack.End);
    Assert.Equal(1, StageOf(scene));
  }

  // Fails when the completed stage is set to the construction's last instead of the stage named, or
  // the entity is planted with a finished construction though a stage is unpaid.
  [Fact]
  public void A_complete_to_stage_one_leaves_the_last_stage_unpaid()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("stacks", "{}"),
      CompleteTo("1")
    );

    scene.Run(Mods);

    Assert.Equal(1, StageOf(scene));
    Assert.Null(
      ReflectionHelpers.GetField(
        scene.World.GetBlockEntity(scene.PositionOf("frame"))!,
        "_rcc"
      )
    );
  }

  // Fails when a complete to a stage draws under scope "stacks", where a construction is built free.
  [Fact]
  public void A_complete_to_a_stage_under_scope_stacks_draws_nothing()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("stacks", "{}"),
      CompleteTo("1")
    );

    Assert.Empty(scene.Run(Mods).Pack!.Draws);
  }

  // Fails when "all" is read as a stage short of the last, or an absent "to" stops early.
  [Theory]
  [InlineData("\"to\": \"all\"")]
  [InlineData("\"to\": 2")]
  [InlineData("\"of\": \"frame\"")]
  public void A_complete_to_all_or_without_to_completes_every_stage(string key)
  {
    string step = key.StartsWith("\"of\"")
      ? "{\"complete\": {\"of\": \"frame\"}}"
      : "{\"complete\": {\"of\": \"frame\", " + key + "}}";
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("all", Paid),
      PlacedFrame + ", " + step
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(3, pack.Draws.Count);
    Assert.Empty(pack.End);
    Assert.Equal(2, StageOf(scene));
  }

  // Fails when a stage past the last is accepted, or the load names no line.
  [Fact]
  public void A_complete_to_a_stage_past_the_last_fails_the_load_naming_the_line()
  {
    string message = Refused(
      "setupbuild",
      Gathered("stacks", "{}"),
      CompleteTo("3")
    );

    Assert.Equal(
      "t.json: steps[1]: \"to\" 3 is not a stage of a construction of 3 stage(s), 0 to 2.",
      message
    );
  }

  // Fails when a complete to a stage of a block with no construction is read as a complete of its
  // structure, which raises the stack.
  [Fact]
  public void A_complete_to_a_stage_of_a_block_with_no_construction_fails_the_load()
  {
    string message = Refused(
      "smokestack",
      Gathered("stacks", "{}"),
      Stack.Replace(", \"complete\": \"all\"", "")
        + ", {\"complete\": {\"of\": \"stack\", \"to\": 1}}"
    );

    Assert.StartsWith(
      "t.json: steps[1]: \"to\" 1 needs a construction",
      message
    );
  }

  // Fails when materials is ignored by a complete to a stage, so the planks a placeholder names
  // are drawn as any wood.
  [Fact]
  public void A_complete_to_a_stage_draws_the_variant_materials_names()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("all", "{\"setupbuild:keyframe\": 1, \"game:plank-pine\": 3}"),
      PlacedKey
        + ", {\"complete\": {\"of\": \"key\", \"to\": 2, \"materials\": {\"wood\": \"pine\"}}}"
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Contains(("steps[1]", "game:plank-pine", 2), pack.Draws);
  }

  // Fails when a placeholder in a code is drawn as written, so a pack never holds it.
  [Fact]
  public void A_complete_without_materials_draws_a_placeholder_as_any_variant()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("all", "{\"setupbuild:keyframe\": 1, \"game:plank-oak\": 3}"),
      PlacedKey + ", {\"complete\": {\"of\": \"key\"}}"
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Contains(("steps[1]", "game:plank-*", 2), pack.Draws);
  }

  private static ExpandedLib.Blocks.ExRightClickConstruction KeyFrameRcc(
    SetupScene scene
  ) =>
    (ExpandedLib.Blocks.ExRightClickConstruction)
      ExpandedLib.Testing.ReflectionHelpers.GetField(
        scene
          .World.GetBlockEntity(scene.PositionOf("key"))!
          .GetBehavior<ExpandedLib.Blocks.ExRightClickConstructable>()!,
        "rcc"
      )!;

  private const string PlacedKey =
    "{\"place\": {\"id\": \"key\", \"code\": \"setupbuild:keyframe\", \"at\": [0, 0, 0]}}";

  // Fails when a paid stage's StoreWildCard variant is not stored under the key materials names, so
  // the stage keyed on it reads no variant.
  [Fact]
  public void A_complete_stores_the_variant_materials_names_for_a_paid_stage()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("all", "{\"setupbuild:keyframe\": 1, \"game:plank-pine\": 1}"),
      PlacedKey
        + ", {\"complete\": {\"of\": \"key\", \"to\": 1, \"materials\": {\"wood\": \"pine\"}}}"
    );

    scene.Run(Mods);

    Assert.Equal("pine", KeyFrameRcc(scene).StoredWildCards["wood"]);
  }

  // Fails when no variant is stored without materials, or the stored one is not the first
  // registered collectible's.
  [Fact]
  public void A_complete_without_materials_stores_the_first_registered_variant()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("all", "{\"setupbuild:keyframe\": 1, \"game:plank-oak\": 1}"),
      PlacedKey + ", {\"complete\": {\"of\": \"key\", \"to\": 1}}"
    );
    Item first = scene.World.World.Items.First(i =>
      i is { Code.Path: { } path, Id: not 0 } && path.StartsWith("plank-")
    );

    scene.Run(Mods);

    Assert.Equal(first.Variant["wood"], KeyFrameRcc(scene).StoredWildCards["wood"]);
  }

  // Fails when a stage left unpaid by to stores its variant.
  [Fact]
  public void A_complete_stores_no_variant_for_a_stage_it_does_not_pay()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("stacks", "{}"),
      PlacedKey
        + ", {\"complete\": {\"of\": \"key\", \"to\": 0, \"materials\": {\"wood\": \"pine\"}}}"
    );

    scene.Run(Mods);

    Assert.Empty(KeyFrameRcc(scene).StoredWildCards);
  }

  // Fails when complete raises a structure's cells without drawing them.
  [Fact]
  public void A_structure_short_of_the_cells_it_raises_fails_at_complete()
  {
    string message = Refused(
      "smokestack",
      Gathered("all", "{\"smokestack:smokestack-intake-tier3-n\": 1}"),
      Stack
    );

    Assert.StartsWith("t.json: steps[0]: the pack holds 0 of ", message);
    Assert.EndsWith("; the step takes 1.", message);
  }

  // Fails when a structure's raised cells are left out of the spawn bounds, so a spawn two cells
  // above its core is not given.
  [Fact]
  public void A_spawn_above_a_completed_structure_is_given()
  {
    using SetupScene scene = BuildWithBox(
      "{\"scope\": \"stacks\", \"gathered\": {}, \"ends\": {\"stacks\": {\"setupbox:box\": 2}}}",
      "{\"place\": {\"id\": \"box\", \"code\": \"setupbox:box\", \"at\": [-6, 0, 0]}}"
        + ", "
        + Stack
        + ", {\"call\": {\"at\": {\"of\": \"box\"}, \"method\": \"Spill\", \"args\": [\"setupbox:box\", 2, 6, 3, 0]}}"
    );

    SetupPackRecord pack = scene
      .Run([("exlib", "0.8.4"), ("smokestack", "1.0.0"), ("setupbox", "1.0.0")])
      .Pack!;

    Assert.Equal(2, pack.End["setupbox:box"]);
  }

  // Fails when the raised cells are drawn once for all, or under the step's own block.
  [Fact]
  public void A_completed_structure_draws_each_cell_it_raises_after_its_own_block()
  {
    using SetupScene scene = Build(
      "smokestack",
      Gathered("all", "{\"*\": 1000}"),
      Stack
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal("smokestack:smokestack-intake-tier3-n", pack.Draws[0].Code);
    Assert.True(pack.Draws.Count > 10);
    Assert.All(
      pack.Draws,
      d => Assert.Equal(("steps[0]", 1), (d.Where, d.Count))
    );
    Assert.Equal(1000 - pack.Draws.Count, pack.End.Values.Single());
  }
}
#endif
