using System;
using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
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

  // A key with arguments reads "key[a,b]"; the guide line is the one template that keeps its words.
  private static string Format(string key, object[] args) =>
    key == "exlib:handbook-guide" ? $"Guide: {args[0]}"
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
