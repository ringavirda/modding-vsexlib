using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
#if !GAME_GE_1_21
using FastVec3f = Vintagestory.API.MathTools.Vec3f;
#endif

namespace ExpandedLib.Helpers;

/// <summary>Fills the held, slot and ground views of every collectible whose mod depends on exlib from
/// <see cref="ItemViews.Compute(ICoreAPI, CollectibleObject)"/>, once the object loader has built them.</summary>
internal static class ItemViewsPass {
  private const double Tolerance = 1e-4;

  /// <summary>Replaces each transform equal to the game's default for its type with the computed one and
  /// fills each absent held clip; an authored transform or clip stays. Collectibles that declare kind
  /// <c>none</c>, texture-only items and collectibles of other domains are left as they are, and a shape
  /// that does not load leaves its collectible as it is with a warning.</summary>
  /// <param name="api">The api of either side, after the object loader.</param>
  /// <exception cref="InvalidOperationException">A collectible declares an <c>attributes.itemViews.kind</c>
  /// that is not a kind name in lower case.</exception>
  /// <exception cref="ArgumentException">A collectible declares a tip that is not a sign and one of x, y, z.</exception>
  internal static void Run(ICoreAPI api) {
    HashSet<string> domains =
    [
      .. api
        .ModLoader.Mods.Where(m =>
          m.Info.Dependencies.Any(d => d.ModID == "exlib")
        )
        .Select(m => m.Info.ModID),
    ];
    if (domains.Count == 0)
      return;
    foreach (
      CollectibleObject collectible in api
        .World.Blocks.Cast<CollectibleObject>()
        .Concat(api.World.Items)
    ) {
      if (
        collectible?.Code == null
        || collectible.IsMissing
        || !domains.Contains(collectible.Code.Domain)
        || CollectibleViews.OptedOut(collectible)
      )
        continue;
      ItemViewSet? views = ItemViews.Compute(api, collectible);
      if (views == null) {
        WarnUnloadedShape(api, collectible);
        continue;
      }
      Apply(collectible, views);
    }
  }

  private static void WarnUnloadedShape(
    ICoreAPI api,
    CollectibleObject collectible
  ) {
    CompositeShape? shape = collectible switch {
      Block block => block.ShapeInventory ?? block.Shape,
      Item item => item.Shape,
      _ => null,
    };
    if (shape?.Base == null || CollectibleViews.BoxOf(api, collectible) != null)
      return;
    api.Logger.Warning(
      "{0}: shape {1} does not load or has no drawn face, so its item views stay the game's",
      collectible.Code,
      shape.Base
    );
  }

  private static void Apply(CollectibleObject c, ItemViewSet views) {
    bool block = c is Block;
    if (
      Equal(
        c.GuiTransform,
        block
          ? ModelTransform.BlockDefaultGui()
          : ModelTransform.ItemDefaultGui()
      )
    )
      c.GuiTransform = views.Gui;
    if (
      Equal(
        c.TpHandTransform,
        block ? ModelTransform.BlockDefaultTp() : ModelTransform.ItemDefaultTp()
      )
    )
      c.TpHandTransform = views.TpHand;
    if (
      Equal(
        c.GroundTransform,
        block
          ? ModelTransform.BlockDefaultGround()
          : ModelTransform.ItemDefaultGround()
      )
    )
      c.GroundTransform = views.Ground;

    if (views.IdleClip != null && Absent(c.HeldRightTpIdleAnimation, null))
      c.HeldRightTpIdleAnimation = views.IdleClip;
    if (
      views.ReadyClip != null
      && Absent(c.HeldRightReadyAnimation, ViewReferences.DefaultReady)
    )
      c.HeldRightReadyAnimation = views.ReadyClip;
    if (
      views.HitClip != null
      && Absent(c.HeldTpHitAnimation, ViewReferences.DefaultHit)
    )
      c.HeldTpHitAnimation = views.HitClip;
    if (
      views.UseClip != null
      && Absent(c.HeldTpUseAnimation, ViewReferences.DefaultUse)
    )
      c.HeldTpUseAnimation = views.UseClip;
  }

  private static bool Absent(string? clip, string? gameDefault) =>
    string.IsNullOrEmpty(clip) || clip == gameDefault;

  private static bool Equal(ModelTransform? a, ModelTransform b) =>
    a == null
    || Near(a.Translation, b.Translation)
      && Near(a.Rotation, b.Rotation)
      && Near(a.Origin, b.Origin)
      && Near(a.ScaleXYZ, b.ScaleXYZ);

  private static bool Near(FastVec3f a, FastVec3f b) =>
    Math.Abs(a.X - b.X) <= Tolerance
    && Math.Abs(a.Y - b.Y) <= Tolerance
    && Math.Abs(a.Z - b.Z) <= Tolerance;
}
