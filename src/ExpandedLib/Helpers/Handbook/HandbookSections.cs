#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ExpandedLib.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ExpandedLib.Helpers;

/// <summary>A piece of a handbook line.</summary>
internal abstract record HandbookPart;

/// <summary>Plain text.</summary>
internal sealed record TextPart(string Text) : HandbookPart;

/// <summary>Stacks shown as one clickable slideshow, each opening its own page, then the
/// <paramref name="Text"/> that names them.</summary>
internal sealed record StackPart(ItemStack[] Stacks, string Text)
  : HandbookPart;

/// <summary>A link that opens the handbook page <paramref name="PageCode"/>.</summary>
internal sealed record PageLinkPart(string Text, string PageCode)
  : HandbookPart;

/// <summary>A generated section of a block's handbook page: an optional heading, by its lang key,
/// and its lines.</summary>
internal sealed record HandbookSection(
  string? HeadingKey,
  HandbookPart[][] Lines
);

/// <summary>The sections a block's handbook page gains from its loaded definition, as a model that
/// <see cref="HandbookSectionsRender"/> turns into rich text.</summary>
internal static class HandbookSections
{
  private const string Slot = "\u0001";
  private const string RccBehaviorName = "ExRightClickConstructable";
  private const string TextPagesPath = "config/handbook";

  private static readonly Regex StoredPlaceholder = new(@"\{[^}]*\}");

  private static readonly ConditionalWeakTable<
    ICoreClientAPI,
    Dictionary<string, string>
  > TextPageTitles = new();

  /// <summary>The sections of <paramref name="collectible"/>'s handbook page in page order; none
  /// for an item or a block that has nothing to state.</summary>
  public static List<HandbookSection> For(
    ICoreClientAPI capi,
    CollectibleObject collectible
  )
  {
    var sections = new List<HandbookSection>();
    if (collectible is not Block block)
      return sections;
    Add(sections, BuiltInPlace(capi.World, block));
    Add(sections, Guide(capi, block));
    return sections;
  }

  private static void Add(List<HandbookSection> into, HandbookSection? section)
  {
    if (section != null)
      into.Add(section);
  }

  /// <summary>One row per construction stage after the first, numbered from 1, then a Total row
  /// summing each material across the stages in first-seen order. A stage that takes neither
  /// material nor tool keeps its row, reading none. Null for a block with no stages or whose
  /// stages all take nothing.</summary>
  internal static HandbookSection? BuiltInPlace(
    IWorldAccessor world,
    Block block
  )
  {
    ExConstructionStage[]? stages = block
      .BlockEntityBehaviors?.FirstOrDefault(b => b.Name == RccBehaviorName)
      ?.properties?["stages"].AsObject<ExConstructionStage[]>(null);
    if (stages == null)
      return null;

    var lines = new List<HandbookPart[]>();
    var totals =
      new List<(string Key, ItemStack[] Stacks, string Name, int Quantity)>();
    bool anyTakes = false;
    for (int i = 1; i < stages.Length; i++)
    {
      var parts = new List<HandbookPart>
      {
        new TextPart(Lang.Get(ExlibLang.HandbookBuiltinplaceStage, i) + ": "),
      };
      var items = new List<HandbookPart>();
      foreach (ExConstructionIngredient ing in stages[i].RequireStacks ?? [])
      {
        (string key, ItemStack[] stacks, string name) = Material(world, ing);
        items.Add(MaterialPart(stacks, ing.Quantity, name));
        int at = totals.FindIndex(t => t.Key == key);
        if (at < 0)
          totals.Add((key, stacks, name, ing.Quantity));
        else
          totals[at] = totals[at] with
          {
            Quantity = totals[at].Quantity + ing.Quantity,
          };
      }
      foreach (ExConstructionToolStep step in stages[i].ToolSteps)
        if (step.Tool != null)
          items.Add(
            new TextPart(
              Lang.Get(
                  ExlibLang.ConstructionInfoTool,
                  ExRightClickConstruction.ToolKindName(world, step.Tool)
                )
                .Trim()
            )
          );
      anyTakes |= items.Count > 0;
      parts.AddRange(Separated(items));
      if (items.Count == 0)
        parts.Add(new TextPart(Lang.Get(ExlibLang.HandbookBuiltinplaceNone)));
      lines.Add([.. parts]);
    }
    if (!anyTakes)
      return null;

    if (totals.Count > 0)
    {
      var parts = new List<HandbookPart>
      {
        new TextPart(Lang.Get(ExlibLang.HandbookBuiltinplaceTotal) + ": "),
      };
      parts.AddRange(
        Separated(
          totals.Select(t => MaterialPart(t.Stacks, t.Quantity, t.Name))
        )
      );
      lines.Add([.. parts]);
    }
    return new HandbookSection(ExlibLang.HandbookBuiltinplace, [.. lines]);
  }

  private static (string Key, ItemStack[] Stacks, string Name) Material(
    IWorldAccessor world,
    ExConstructionIngredient ingredient
  )
  {
    ExConstructionIngredient ing = ingredient.Clone();
    if (ing.Code != null)
      ing.Code = new AssetLocation(
        StoredPlaceholder.Replace(ing.Code.ToString(), "*")
      );
    ing.Resolve(world, "handbook built in place");
    string key = $"{ing.Type}|{ing.Code}|{ing.Name}";
    if (ing.MatchesByPattern)
      return (
        key,
        [
          .. ConstructionHints
            .Matching(world, ing)
            .Select(c => new ItemStack(c, ing.Quantity)),
        ],
        ExRightClickConstruction.IngredientName(world, ing)
      );
    return (
      key,
      ing.ResolvedItemStack == null ? [] : [ing.ResolvedItemStack.Clone()],
      ExRightClickConstruction.ExactName(world, ing)
    );
  }

  private static StackPart MaterialPart(
    ItemStack[] stacks,
    int quantity,
    string name
  ) =>
    new(
      stacks,
      Lang.Get(ExlibLang.ConstructionInfoStack, quantity, name).Trim()
    );

  private static IEnumerable<HandbookPart> Separated(
    IEnumerable<HandbookPart> items
  )
  {
    bool first = true;
    foreach (HandbookPart item in items)
    {
      if (!first)
        yield return new TextPart(", ");
      first = false;
      yield return item;
    }
  }

  /// <summary>The Guide line: a link to the text page named by the block's
  /// <c>attributes.handbook.page</c>, titled as the page is. Null when the block names none or no
  /// loaded text page has that code.</summary>
  internal static HandbookSection? Guide(ICoreClientAPI capi, Block block)
  {
    string? code = block.Attributes?["handbook"]?["page"]?.AsString();
    if (string.IsNullOrEmpty(code))
      return null;
    if (!TextPages(capi).TryGetValue(code, out string? title))
      return null;

    string[] parts = Lang.Get(ExlibLang.HandbookGuide, Slot).Split(Slot, 2);
    var line = new List<HandbookPart>();
    if (parts[0].Length > 0)
      line.Add(new TextPart(parts[0]));
    line.Add(new PageLinkPart(Lang.Get(title), code));
    if (parts.Length > 1 && parts[1].Length > 0)
      line.Add(new TextPart(parts[1]));
    return new HandbookSection(
      null,
      [
        [.. line],
      ]
    );
  }

  private static Dictionary<string, string> TextPages(ICoreClientAPI capi) =>
    TextPageTitles.GetValue(
      capi,
      c =>
      {
        var titles = new Dictionary<string, string>();
        var pages = c.Assets?.GetMany<GuiHandbookTextPage>(
          c.Logger,
          TextPagesPath
        );
        if (pages != null)
          foreach (GuiHandbookTextPage page in pages.Values)
            if (page?.pageCode != null && page.Title != null)
              titles[page.pageCode] = page.Title;
        return titles;
      }
    );
}
#endif
