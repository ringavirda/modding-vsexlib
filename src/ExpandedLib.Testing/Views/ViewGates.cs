using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>Collectibles that draw and are held the same way, so one evaluation speaks for all of them.</summary>
/// <param name="Key">The shape, overlays, selective elements, transforms, held clips and kind the members share.</param>
/// <param name="Kind">The kind of the group's box.</param>
/// <param name="Box">The drawn box, in voxels.</param>
/// <param name="Representative">The first member in code order; the metrics are composed from it.</param>
/// <param name="Codes">Every member's code, in code order.</param>
internal sealed record ViewGroup(
  string Key,
  ItemViewKind Kind,
  CollectibleViews.ViewBox Box,
  CollectibleObject Representative,
  IReadOnlyList<string> Codes
) {
  internal bool IsBlock => Representative is Block;
}

/// <summary>G1 slot.span and G2 slot.centre of a group.</summary>
/// <param name="Span">The largest projected extent of the box, over the 48 px slot.</param>
/// <param name="Centre">The distance from the footprint's centre to the slot's centre, in pixels.</param>
internal sealed record SlotMetrics(double Span, double Centre);

/// <summary>G5 ground.rest and G6 ground.size of a group.</summary>
/// <param name="Rest">The lowest point of the posed box above the item's position, in blocks.</param>
/// <param name="Size">An item's ground scale in blocks per mesh unit; a block's longest ground extent, in blocks.</param>
internal sealed record GroundMetrics(double Rest, double Size);

/// <summary>The view gates, composed from the resolved collectibles of a loaded game world.</summary>
internal static class ViewGates {
  /// <summary>The side of an inventory slot at GUI scale 1, in pixels.</summary>
  internal const double SlotPx = 48;

  /// <summary>The collectibles of <paramref name="domain"/> that have a drawn box, grouped by what decides their views.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="domain">The domain of the collectible codes.</param>
  /// <returns>The groups in the code order of their first member; a collectible with no box or no kind belongs to none.</returns>
  internal static IReadOnlyList<ViewGroup> Groups(
    TestWorld world,
    string domain
  ) {
    IWorldAccessor w = world.World;
    IEnumerable<CollectibleObject> all = w
      .Collectibles.Where(c => c.Code != null && c.Code.Domain == domain)
      .Where(c => c is not Block { BlockId: 0 })
      .OrderBy(c => c.Code.ToString(), StringComparer.Ordinal);
    var groups = new Dictionary<string, List<CollectibleObject>>();
    foreach (CollectibleObject c in all) {
      string key = KeyOf(c);
      if (!groups.TryGetValue(key, out List<CollectibleObject>? members))
        groups[key] = members = [];
      members.Add(c);
    }
    var result = new List<ViewGroup>();
    foreach ((string key, List<CollectibleObject> members) in groups) {
      CollectibleObject first = members[0];
      ItemViewKind? kind = CollectibleViews.KindOf(world.Api, first);
      CollectibleViews.ViewBox? box = CollectibleViews.BoxOf(world.Api, first);
      if (kind == null || box == null)
        continue;
      result.Add(
        new ViewGroup(
          key,
          kind.Value,
          box,
          first,
          members.Select(m => m.Code.ToString()).ToList()
        )
      );
    }
    return result;
  }

  /// <summary>G1 and G2 of <paramref name="group"/> in the inventory slot.</summary>
  /// <param name="guiScale">The GUI scale.</param>
  internal static SlotMetrics Slot(ViewGroup group, double guiScale = 1) {
    Mat4 m = ViewMatrices.Slot(
      ViewMatrices.Transform(group.Representative, ViewTarget.Gui),
      group.IsBlock,
      guiScale
    );
    Vec3d[] p = Corners(group.Box).Select(m.Apply).ToArray();
    double x0 = p.Min(v => v.X),
      x1 = p.Max(v => v.X),
      y0 = p.Min(v => v.Y),
      y1 = p.Max(v => v.Y);
    return new SlotMetrics(
      Math.Max(x1 - x0, y1 - y0) / (SlotPx * guiScale),
      Math.Sqrt(Math.Pow((x0 + x1) / 2, 2) + Math.Pow((y0 + y1) / 2, 2))
    );
  }

  /// <summary>G5 and G6 of <paramref name="group"/> on the ground, at no yaw and no scale jitter.</summary>
  internal static GroundMetrics Ground(ViewGroup group) {
    ModelTransform tf = ViewMatrices.Transform(
      group.Representative,
      ViewTarget.Ground
    );
    Vec3d[] p = Corners(group.Box)
      .Select(ViewMatrices.Ground(tf).Apply)
      .ToArray();
    double k = 0.2 * tf.ScaleXYZ.X;
    return new GroundMetrics(
      p.Min(v => v.Y),
      group.IsBlock ? k * group.Box.Extents()[0] / 16 : k
    );
  }

  /// <summary>The eight corners of <paramref name="box"/> in mesh units (blocks), in the shape's frame.</summary>
  internal static Vec3d[] Corners(CollectibleViews.ViewBox box) {
    double[] from = [box.From.X, box.From.Y, box.From.Z];
    double[] to = [box.To.X, box.To.Y, box.To.Z];
    var corners = new Vec3d[8];
    for (int i = 0; i < 8; i++) {
      double a = (i & 1) == 0 ? from[0] : to[0],
        b = (i & 2) == 0 ? from[1] : to[1],
        c = (i & 4) == 0 ? from[2] : to[2];
      corners[i] =
        new Vec3d(
          box.Axes[0].X * a + box.Axes[1].X * b + box.Axes[2].X * c,
          box.Axes[0].Y * a + box.Axes[1].Y * b + box.Axes[2].Y * c,
          box.Axes[0].Z * a + box.Axes[1].Z * b + box.Axes[2].Z * c
        ) / 16;
    }
    return corners;
  }

  private static string KeyOf(CollectibleObject c) {
    CompositeShape? shape = c is Block block
      ? block.ShapeInventory ?? block.Shape
      : ((Item)c).Shape;
    return string.Join(
      "|",
      ShapeKey(shape),
      string.Join(",", shape?.Overlays?.Select(ShapeKey) ?? []),
      string.Join(",", shape?.SelectiveElements ?? []),
      TransformKey(ViewMatrices.Transform(c, ViewTarget.Gui)),
      TransformKey(ViewMatrices.Transform(c, ViewTarget.Hand)),
      TransformKey(ViewMatrices.Transform(c, ViewTarget.Ground)),
      c.HeldRightTpIdleAnimation,
      c.HeldRightReadyAnimation,
      c.HeldTpUseAnimation,
      c.Attributes?["itemViews"]?["kind"]?.AsString(),
      c.Tool,
      c is ItemWorkItem
    );
  }

  private static string ShapeKey(CompositeShape? s) =>
    s == null
      ? "-"
      : string.Create(
        CultureInfo.InvariantCulture,
        $"{s.Base}@{s.rotateX},{s.rotateY},{s.rotateZ}"
      );

  private static string TransformKey(ModelTransform t) =>
    string.Create(
      CultureInfo.InvariantCulture,
      $"{t.Translation}/{t.Rotation}/{t.Origin}/{t.ScaleXYZ}/{t.Rotate}"
    );
}
