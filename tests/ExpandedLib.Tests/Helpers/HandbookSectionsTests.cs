using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Xunit;
#if GAME_GE_1_22
using Vintagestory.GameContent;
#endif

namespace ExpandedLib.Tests;

#if GAME_GE_1_22
/// <summary>The sections a machine's handbook page generates from its definition: Built in place and
/// the Guide line, read as text through <see cref="HandbookText"/>.</summary>
[Collection(LangCollection.Name)]
public class HandbookSectionsTests : IDisposable
{
  private const string StageKey = "exlib:handbook-builtinplace-stage";
  private const string Stack = "exlib:construction-info-stack";
  private const string Plate = "stub:rcc-ingredient-metalplate";
  private const string Rod = "stub:rcc-ingredient-rod";

  private readonly TestWorld _world = new();

  public HandbookSectionsTests()
  {
    TestLang.Init();
    var svc = Substitute.For<ITranslationService>();
    svc.LanguageCode.Returns("en");
    svc.Get(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => Format(ci.Arg<string>(), ci.Arg<object[]>()));
    svc.GetIfExists(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => Format(ci.Arg<string>(), ci.Arg<object[]>()));
    svc.GetMatching(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
      .Returns(true);
    Lang.AvailableLanguages["en"] = svc;
    foreach (string metal in new[] { "iron", "steel" })
    foreach (string part in new[] { "metalplate", "rod" })
      _world.RegisterItem($"game:{part}-{metal}").VariantStrict["metal"] =
        metal;
  }

  private readonly List<string> _pageRoots = [];

  public void Dispose()
  {
    Lang.AvailableLanguages["en"] = TestLang.Service;
    foreach (string root in _pageRoots)
      Directory.Delete(root, true);
  }

  private static readonly Dictionary<string, string> Templates = new()
  {
    ["exlib:handbook-guide"] = "Guide: {0}",
    ["exlib:handbook-partof-line"] = "{0} ({1} cells)",
    ["exlib:handbook-partof-standsin"] = "{0} (stands in)",
    ["exlib:handbook-operating-hotkey"] = "{0} + {1}",
    ["exlib:handbook-operating-line"] = "{0}: {1}",
    ["exlib:handbook-operating-control"] = "{0} - {1}",
    ["exlib:handbook-operating-right"] = "Right-click",
    ["exlib:handbook-operating-left"] = "Left-click",
  };

  // A key with arguments reads "key[a,b]"; the keys of Templates keep their words.
  private static string Format(string key, object[] args) =>
    Templates.TryGetValue(key, out string? template)
      ? string.Format(template, args)
    : args.Length == 0 ? key
    : $"{key}[{string.Join(",", args)}]";

  private Block Machine(
    Action<ConstructionStages> stages,
    string? guidePage = null,
    Block? host = null
  )
  {
    Block block = TestBlocks.Configure(
      host ?? new Block(),
      "stub:machine",
      5003
    );
    var built = new ConstructionStages();
    stages(built);
    block.BlockEntityBehaviors =
    [
      new BlockEntityBehaviorType
      {
        Name = "ExRightClickConstructable",
        properties = new JsonObject(built.Build()),
      },
    ];
    if (guidePage != null)
      block.Attributes = new JsonObject(
        new JObject { ["handbook"] = new JObject { ["page"] = guidePage } }
      );
    return block;
  }

  private string[] Page(Block block) =>
    HandbookText.Lines(_world.ClientApi, new ItemStack(block));

  // Stage 0 rises with the block; stage 1 takes plates and a rod, stage 2 nothing, stage 3 rods.
  private static void RisingStages(ConstructionStages s) =>
    s.Stage(_ => { })
      .Stage(x => x.RequireMetalPlate("stub", 2).RequireMetalRod("stub", 1))
      .Stage(x => x.AddElements("Frame"))
      .Stage(x => x.RequireMetalRod("stub", 3));

  // Fails when the Total row sums the last stage that names a material instead of every stage:
  // the rods read 3, not 4.
  [Fact]
  public void The_total_row_sums_each_material_across_the_stages()
  {
    string[] page = Page(Machine(RisingStages));

    Assert.Equal(
      $"exlib:handbook-builtinplace-total: {Stack}[2,{Plate}], {Stack}[4,{Rod}]",
      page[^1]
    );
  }

  // Fails when the Total key carries the ingredient name: the same rod under two names totals twice.
  [Fact]
  public void The_total_row_merges_one_item_shown_under_different_ingredient_names()
  {
    string[] page = Page(
      Machine(s =>
        s.Stage(_ => { })
          .Stage(x => x.Require("game:rod-iron", 1, "stub:first"))
          .Stage(x => x.Require("game:rod-iron", 2, "stub:second"))
      )
    );

    Assert.Equal(
      $"exlib:handbook-builtinplace-total: {Stack}[3,game:item-rod-iron]",
      page[^1]
    );
  }

  // Fails when a stage that takes nothing loses its row: stage 3 would be numbered 2.
  [Fact]
  public void A_stage_that_takes_nothing_keeps_its_row_and_reads_none()
  {
    string[] page = Page(Machine(RisingStages));

    Assert.Equal(
      [
        "exlib:handbook-builtinplace",
        $"{StageKey}[1]: {Stack}[2,{Plate}], {Stack}[1,{Rod}]",
        $"{StageKey}[2]: exlib:handbook-builtinplace-none",
        $"{StageKey}[3]: {Stack}[3,{Rod}]",
        $"exlib:handbook-builtinplace-total: {Stack}[2,{Plate}], {Stack}[4,{Rod}]",
      ],
      page
    );
  }

  // Fails when the all-empty check is dropped: the section is returned with none rows.
  [Fact]
  public void A_block_whose_stages_all_take_nothing_has_no_section()
  {
    Block block = Machine(s =>
      s.Stage(_ => { })
        .Stage(x => x.AddElements("Frame"))
        .Stage(x => x.AddElements("Lid"))
    );

    Assert.Empty(Page(block));
  }

  private static string Offsets(int number, int count) =>
    string.Join(
      ",",
      Enumerable
        .Range(0, count)
        .Select(x => $"{{\"x\":{x},\"y\":0,\"z\":0,\"w\":{number}}}")
    );

  // A structure block with a handbook page whose blockNumbers are the given selector -> number pairs
  // and whose offsets carry each number the given number of times.
  private Block Structure(
    int id,
    string code,
    string numbers,
    params (int Number, int Count)[] cells
  )
  {
    Block block = TestBlocks.Configure(new Block(), code, id);
    block.CreativeInventoryTabs = ["general"];
    block.Attributes = new JsonObject(
      JObject.Parse(
        $"{{\"multiblockStructure\":{{\"blockNumbers\":{numbers},\"offsets\":[{string.Join(",", cells.Select(c => Offsets(c.Number, c.Count)))}]}}}}"
      )
    );
    _world.Register(block);
    return block;
  }

  // Fails when every offset is counted: the three machine cells read 5.
  [Fact]
  public void Part_of_counts_the_offsets_that_carry_the_blocks_number()
  {
    Structure(
      5100,
      "stub:core",
      "{\"stub:machine*\":1,\"stub:other\":2}",
      (1, 3),
      (2, 2)
    );

    string[] page = Page(Machine(RisingStages));

    Assert.Equal(
      ["exlib:handbook-partof", "stub:block-core (3 cells)"],
      page[..2]
    );
  }

  // Fails when the air check is dropped: the stand-in reads a cell count.
  [Fact]
  public void Part_of_reads_stands_in_for_a_selector_that_also_matches_air()
  {
    Structure(5100, "stub:core", "{\"*:@(air|machine)\":1}", (1, 4));

    string[] page = Page(Machine(RisingStages));

    Assert.Equal("stub:block-core (stands in)", page[1]);
  }

  // Fails when a structure without a handbook page is listed.
  [Fact]
  public void Part_of_leaves_out_a_structure_with_no_handbook_page()
  {
    Block core = Structure(5100, "stub:core", "{\"stub:machine*\":1}", (1, 3));
    core.CreativeInventoryTabs = null;

    string[] page = Page(Machine(RisingStages));

    Assert.DoesNotContain(page, l => l.Contains("block-core"));
  }

  private sealed class OperatedBlock : Block, IHandbookOperatingHelp
  {
    public HandbookControl[] GetHandbookOperatingHelp(ICoreClientAPI capi) =>
      [
        new HandbookControl(
          null,
          [
            new WorldInteraction
            {
              ActionLangCode = "stub:blockhelp-load",
              MouseButton = EnumMouseButton.Right,
              HotKeyCode = "sneak",
            },
            new WorldInteraction
            {
              ActionLangCode = "stub:blockhelp-fire",
              MouseButton = EnumMouseButton.Left,
            },
            new WorldInteraction
            {
              ActionLangCode = "stub:blockhelp-pinch",
              MouseButton = EnumMouseButton.Right,
              HotKeyCodes = ["ctrl", "unmapped"],
            },
          ]
        ),
      ];
  }

  // Fails when the hotkey prefix is dropped: the first line reads "Right-click: ...".
  [Fact]
  public void Operating_lines_join_the_hotkeys_to_the_button_and_the_action()
  {
    var block = TestBlocks.Configure(
      new OperatedBlock(),
      "stub:operated",
      5200
    );

    string[] page = Page(block);

    Assert.Equal(
      [
        "exlib:handbook-operating",
        "Shift + Right-click: stub:blockhelp-load",
        "Left-click: stub:blockhelp-fire",
        "Ctrl + unmapped + Right-click: stub:blockhelp-pinch",
      ],
      page
    );
  }

  private sealed class RepeatedHelpBlock : Block, IHandbookOperatingHelp
  {
    public HandbookControl[] GetHandbookOperatingHelp(ICoreClientAPI capi) =>
      [
        new HandbookControl(
          null,
          [
            new WorldInteraction
            {
              ActionLangCode = "stub:blockhelp-load",
              MouseButton = EnumMouseButton.Right,
              HotKeyCode = "sneak",
            },
            new WorldInteraction
            {
              ActionLangCode = "stub:blockhelp-load",
              MouseButton = EnumMouseButton.Right,
              HotKeyCode = "sneak",
            },
            new WorldInteraction
            {
              ActionLangCode = "stub:blockhelp-load",
              MouseButton = EnumMouseButton.Right,
              HotKeyCode = "ctrl",
            },
          ]
        ),
      ];
  }

  // Fails when the de-duplication is removed: the repeated line is listed twice.
  [Fact]
  public void Operating_drops_a_repeated_line_and_keeps_one_that_differs_in_hotkey()
  {
    var block = TestBlocks.Configure(
      new RepeatedHelpBlock(),
      "stub:repeated",
      5201
    );

    Assert.Equal(
      [
        "exlib:handbook-operating",
        "Shift + Right-click: stub:blockhelp-load",
        "Ctrl + Right-click: stub:blockhelp-load",
      ],
      Page(block)
    );
  }

  private sealed class NamedControlsBlock : Block, IHandbookOperatingHelp
  {
    private static WorldInteraction Feed() =>
      new()
      {
        ActionLangCode = "stub:blockhelp-feed",
        MouseButton = EnumMouseButton.Right,
      };

    public HandbookControl[] GetHandbookOperatingHelp(ICoreClientAPI capi) =>
      [
        new HandbookControl("stub:control-wheel", [Feed()]),
        new HandbookControl("stub:control-lever", [Feed()]),
        new HandbookControl(null, [Feed()]),
      ];
  }

  // Fails when the name prefix is dropped: the first line reads "Right-click: ..." alone.
  [Fact]
  public void A_named_control_prefixes_its_lines_with_its_name()
  {
    var block = TestBlocks.Configure(
      new NamedControlsBlock(),
      "stub:named",
      5202
    );

    Assert.Equal(
      "stub:control-wheel - Right-click: stub:blockhelp-feed",
      Page(block)[1]
    );
  }

  // Fails when Distinct runs over the unprefixed line: the lever's line is dropped as a repeat of
  // the wheel's.
  [Fact]
  public void Two_controls_with_the_same_interaction_keep_a_line_each()
  {
    var block = TestBlocks.Configure(
      new NamedControlsBlock(),
      "stub:named",
      5202
    );

    Assert.Equal(
      [
        "exlib:handbook-operating",
        "stub:control-wheel - Right-click: stub:blockhelp-feed",
        "stub:control-lever - Right-click: stub:blockhelp-feed",
        "Right-click: stub:blockhelp-feed",
      ],
      Page(block)
    );
  }

  // Fails when a null name renders through Lang.Get: the unnamed line reads " - Right-click: ..."
  // or "exlib:handbook-operating-control[,...]".
  [Fact]
  public void An_unnamed_control_leaves_its_line_unprefixed()
  {
    var block = TestBlocks.Configure(
      new NamedControlsBlock(),
      "stub:named",
      5202
    );

    Assert.Equal("Right-click: stub:blockhelp-feed", Page(block)[^1]);
  }

  private sealed class OperatedMachine : Block, IHandbookOperatingHelp
  {
    public HandbookControl[] GetHandbookOperatingHelp(ICoreClientAPI capi) =>
      [
        new HandbookControl(
          null,
          [
            new WorldInteraction
            {
              ActionLangCode = "stub:blockhelp-feed",
              MouseButton = EnumMouseButton.Right,
            },
          ]
        ),
      ];
  }

  // Fails when Guide is added last: the guide line follows the Built in place rows.
  [Fact]
  public void Sections_read_guide_then_operating_then_built_in_place()
  {
    LoadTextPage("iiex-lathe");

    string[] page = Page(
      Machine(RisingStages, "iiex-lathe", new OperatedMachine())
    );

    Assert.Equal("Guide: iiex:title -> iiex-lathe", page[0]);
    Assert.Equal("exlib:handbook-operating", page[1]);
    Assert.Equal("Right-click: stub:blockhelp-feed", page[2]);
    Assert.Equal("exlib:handbook-builtinplace", page[3]);
    Assert.StartsWith("exlib:handbook-builtinplace-total", page[^1]);
  }

  private sealed class ProducerEntity : BlockEntity, IMpEnergyProducer
  {
    public float DriveTorque(float speed) => 1f;
  }

  private sealed class ConsumerEntity : BlockEntity, IMpEnergyConsumer
  {
    public float LoadTorque(float speed) => 1f;
  }

  private sealed class StorageEntity : BlockEntity, IMpEnergyStorage
  {
    public float Inertia => 1f;
  }

  private sealed class ProducerBehavior : BlockEntityBehavior, IMpEnergyProducer
  {
    public ProducerBehavior(BlockEntity blockentity)
      : base(blockentity) { }

    public float DriveTorque(float speed) => 1f;
  }

  private sealed class ConsumerBehavior : BlockEntityBehavior, IMpEnergyConsumer
  {
    public ConsumerBehavior(BlockEntity blockentity)
      : base(blockentity) { }

    public float LoadTorque(float speed) => 1f;
  }

  class BothEntity : BlockEntity, IMpEnergyProducer, IMpEnergyConsumer
  {
    public float DriveTorque(float speed) => 1f;

    public float LoadTorque(float speed) => 1f;
  }

  // A block whose only roles come from the entity behaviour classes.
  private Block MpBehaviors(int id, string code, params Type[] behaviors)
  {
    Block block = TestBlocks.Configure(new Block(), code, id);
    block.BlockEntityBehaviors =
    [
      .. behaviors.Select(b =>
      {
        _world.RegisterClass(b.Name, b);
        return new BlockEntityBehaviorType { Name = b.Name };
      }),
    ];
    block.CreativeInventoryTabs = ["general"];
    _world.Register(block);
    return block;
  }

  // A block of the entity class, with a handbook page unless noPage.
  private Block Mp(int id, string code, Type entity, bool noPage = false)
  {
    _world.RegisterClass(entity.Name, entity);
    Block block = TestBlocks.Configure(new Block(), code, id);
    block.EntityClass = entity.Name;
    block.CreativeInventoryTabs = noPage ? null : ["general"];
    _world.Register(block);
    return block;
  }

  // Fails when the page filter is dropped: the consumer with no page reads as a second entry.
  [Fact]
  public void Drives_and_driven_by_list_the_partners_that_have_a_page()
  {
    Block engine = Mp(5300, "stub:engine", typeof(ProducerEntity));
    Block press = Mp(5301, "stub:press", typeof(ConsumerEntity));
    Mp(5302, "stub:hidden", typeof(ConsumerEntity), noPage: true);

    Assert.Equal(["exlib:handbook-drives", "stub:block-press"], Page(engine));
    Assert.Equal(["exlib:handbook-drivenby", "stub:block-engine"], Page(press));
  }

  // Fails when storage counts as a producer: the flywheel gets a Drives section and the engine
  // lists it as driven.
  [Fact]
  public void A_storage_block_gets_neither_section_and_is_not_listed()
  {
    Block engine = Mp(5300, "stub:engine", typeof(ProducerEntity));
    Mp(5301, "stub:press", typeof(ConsumerEntity));
    Block flywheel = Mp(5303, "stub:flywheel", typeof(StorageEntity));

    Assert.Empty(Page(flywheel));
    Assert.DoesNotContain(Page(engine), l => l.Contains("flywheel"));
  }

  // Fails when the entity behaviour loop is dropped: both blocks read as having no role.
  [Fact]
  public void Roles_come_from_entity_behaviours()
  {
    Block engine = MpBehaviors(5310, "stub:beengine", typeof(ProducerBehavior));
    Block press = MpBehaviors(5311, "stub:bepress", typeof(ConsumerBehavior));

    Assert.Equal(["exlib:handbook-drives", "stub:block-bepress"], Page(engine));
    Assert.Equal(
      ["exlib:handbook-drivenby", "stub:block-beengine"],
      Page(press)
    );
  }

  // Fails when a block that produces and consumes counts as a consumer: the engine lists the
  // transmission under Drives.
  [Fact]
  public void A_block_that_both_produces_and_consumes_is_not_listed_as_driven()
  {
    Block engine = Mp(5300, "stub:engine", typeof(ProducerEntity));
    Mp(5312, "stub:gearbox", typeof(BothEntity));

    Assert.Empty(Page(engine).Where(l => l.Contains("gearbox")));
  }

  private void LoadTextPage(string code)
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      "handbook-pages-" + Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(Path.Combine(root, "config", "handbook"));
    File.WriteAllText(
      Path.Combine(root, "config", "handbook", "lathe.json"),
      new JObject { ["pageCode"] = code, ["title"] = "iiex:title" }.ToString()
    );
    _pageRoots.Add(root);
    _world.ClientAssets("iiex", root);
  }

  // Fails when the loaded-page check is dropped: the unknown code still renders a link.
  [Fact]
  public void The_guide_line_names_a_loaded_page_and_skips_an_unknown_one()
  {
    LoadTextPage("iiex-lathe");

    string[] loaded = Page(Machine(RisingStages, "iiex-lathe"));
    string[] unknown = Page(Machine(RisingStages, "iiex-absent"));

    Assert.Equal("Guide: iiex:title -> iiex-lathe", loaded[0]);
    Assert.DoesNotContain(unknown, l => l.StartsWith("Guide: "));
  }
}
#endif

/// <summary>The handbook keys of <see cref="ExBlockDef"/> keep each other, whichever is set
/// first.</summary>
public class HandbookSectionsPageMergeTests {
  public HandbookSectionsPageMergeTests() => TestModDomain.Register();

  private static JObject HandbookOf(ExBlockDef def) =>
    (JObject)def.ToJson()["attributes"]!["handbook"]!;

  // Fails when Handbook replaces the handbook object again: the page key is lost.
  [Fact]
  public void Handbook_after_HandbookPage_keeps_the_page() {
    JObject handbook = HandbookOf(
      ExBlockDef.Create("d", "c").HandbookPage("iiex-lathe").Handbook("x-*")
    );

    Assert.Equal("iiex-lathe", (string?)handbook["page"]);
    Assert.Equal("x-*", (string?)handbook["groupBy"]![0]);
  }

  // Fails when HandbookPage replaces the handbook object: the groupBy key is lost.
  [Fact]
  public void HandbookPage_after_Handbook_keeps_the_group() {
    JObject handbook = HandbookOf(
      ExBlockDef.Create("d", "c").Handbook("x-*").HandbookPage("iiex-lathe")
    );

    Assert.Equal("iiex-lathe", (string?)handbook["page"]);
    Assert.Equal("x-*", (string?)handbook["groupBy"]![0]);
  }
}
