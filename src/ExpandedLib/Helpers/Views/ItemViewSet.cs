using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>The views <see cref="ItemViews.Compute(Vintagestory.API.Common.ICoreAPI, CollectibleObject)"/> computes for a collectible.</summary>
/// <param name="Kind">The kind the views were laid out for.</param>
/// <param name="Gui">The slot transform.</param>
/// <param name="TpHand">The third-person hand transform; its origin is the box centre in blocks and the reference item's authored origin on the workpiece and shipped routes.</param>
/// <param name="Ground">The dropped-item transform.</param>
/// <param name="IdleClip">The held idle clip, or null where the game's stays.</param>
/// <param name="ReadyClip">The held ready clip, or null where the game's stays.</param>
/// <param name="HitClip">The held hit clip, or null where the game's stays.</param>
/// <param name="UseClip">The held use clip, or null where the game's stays.</param>
/// <param name="BoxFrom">The drawn box's lower corner in voxels; for an item in the lay frame <paramref name="BoxAxes"/>, for a block in the shape's x, y, z; null for a workpiece.</param>
/// <param name="BoxTo">The drawn box's upper corner in voxels, in the same frame as <paramref name="BoxFrom"/>; null for a workpiece.</param>
/// <param name="BoxAxes">The box frame as unit vectors in the shape's frame; for an item the longest, middle and
/// shortest axes, the longest pointing at a longtool's or tool's working end, for a block the shape's x, y and z; null for a workpiece.</param>
/// <param name="Grip">The held point in voxels in the shape's frame; null for a workpiece and for a block.</param>
public sealed record ItemViewSet(
  ItemViewKind Kind,
  ModelTransform Gui,
  ModelTransform TpHand,
  ModelTransform Ground,
  string? IdleClip,
  string? ReadyClip,
  string? HitClip,
  string? UseClip,
  Vec3d? BoxFrom,
  Vec3d? BoxTo,
  Vec3d[]? BoxAxes,
  Vec3d? Grip
);
