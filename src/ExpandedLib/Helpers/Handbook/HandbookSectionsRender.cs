#if GAME_GE_1_22
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Helpers;

/// <summary>Turns <see cref="HandbookSection"/> models into the rich text components of a handbook
/// page.</summary>
internal static class HandbookSectionsRender
{
  private const float StackSize = 30;

  public static void Add(
    ICoreClientAPI capi,
    IEnumerable<HandbookSection> sections,
    ActionConsumable<string> openDetailPageFor,
    List<RichTextComponentBase> into
  )
  {
    foreach (HandbookSection section in sections)
    {
      if (section.HeadingKey != null)
      {
        bool haveText = true;
        CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(
          into,
          capi,
          section.HeadingKey,
          ref haveText
        );
      }
      foreach (HandbookPart[] line in section.Lines)
      {
        foreach (HandbookPart part in line)
          AddPart(capi, part, openDetailPageFor, into);
        into.Add(new RichTextComponent(capi, "\n", CairoFont.WhiteSmallText()));
      }
    }
  }

  private static void AddPart(
    ICoreClientAPI capi,
    HandbookPart part,
    ActionConsumable<string> openDetailPageFor,
    List<RichTextComponentBase> into
  )
  {
    switch (part)
    {
      case TextPart text:
        into.Add(
          new RichTextComponent(capi, text.Text, CairoFont.WhiteSmallText())
        );
        break;
      case StackPart stack:
        if (stack.Stacks.Length > 0)
          into.Add(
            new SlideshowItemstackTextComponent(
              capi,
              stack.Stacks,
              StackSize,
              EnumFloat.Inline,
              cs => openDetailPageFor(HandbookMadeAtPatch.PageCode(capi, cs))
            )
          );
        into.Add(
          new RichTextComponent(capi, stack.Text, CairoFont.WhiteSmallText())
        );
        break;
      case PageLinkPart link:
        into.Add(
          new LinkTextComponent(
            capi,
            link.Text,
            CairoFont.WhiteSmallText(),
            _ => openDetailPageFor(link.PageCode)
          )
        );
        break;
    }
  }
}
#endif
