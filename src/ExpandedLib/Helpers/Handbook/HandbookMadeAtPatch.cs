#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Machines;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ExpandedLib.Helpers;

/// <summary>Adds a Made at section to a product's handbook page, and a Makes section to a diagram's, from the
/// recipes keyed with <see cref="RecipeDiagram"/>, then the generated sections of
/// <see cref="HandbookSections"/>.</summary>
[HarmonyPatch(
  typeof(CollectibleBehaviorHandbookTextAndExtraInfo),
  nameof(CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo)
)]
internal static class HandbookMadeAtPatch
{
  private const string Slot = "\u0001";

  public static void Postfix(
    ItemSlot inSlot,
    ICoreClientAPI capi,
    ActionConsumable<string> openDetailPageFor,
    ref RichTextComponentBase[] __result
  )
  {
    CollectibleObject? collectible = inSlot.Itemstack?.Collectible;
    if (collectible == null)
      return;

    var extra = new List<RichTextComponentBase>();
    if (RecipeDiagram.IsDiagram(collectible))
      AddMakes(capi, collectible, openDetailPageFor, extra);
    else
      AddMadeAt(capi, collectible, openDetailPageFor, extra);
    HandbookSectionsRender.Add(
      capi,
      HandbookSections.For(capi, collectible),
      openDetailPageFor,
      extra
    );
    if (extra.Count > 0)
      __result = [.. __result, .. extra];
  }

  private static void AddMadeAt(
    ICoreClientAPI capi,
    CollectibleObject product,
    ActionConsumable<string> openDetailPageFor,
    List<RichTextComponentBase> into
  )
  {
    DiagramUse[] uses = RecipeDiagram
      .MadeAt(capi.World, product)
      .DistinctBy(u => u.Diagram.Code.ToString())
      .ToArray();
    if (uses.Length == 0)
      return;

    bool haveText = true;
    CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(
      into,
      capi,
      "exlib:handbook-madeat",
      ref haveText
    );
    foreach (DiagramUse use in uses)
    {
      string[] parts = Lang.Get(
          "exlib:handbook-madeat-line",
          use.Station.GetPlacedBlockName(capi.World, null),
          Slot
        )
        .Split(Slot, 2);
      var bullet = new RichTextComponent(
        capi,
        "- " + parts[0],
        CairoFont.WhiteSmallText()
      );
      bullet.PaddingLeft = 8;
      into.Add(bullet);
      into.Add(
        new LinkTextComponent(
          capi,
          new ItemStack(use.Diagram).GetName(),
          CairoFont.WhiteSmallText(),
          _ => openDetailPageFor(PageCode(capi, use.Diagram))
        )
      );
      into.Add(
        new RichTextComponent(
          capi,
          (parts.Length > 1 ? parts[1] : "") + "\n",
          CairoFont.WhiteSmallText()
        )
      );
    }
  }

  private static void AddMakes(
    ICoreClientAPI capi,
    CollectibleObject diagram,
    ActionConsumable<string> openDetailPageFor,
    List<RichTextComponentBase> into
  )
  {
    ItemStack[] products = RecipeDiagram
      .Makes(capi.World, diagram.Code.ToString())
      .Select(r => r.Output?.ResolvedItemStack)
      .OfType<ItemStack>()
      .DistinctBy(s => s.Collectible.Code.ToString())
      .ToArray();
    if (products.Length == 0)
      return;

    bool haveText = true;
    CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(
      into,
      capi,
      "exlib:handbook-diagram-makes",
      ref haveText
    );
    into.Add(new ClearFloatTextComponent(capi, 2));
    foreach (ItemStack product in products)
      into.Add(
        new SlideshowItemstackTextComponent(
          capi,
          [product],
          40,
          EnumFloat.Inline,
          cs => openDetailPageFor(PageCode(capi, cs.Collectible))
        )
      );
  }

  private static string PageCode(
    ICoreClientAPI capi,
    CollectibleObject collectible
  ) => PageCode(capi, new ItemStack(collectible));

  internal static string PageCode(ICoreClientAPI capi, ItemStack stack) =>
    HandbookPages.PageCode(capi, stack);
}
#endif
