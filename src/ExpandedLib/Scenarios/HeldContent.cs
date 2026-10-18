using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ExpandedLib.Scenarios;

/// <summary>Fills the liquid container a scenario or setup stack names, for both runners.</summary>
internal static class HeldContent {
  /// <summary>Pours <paramref name="litres"/> of the liquid item <paramref name="contentCode"/> into
  /// <paramref name="container"/>.</summary>
  /// <param name="world">The world that resolves the liquid item.</param>
  /// <param name="container">A stack of a <see cref="BlockLiquidContainerBase"/>.</param>
  /// <param name="contentCode">An item code whose stack a container takes.</param>
  /// <param name="litres">The litres to pour; null is refused.</param>
  /// <returns>Null when every litre landed, else why the fill was refused.</returns>
  public static string? Fill(
    IWorldAccessor world,
    ItemStack container,
    string contentCode,
    float? litres
  ) {
    if (container.Collectible is not BlockLiquidContainerBase vessel)
      return $"\"{container.Collectible.Code}\" is not a liquid container, so it holds no \"content\".";
    Item? liquid = world.GetItem(new AssetLocation(contentCode));
    if (liquid == null)
      return $"\"{contentCode}\" is not registered by the load.";
    if (litres is not { } amount)
      return "a \"content\" needs its \"litres\".";
    container.Attributes ??= new TreeAttribute();
    var poured = new ItemStack(liquid);
    float? perLitre = BlockLiquidContainerBase
      .GetContainableProps(poured)
      ?.ItemsPerLitre;
    if (perLitre is not { } items)
      return $"\"{contentCode}\" is no liquid a container takes.";
    poured.StackSize = (int)Math.Ceiling(items * amount);
    vessel.TryPutLiquid(container, poured, amount);
    float held = vessel.GetCurrentLitres(container);
    return held < amount - 0.001f
      ? $"\"{container.Collectible.Code}\" took {held} of the {amount} litres of \"{contentCode}\"."
      : null;
  }
}
