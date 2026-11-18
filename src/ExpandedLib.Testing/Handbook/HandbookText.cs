#if GAME_GE_1_22
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>Reads the sections exlib generates for a block's handbook page as plain text, the way
/// the page reads without its pictures.</summary>
public static class HandbookText
{
  /// <summary>The generated sections of <paramref name="stack"/>'s handbook page: each heading as
  /// its translated line, then each line of the section with a stack part as the text that names
  /// it and a page link as <c>text -&gt; page code</c>. Empty for an item and for a block with no
  /// generated section.</summary>
  /// <param name="capi">The client api whose world and assets the page is read from.</param>
  /// <param name="stack">The stack whose page is read.</param>
  public static string[] Lines(ICoreClientAPI capi, ItemStack stack) =>
    [
      .. HandbookSections
        .For(capi, stack.Collectible)
        .SelectMany(section =>
          (
            section.HeadingKey == null
              ? []
              : new[] { Lang.Get(section.HeadingKey) }
          ).Concat(
            section.Lines.Select(line => string.Concat(line.Select(Text)))
          )
        ),
    ];

  /// <summary>The page code Shift+H opens for the block at <paramref name="pos"/>, as the survival
  /// handbook finds it: the stack the block picks, then its collectible's page code provider, then
  /// the stack's own page code.</summary>
  /// <param name="capi">The client api whose world holds the block.</param>
  /// <param name="pos">The cell the player's selection is on.</param>
  /// <returns>The page code, or null when the block picks no stack.</returns>
  public static string? PickPage(ICoreClientAPI capi, BlockPos pos)
  {
    ItemStack? stack = capi
      .World.BlockAccessor.GetBlock(pos)
      .OnPickBlock(capi.World, pos);
    return stack == null
      ? null
      : stack
        .Collectible.GetCollectibleInterface<IHandBookPageCodeProvider>()
        ?.HandbookPageCodeForStack(capi.World, stack)
        ?? GuiHandbookItemStackPage.PageCodeForStack(stack);
  }

  /// <summary>Whether <paramref name="pageCode"/> is the page of a stack the handbook lists.</summary>
  /// <param name="capi">The client api whose collectibles are listed.</param>
  /// <param name="pageCode">A page code, as <c>PickPage</c> returns.</param>
  public static bool HasPage(ICoreClientAPI capi, string pageCode) =>
    HandbookPages.Has(capi, pageCode);

  private static string Text(HandbookPart part) =>
    part switch
    {
      TextPart text => text.Text,
      StackPart stack => stack.Text,
      PageLinkPart link => $"{link.Text} -> {link.PageCode}",
      _ => "",
    };
}
#endif
