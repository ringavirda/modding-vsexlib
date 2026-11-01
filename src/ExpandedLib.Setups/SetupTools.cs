#if GAME_GE_1_22
using System;
using System.Linq;
using ExpandedLib.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Scenarios;

/// <summary>The stacks a setup's craft steps lay and hold.</summary>
internal static class SetupTools
{
  /// <summary>Fills a vessel laid for a bill that pays a liquid with the litres the bill asks for; any
  /// other stack is returned as it is.</summary>
  /// <param name="world">The world that resolves the liquid item.</param>
  /// <param name="order">The order whose bill is laid.</param>
  /// <param name="laid">The stack laid for one of the order's inputs.</param>
  /// <returns><paramref name="laid"/>.</returns>
  /// <exception cref="InvalidOperationException">The vessel refuses the liquid or takes less than
  /// the litres.</exception>
  public static ItemStack Filled(
    IWorldAccessor world,
    CraftOrder order,
    ItemStack laid
  )
  {
    if (
      CraftBills.Vessel(order, laid)
      && CraftBills.Liquid(order.Grid) is { } bill
    )
      if (
        HeldContent.Fill(world, laid, bill.Content.ToString(), bill.Litres) is
        { } refusal
      )
        throw new InvalidOperationException(refusal);
    return laid;
  }

  /// <summary>The one-item stack a sequence step's hand holds for the tool pattern
  /// <paramref name="pattern"/>: an item the pack holds that matches it, or, with no pack, the
  /// first loaded item that does.</summary>
  /// <param name="world">The world whose registered items are searched.</param>
  /// <param name="pattern">A code with <c>*</c> wildcards, as a sequence step's <c>tool</c>.</param>
  /// <param name="pack">The case's pack, or null for none.</param>
  /// <param name="drawn">The pack key the stack stands for, which the caller draws; null with no
  /// pack.</param>
  /// <returns>A stack of one at full durability.</returns>
  /// <exception cref="InvalidOperationException">The pack holds nothing that matches, or, with no
  /// pack, nothing loaded does.</exception>
  public static ItemStack Held(
    IWorldAccessor world,
    string pattern,
    SetupPack? pack,
    out string? drawn
  )
  {
    drawn = null;
    if (pack == null)
      return new ItemStack(
        world.Items.FirstOrDefault(i =>
          i?.Code != null && WildcardUtil.Match(pattern, i.Code.ToString())
        )
          ?? throw new InvalidOperationException(
            $"no loaded item matches {pattern}."
          ),
        1
      );
    drawn =
      pack.HeldKeys.FirstOrDefault(k => WildcardUtil.Match(pattern, k))
      ?? throw new InvalidOperationException($"the pack holds no {pattern}.");
    var code = new AssetLocation(drawn);
    return new ItemStack(
      (CollectibleObject?)world.GetItem(code)
        ?? world.GetBlock(code)
        ?? throw new InvalidOperationException(
          $"the pack holds {drawn}, which the load did not register."
        ),
      1
    );
  }
}
#endif
