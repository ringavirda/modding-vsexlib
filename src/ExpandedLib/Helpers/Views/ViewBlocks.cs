using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static ExpandedLib.Helpers.CollectibleViews;
using static ExpandedLib.Helpers.ViewReferences;
#if !GAME_GE_1_21
using FastVec3f = Vintagestory.API.MathTools.Vec3f;
#endif

namespace ExpandedLib.Helpers;

/// <summary>The views of a block: the game's block rotations, with the origins on the drawn box and, for a
/// partial or multicell block, the scales that give its kind's reference extents.</summary>
internal static class ViewBlocks {
  internal static ItemViewSet Compute(
    ItemViewKind kind,
    ViewBox box,
    Vec3d? guiRotation = null
  ) {
    double[] ext =
    [
      box.To.X - box.From.X,
      box.To.Y - box.From.Y,
      box.To.Z - box.From.Z,
    ];
    double[] c =
    [
      (box.From.X + box.To.X) / 32,
      (box.From.Y + box.To.Y) / 32,
      (box.From.Z + box.To.Z) / 32,
    ];
    double longest = ext.Max() / 16;
    double bottom = box.Drawn.SelectMany(p => p).Min(p => p.Y) / 16;

    ModelTransform gui = ModelTransform.BlockDefaultGui();
    ModelTransform tp = ModelTransform.BlockDefaultTp();
    ModelTransform ground = ModelTransform.BlockDefaultGround();
    gui.Origin = Origin(c);
    if (guiRotation != null)
      gui.Rotation = new FastVec3f(
        (float)guiRotation.X,
        (float)guiRotation.Y,
        (float)guiRotation.Z
      );

    if (kind == ItemViewKind.Cube) {
      ground.Translation = new FastVec3f(
        0,
        (float)(-ground.ScaleXYZ.X * GroundUnit * bottom),
        0
      );
    } else {
      bool multicell = kind == ItemViewKind.Multicell;
      gui.Scale = (float)(
        (multicell ? MulticellSlotSpan : PartialSlotSpan)
        / SlotSpanAtUnitScale(box, gui)
      );
      double groundScale =
        (multicell ? MulticellGroundLong : PartialGroundLong) / longest;
      double heldScale =
        (multicell ? MulticellHeldLong : PartialHeldLong) / longest;

      ground.Rotation = new FastVec3f(0, -45, 0);
      if (ext.Min() <= ext.Max() / LeafRatio) {
        int thin = Array.IndexOf(ext, ext.Min());
        if (thin == 0)
          ground.Rotation = new FastVec3f(0, -45, 90);
        else if (thin == 2)
          ground.Rotation = new FastVec3f(90, -45, 0);
      }
      ground.Origin = Origin(c);
      ground.Scale = (float)(groundScale / GroundUnit);
      double[,] turn = Mul(
        Mul(
          Rotation(new Vec3d(0, ground.Rotation.Y, 0)),
          Rotation(new Vec3d(0, 0, ground.Rotation.Z))
        ),
        Rotation(new Vec3d(ground.Rotation.X, 0, 0))
      );
      double low = box
        .Drawn.SelectMany(p => p)
        .Min(p =>
          Mul(
            turn,
            new Vec3d(p.X / 16 - c[0], p.Y / 16 - c[1], p.Z / 16 - c[2])
          ).Y
        );
      ground.Translation = new FastVec3f(0, (float)(-groundScale * low), 0);

      FastVec3f t = tp.Translation;
      double carried = tp.ScaleXYZ.X;
      tp.Origin = Origin(c);
      tp.Scale = (float)heldScale;
      tp.Translation = new FastVec3f(
        (float)((0.5 + carried * t.X - c[0]) / heldScale),
        (float)((0.5 + carried * t.Y - c[1]) / heldScale),
        (float)((0.5 + carried * t.Z - c[2]) / heldScale)
      );
    }

    return new ItemViewSet(
      kind,
      gui,
      tp,
      ground,
      BulkHold,
      BlockReady,
      null,
      BlockUse,
      new Vec3d(box.From.X, box.From.Y, box.From.Z),
      new Vec3d(box.To.X, box.To.Y, box.To.Z),
      box.Axes,
      null
    );
  }

  private static FastVec3f Origin(double[] c) =>
    new((float)c[0], (float)c[1], (float)c[2]);

  // The slot span of the box at slot scale 1: blocks take no item shift and add 180 to the x rotation.
  private static double SlotSpanAtUnitScale(ViewBox box, ModelTransform gui) {
    double[,] m = Rotation(
      new Vec3d(gui.Rotation.X + 180, gui.Rotation.Y, gui.Rotation.Z)
    );
    Vec3d o = new(gui.Origin.X, gui.Origin.Y, gui.Origin.Z);
    double[][] p =
    [
      .. Enumerable
        .Range(0, 8)
        .Select(i =>
        {
          Vec3d corner = new(
            (i & 1) == 0 ? box.From.X : box.To.X,
            (i & 2) == 0 ? box.From.Y : box.To.Y,
            (i & 4) == 0 ? box.From.Z : box.To.Z
          );
          Vec3d v = Mul(
            m,
            new Vec3d(
              corner.X / 16 - o.X,
              corner.Y / 16 - o.Y,
              corner.Z / 16 - o.Z
            )
          );
          return new[] { v.X, v.Y };
        }),
    ];
    double w = p.Max(q => q[0]) - p.Min(q => q[0]);
    double h = p.Max(q => q[1]) - p.Min(q => q[1]);
    return SlotItemPixels * Math.Max(w, h) / SlotPixels;
  }
}
