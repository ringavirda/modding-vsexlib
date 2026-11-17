#if GAME_GE_1_22
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

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
