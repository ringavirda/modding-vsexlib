using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// A collectible that carries molten metal as a held stack and can be filled in the hand without a
/// furnace; <c>/exmod molten</c> fills the player's active item through it.
/// </summary>
public interface IMoltenFillable {
  /// <summary>Adds <paramref name="metal"/> to the held <paramref name="vessel"/> stack, in place.</summary>
  /// <param name="world">The world the player is in.</param>
  /// <param name="vessel">The held stack of this collectible; its carried contents are rewritten.</param>
  /// <param name="metal">A one-item temperature carrier for the metal, stamped with the temperature (deg C) it is poured at.</param>
  /// <param name="units">Units offered; above zero.</param>
  /// <returns>Units taken, from 0 (the vessel is full, holds another metal or takes no metal now) up to <paramref name="units"/>.</returns>
  int FillHeld(
    IWorldAccessor world,
    ItemStack vessel,
    ItemStack metal,
    int units
  );
}
