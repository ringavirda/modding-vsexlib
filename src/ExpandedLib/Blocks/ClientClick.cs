using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks;

/// <summary>The part of a right-click the 1.22 client decides before the server sees the block use, for
/// the harness to run.</summary>
internal static class ClientClick {
  /// <summary>Places the block in the active hand on a sneak click, as the client does before the clicked
  /// block's <see cref="Block.OnBlockInteractStart"/>: the held block goes against the clicked face and one
  /// is taken from the stack.</summary>
  /// <param name="world">The world the block goes into.</param>
  /// <param name="player">The clicking player; its <c>ShiftKey</c> and active slot are read.</param>
  /// <param name="selection">The clicked selection; not changed.</param>
  /// <returns>Whether a block was placed. False when the shift key is up, the hand holds no block, the
  /// clicked block sets <see cref="Block.PlacedPriorityInteract"/>, or the held block refuses the place.</returns>
  public static bool PlacesFirst(
    IWorldAccessor world,
    IPlayer player,
    BlockSelection selection
  ) {
    ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
    if (
      !player.Entity.Controls.ShiftKey || slot.Itemstack?.Block is not { } held
    )
      return false;
    Block clicked = world.BlockAccessor.GetBlock(selection.Position);
    if (clicked.PlacedPriorityInteract)
      return false;
    bool offset = clicked.Replaceable < 6000;
    var target = new BlockSelection {
      Position = offset
        ? selection.Position.AddCopy(selection.Face)
        : selection.Position.Copy(),
      Face = selection.Face,
      HitPosition = selection.HitPosition,
      DidOffset = offset,
    };
    string failure = "";
    if (!held.TryPlaceBlock(world, player, slot.Itemstack, target, ref failure))
      return false;
    slot.TakeOut(1);
    slot.MarkDirty();
    return true;
  }
}
