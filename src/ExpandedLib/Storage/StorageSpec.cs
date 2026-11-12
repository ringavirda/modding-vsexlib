namespace ExpandedLib.Storage;

/// <summary>
/// The ground-storage layout <see cref="ExStorage.Classify"/> gives an item variant.
/// </summary>
/// <param name="Class">The class.</param>
/// <param name="Layout">The vanilla layout name (<c>Quadrants</c>, <c>Halves</c>,
/// <c>SingleCenter</c>, <c>Messy12</c>, <c>Stacking</c>, <c>WallHalves</c>), or null for
/// <see cref="StorageClass.Kept"/> and <see cref="StorageClass.Exempt"/>.</param>
/// <param name="Capacity">Items one block holds; 0 where the class has no layout of its own.</param>
/// <param name="CollisionY2">Height of the collision box of one layer, in blocks.</param>
/// <param name="ItemsPerLayer">Items in one pile layer; 0 outside <see cref="StorageClass.Pile"/>.</param>
/// <param name="CbScaleYByLayer">The <c>cbScaleYByLayer</c> property, one over the items in a layer;
/// 0 outside <see cref="StorageClass.Pile"/>.</param>
/// <param name="Reason">Why the item is exempt; null otherwise.</param>
public sealed record StorageSpec(
  StorageClass Class,
  string? Layout,
  int Capacity,
  double CollisionY2,
  int ItemsPerLayer = 0,
  double CbScaleYByLayer = 0,
  string? Reason = null
);
