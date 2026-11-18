#if GAME_GE_1_22
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Blocks;

public partial class BlockBehaviorExOrientable : IHandBookPageCodeProvider
{
  /// <summary>The handbook page Shift+H opens for a placed block of this family: the page of the
  /// variant that stands in the handbook when the stack picked has none. The stack's own page code
  /// on a world that is not the client's.</summary>
  string IHandBookPageCodeProvider.HandbookPageCodeForStack(
    IWorldAccessor world,
    ItemStack stack
  ) =>
    world.Api is ICoreClientAPI capi
      ? HandbookPages.Nearest(capi, stack)
      : GuiHandbookItemStackPage.PageCodeForStack(stack);
}
#endif
