using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
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

  public void Dispose() => Lang.AvailableLanguages["en"] = TestLang.Service;

  private static readonly Dictionary<string, string> Templates = new()
  {
    ["exlib:handbook-guide"] = "Guide: {0}",
    ["exlib:handbook-partof-line"] = "{0} ({1} cells)",
    ["exlib:handbook-partof-standsin"] = "{0} (stands in)",
    ["exlib:handbook-operating-hotkey"] = "{0} + {1}",
    ["exlib:handbook-operating-line"] = "{0}: {1}",
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
    string? guidePage = null
  )
  {
    Block block = TestBlocks.Configure(new Block(), "stub:machine", 5003);
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
      page[^2..]
    );
  }

  // Fails when the air check is dropped: the stand-in reads a cell count.
  [Fact]
  public void Part_of_reads_stands_in_for_a_selector_that_also_matches_air()
  {
    Structure(5100, "stub:core", "{\"*:@(air|machine)\":1}", (1, 4));

    string[] page = Page(Machine(RisingStages));

    Assert.Equal("stub:block-core (stands in)", page[^1]);
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
    public WorldInteraction[] GetHandbookOperatingHelp(ICoreClientAPI capi) =>
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

  private void LoadTextPage(string code) =>
    _world
      .ClientApi.Assets.GetMany<GuiHandbookTextPage>(
        Arg.Any<Vintagestory.API.Common.ILogger>(),
        Arg.Any<string>(),
        Arg.Any<string>()
      )
      .Returns(
        new Dictionary<AssetLocation, GuiHandbookTextPage>
        {
          [new AssetLocation("iiex:config/handbook/lathe.json")] =
            new GuiHandbookTextPage { pageCode = code, Title = "iiex:title" },
        }
      );

  // Fails when the loaded-page check is dropped: the unknown code still renders a link.
  [Fact]
  public void The_guide_line_names_a_loaded_page_and_skips_an_unknown_one()
  {
    LoadTextPage("iiex-lathe");

    string[] loaded = Page(Machine(RisingStages, "iiex-lathe"));
    string[] unknown = Page(Machine(RisingStages, "iiex-absent"));

    Assert.Equal("Guide: iiex:title -> iiex-lathe", loaded[^1]);
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
