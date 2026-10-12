using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>Which of a collectible's transforms a matrix applies.</summary>
internal enum ViewTarget {
  Gui,
  Hand,
  Ground,
}

/// <summary>The matrices the game builds from a collectible's transforms in the inventory slot, the hand and on the
/// ground. A mesh point is in blocks (voxels / 16), and a transform is applied as the renderer applies it.</summary>
internal static class ViewMatrices {
  /// <summary>The side of an item in the slot at GUI scale 1, in pixels.</summary>
  internal const double SlotItemPx = 25.6;

  /// <summary>The player's eye height in blocks, the pivot of the first-person pitch follow.</summary>
  internal const double EyeHeight = 1.7;

  /// <summary>The first-person hands' projection, in degrees, at the game's default <c>fpHandsFoV</c>.</summary>
  internal const double FpHandsFov = 75;

  /// <summary>The pitch of a level gaze, in radians; the pitch follow turns by the pitch less this.</summary>
  internal const double LevelPitch = Math.PI;

  /// <summary>The held item's pitch follow when its attributes give no <c>heldItemPitchFollow</c>.</summary>
  internal const double DefaultPitchFollow = 0.75;

  /// <summary>The transform of <paramref name="collectible"/> for <paramref name="target"/> as the game resolved
  /// it, or the type default of a block or an item when it has none.</summary>
  internal static ModelTransform Transform(
    CollectibleObject collectible,
    ViewTarget target
  ) =>
    (
      target switch {
        ViewTarget.Gui => collectible.GuiTransform,
        ViewTarget.Hand => collectible.TpHandTransform,
        _ => collectible.GroundTransform,
      }
    ) ?? Default(collectible is Block, target);

  /// <summary>The game's default for a transform no collectible of the type authors.</summary>
  internal static ModelTransform Default(bool block, ViewTarget target) =>
    (block, target) switch {
      (true, ViewTarget.Gui) => ModelTransform.BlockDefaultGui(),
      (true, ViewTarget.Hand) => ModelTransform.BlockDefaultTp(),
      (true, _) => ModelTransform.BlockDefaultGround(),
      (false, ViewTarget.Gui) => ModelTransform.ItemDefaultGui(),
      (false, ViewTarget.Hand) => ModelTransform.ItemDefaultTp(),
      _ => ModelTransform.ItemDefaultGround(),
    };

  /// <summary>The transform an itemtype's JSON describes: every key it names, and the type default for the keys it
  /// omits (<c>translation</c>, <c>rotation</c>, <c>origin</c>, <c>scale</c> or <c>scaleXyz</c>, <c>rotate</c>).</summary>
  /// <param name="authored">The JSON object of the transform; null when the itemtype has none.</param>
  /// <param name="defaults">The type default; it is not changed.</param>
  internal static ModelTransform Resolve(
    JObject? authored,
    ModelTransform defaults
  ) {
    ModelTransform r = defaults.Clone();
    if (authored == null)
      return r;
    if (authored["translation"] is JObject t)
      r.Translation = Vec(t);
    if (authored["rotation"] is JObject rot)
      r.Rotation = Vec(rot);
    if (authored["origin"] is JObject o)
      r.Origin = Vec(o);
    if (authored["scale"] is JValue s)
      r.Scale = (float)s;
    if (authored["scaleXyz"] is JObject xyz)
      r.ScaleXYZ = Vec(xyz);
    if (authored["rotate"] is JValue rotate)
      r.Rotate = (bool)rotate;
    return r;
  }

  private static FastVec3f Vec(JObject o) =>
    new((float)(o["x"] ?? 0), (float)(o["y"] ?? 0), (float)(o["z"] ?? 0));

  /// <summary>The matrix that takes a mesh point to the inventory slot, in pixels with y down, from the slot's
  /// centre; an item sits 3 px left and 1 px above the centre, a block does not and turns 180 degrees more about x.</summary>
  /// <param name="transform">The gui transform.</param>
  /// <param name="block">Whether the stack is a block's.</param>
  /// <param name="guiScale">The GUI scale; it scales the slot and the transform's translation, not the item's shift.</param>
  /// <param name="x">The slot centre, in pixels; the matrix takes its whole part.</param>
  /// <param name="y">The slot centre, in pixels; the matrix takes its whole part.</param>
  /// <param name="z">The depth the slot draws at.</param>
  internal static Mat4 Slot(
    ModelTransform transform,
    bool block,
    double guiScale = 1,
    double x = 0,
    double y = 0,
    double z = 0
  ) {
    double size = SlotItemPx * guiScale;
    FastVec3f t = transform.Translation,
      o = transform.Origin,
      s = transform.ScaleXYZ,
      r = transform.Rotation;
    return Mat4
      .Identity.Translate((int)x - (block ? 0 : 3), (int)y - (block ? 0 : 1), z)
      .Translate(
        o.X + guiScale * t.X,
        o.Y + guiScale * t.Y,
        o.Z * size + guiScale * t.Z
      )
      .Scale(size * s.X, size * s.Y, size * s.Z)
      .RotateXYZ(r.X + (block ? 180 : 0), r.Y, r.Z)
      .Translate(-o.X, -o.Y, -o.Z);
  }

  /// <summary>The matrix that takes a mesh point to where the right hand holds it, in the seraph shape's
  /// frame: blocks, x and z not centred on the entity.</summary>
  /// <param name="transform">The third-person hand transform; the first-person hand draws with it too.</param>
  /// <param name="pose">The seraph posed at the held clip's frame.</param>
  internal static Mat4 Hand(ModelTransform transform, SeraphPose pose) {
    AttachmentPoint ap = pose.Attachment;
    FastVec3f t = transform.Translation,
      o = transform.Origin,
      s = transform.ScaleXYZ,
      r = transform.Rotation;
    return pose
      .ItemAnchor.Translate(o.X, o.Y, o.Z)
      .Scale(s.X, s.Y, s.Z)
      .Translate(ap.PosX / 16 + t.X, ap.PosY / 16 + t.Y, ap.PosZ / 16 + t.Z)
      .RotateXYZ(ap.RotationX + r.X, ap.RotationY + r.Y, ap.RotationZ + r.Z)
      .Translate(-o.X, -o.Y, -o.Z);
  }

  /// <summary>The matrix that takes a mesh point to the first-person hand, in the entity frame: the shape's frame
  /// with its x and z centre on the entity's axis, the pitch follow and the hands' height offset applied.</summary>
  /// <param name="transform">The third-person hand transform.</param>
  /// <param name="pose">The seraph posed at the held clip's "-fp" frame.</param>
  /// <param name="pitch">The gaze pitch in radians; <see cref="LevelPitch"/> is level.</param>
  /// <param name="pitchFollow">The item's <c>heldItemPitchFollow</c>.</param>
  /// <param name="yOffset">The <c>fpHandsYOffset</c> setting, in blocks.</param>
  internal static Mat4 HandFp(
    ModelTransform transform,
    SeraphPose pose,
    double pitch = LevelPitch,
    double pitchFollow = DefaultPitchFollow,
    double yOffset = 0
  ) =>
    Mat4
      .Identity.Translate(0, EyeHeight, 0)
      .RotateZ((pitch - Math.PI) * pitchFollow * 180 / Math.PI)
      .Translate(0, -EyeHeight, 0)
      .Translate(0, yOffset, 0)
      .Translate(-0.5, 0, -0.5)
      .Mul(Hand(transform, pose));

  /// <summary>The matrix that takes a mesh point to its place relative to a landed item's position, in blocks:
  /// 0.2 block per mesh unit per unit of scale, turned about y, then z, then x.</summary>
  /// <param name="transform">The ground transform.</param>
  /// <param name="yaw">The item's random yaw, in degrees; the game turns a transform that has <c>rotate</c>.</param>
  /// <param name="scaleJitter">The item's random scale term, within 0.025 either way.</param>
  internal static Mat4 Ground(
    ModelTransform transform,
    double yaw = 0,
    double scaleJitter = 0
  ) {
    FastVec3f t = transform.Translation,
      o = transform.Origin,
      s = transform.ScaleXYZ,
      r = transform.Rotation;
    return Mat4
      .Identity.Translate(t.X, t.Y, t.Z)
      .Scale(
        0.2 * s.X + scaleJitter,
        0.2 * s.Y + scaleJitter,
        0.2 * s.Z + scaleJitter
      )
      .RotateY(r.Y + yaw)
      .RotateZ(r.Z)
      .RotateX(r.X)
      .Translate(-o.X, -o.Y, -o.Z);
  }
}
