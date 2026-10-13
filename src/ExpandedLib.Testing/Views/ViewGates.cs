using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
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

/// <summary>G7, G8, G10 and G13 of a group in a hand.</summary>
/// <param name="Size">The hand scale; a longtool's held length over the seraph's height; a block's longest held side, in blocks.</param>
/// <param name="Grip">The distance from the point of the shape that lands on the hand to the nearest drawn element's box, in voxels; null for a block.</param>
/// <param name="Axis">The unit vector of the box's longest axis in the pose's frame, along the box's own axis.</param>
/// <param name="Centre">The distance from the posed box centre to where the block defaults put a cube's centre, in blocks; null for an item.</param>
internal sealed record HandMetrics(
  double Size,
  double? Grip,
  Vec3d Axis,
  double? Centre
);

/// <summary>The view gates, composed from the resolved collectibles of a loaded game world.</summary>
internal static class ViewGates {
  /// <summary>The side of an inventory slot at GUI scale 1, in pixels.</summary>
  internal const double SlotPx = 48;

  /// <summary>The seraph's height, in voxels.</summary>
  internal const double SeraphVoxels = 30.3;

  /// <summary>The clip a block is carried with.</summary>
  internal const string BlockHeldClip = "holdbothhandslarge";

  /// <summary>The asset whose animation list maps animation codes to the seraph's clips.</summary>
  internal static readonly AssetLocation PlayerAsset = new(
    "game:entities/humanoid/player.json"
  );

  /// <summary>The itemtype whose held long axis G10 compares against.</summary>
  internal const string SpearReference = "game:spear-generic-iron";

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

  /// <summary>The animation code a stack is held with: a block's is <c>holdbothhandslarge</c>, an item's its idle, else
  /// its ready, else <c>helditemready</c>.</summary>
  internal static string HeldCode(CollectibleObject collectible) =>
    collectible is Block ? BlockHeldClip
    : !string.IsNullOrEmpty(collectible.HeldRightTpIdleAnimation)
      ? collectible.HeldRightTpIdleAnimation
    : !string.IsNullOrEmpty(collectible.HeldRightReadyAnimation)
      ? collectible.HeldRightReadyAnimation
    : "helditemready";

  /// <summary>The seraph shape's clip the game plays for <paramref name="code"/>: the player's animation list maps the
  /// code, ignoring case, to a clip, and the first-person hands take that clip's <c>-fp</c> twin.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="code">An animation code, such as an item's <c>heldRightTpIdleAnimation</c>.</param>
  /// <param name="firstPerson">Whether to take the <c>-fp</c> twin.</param>
  /// <returns>The clip code; null when the player has no such animation or its shape lacks the clip, which plays
  /// nothing and leaves the seraph at rest.</returns>
  internal static string? HeldClip(
    TestWorld world,
    string code,
    bool firstPerson = false
  ) {
    IAsset player = world.Api.Assets.Get(PlayerAsset);
    AnimationMetaData? meta = JsonObject
      .FromJson(player.ToText())["client"]["animations"]
      .AsArray<AnimationMetaData>()
      ?.FirstOrDefault(a =>
        string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase)
      );
    if (meta?.Animation == null)
      return null;
    string clip = meta.Animation + (firstPerson ? "-fp" : "");
    return SeraphPose
      .Clips(world.Api)
      .FirstOrDefault(c =>
        string.Equals(c, clip, StringComparison.OrdinalIgnoreCase)
      );
  }

  /// <summary>G7, G8, G10 and G13 of <paramref name="group"/>, with the seraph at frame 0 of its held clip, or at rest
  /// when the game plays none.</summary>
  /// <param name="world">The world the group comes from.</param>
  /// <param name="group">The group to measure.</param>
  /// <param name="firstPerson">Whether to pose the clip's <c>-fp</c> twin and take the first-person hand matrix; the
  /// block centre is then not measured.</param>
  internal static HandMetrics Hand(
    TestWorld world,
    ViewGroup group,
    bool firstPerson = false
  ) {
    CollectibleObject c = group.Representative;
    ModelTransform tf = ViewMatrices.Transform(c, ViewTarget.Hand);
    SeraphPose pose = SeraphPose.At(
      world.Api,
      HeldClip(world, HeldCode(c), firstPerson)
    );
    Mat4 m = firstPerson
      ? ViewMatrices.HandFp(tf, pose)
      : ViewMatrices.Hand(tf, pose);

    double[] side =
    [
      group.Box.To.X - group.Box.From.X,
      group.Box.To.Y - group.Box.From.Y,
      group.Box.To.Z - group.Box.From.Z,
    ];
    int longest = Array.IndexOf(side, side.Max());
    Vec3d axis = m.Apply(group.Box.Axes[longest]) - m.Apply(new Vec3d());
    axis /= axis.Length();
    double? grip = group.IsBlock ? null : GripGap(group.Box, tf, pose);
    if (grip != null) {
      double[] at = GripPoint(tf, pose);
      double[] from = [group.Box.From.X, group.Box.From.Y, group.Box.From.Z];
      if (at[longest] - from[longest] > side[longest] / 2)
        axis = new Vec3d() - axis;
    }

    double scale = tf.ScaleXYZ.X;
    double size =
      group.IsBlock ? scale * side[longest] / 16
      : group.Kind == ItemViewKind.Longtool
        ? scale * side[longest] / SeraphVoxels
      : scale;
    if (group.IsBlock) {
      Vec3d centre =
        Corners(group.Box).Aggregate(new Vec3d(), (sum, v) => sum + v) / 8;
      Vec3d cube = ViewMatrices
        .Hand(ViewMatrices.Default(true, ViewTarget.Hand), pose)
        .Apply(new Vec3d(0.5, 0.5, 0.5));
      return new HandMetrics(
        size,
        null,
        axis,
        firstPerson ? null : (m.Apply(centre) - cube).Length()
      );
    }
    return new HandMetrics(size, grip, axis, null);
  }

  /// <summary>G10 of <paramref name="group"/>: the angle between its held long axis and the iron spear's, each pointing
  /// from the grip toward the box end farther from it.</summary>
  /// <returns>Degrees, 0 to 180.</returns>
  internal static double AxisDegrees(
    TestWorld world,
    ViewGroup group,
    bool firstPerson = false
  ) {
    CollectibleObject spear = world.World.GetItem(
      new AssetLocation(SpearReference)
    );
    ViewGroup reference = new(
      KeyOf(spear),
      CollectibleViews.KindOf(world.Api, spear)!.Value,
      CollectibleViews.BoxOf(world.Api, spear)!,
      spear,
      [SpearReference]
    );
    double dot = Dot(
      Hand(world, group, firstPerson).Axis,
      Hand(world, reference, firstPerson).Axis
    );
    return Math.Acos(Math.Clamp(dot, -1, 1)) * 180 / Math.PI;
  }

  private static double Dot(Vec3d a, Vec3d b) =>
    a.X * b.X + a.Y * b.Y + a.Z * b.Z;

  private static double GripGap(
    CollectibleViews.ViewBox box,
    ModelTransform tf,
    SeraphPose pose
  ) {
    double[] g = GripPoint(tf, pose);
    var grip = new Vec3d(g[0], g[1], g[2]);
    return box.Elements.Min(e =>
      Math.Sqrt(
        Math.Pow(Math.Max(Math.Max(e.Lo.X - grip.X, 0), grip.X - e.Hi.X), 2)
          + Math.Pow(Math.Max(Math.Max(e.Lo.Y - grip.Y, 0), grip.Y - e.Hi.Y), 2)
          + Math.Pow(Math.Max(Math.Max(e.Lo.Z - grip.Z, 0), grip.Z - e.Hi.Z), 2)
      )
    );
  }

  // The shape point that lands on the hand solves hand(p) = anchor origin, so the anchor and the pose drop out.
  private static double[] GripPoint(ModelTransform tf, SeraphPose pose) {
    AttachmentPoint ap = pose.Attachment;
    Mat4 turn = Mat4.Identity.RotateXYZ(
      ap.RotationX + tf.Rotation.X,
      ap.RotationY + tf.Rotation.Y,
      ap.RotationZ + tf.Rotation.Z
    );
    double[] w =
    [
      -(ap.PosX / 16 + tf.Translation.X) - tf.Origin.X / tf.ScaleXYZ.X,
      -(ap.PosY / 16 + tf.Translation.Y) - tf.Origin.Y / tf.ScaleXYZ.Y,
      -(ap.PosZ / 16 + tf.Translation.Z) - tf.Origin.Z / tf.ScaleXYZ.Z,
    ];
    double[] o = [tf.Origin.X, tf.Origin.Y, tf.Origin.Z];
    return
    [
      16 * (o[0] + turn[0, 0] * w[0] + turn[1, 0] * w[1] + turn[2, 0] * w[2]),
      16 * (o[1] + turn[0, 1] * w[0] + turn[1, 1] * w[1] + turn[2, 1] * w[2]),
      16 * (o[2] + turn[0, 2] * w[0] + turn[1, 2] * w[1] + turn[2, 2] * w[2]),
    ];
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
      c.HeldTpHitAnimation,
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
