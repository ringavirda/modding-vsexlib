using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>The views <see cref="ItemViews.Compute(Vintagestory.API.Common.ICoreAPI, CollectibleObject)"/> computes for a collectible.</summary>
/// <param name="Kind">The kind the views were laid out for.</param>
/// <param name="Gui">The slot transform.</param>
/// <param name="TpHand">The third-person hand transform; its origin is the grip in blocks.</param>
/// <param name="Ground">The dropped-item transform.</param>
/// <param name="IdleClip">The held idle clip, or null where the game's stays.</param>
/// <param name="ReadyClip">The held ready clip, or null where the game's stays.</param>
/// <param name="HitClip">The held hit clip, or null where the game's stays.</param>
/// <param name="UseClip">The held use clip, or null where the game's stays.</param>
/// <param name="BoxFrom">The drawn box's lower corner in voxels, in the lay frame <paramref name="BoxAxes"/>; null for a workpiece.</param>
/// <param name="BoxTo">The drawn box's upper corner in voxels, in the lay frame; null for a workpiece.</param>
/// <param name="BoxAxes">The lay frame: the longest, middle and shortest axes of the drawn box as unit vectors in
/// the shape's frame, the longest pointing at a longtool's or tool's working end; null for a workpiece.</param>
/// <param name="Grip">The held point in voxels in the shape's frame; null for a workpiece.</param>
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
