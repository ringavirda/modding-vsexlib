using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The marks the hand assembly ghost carries at its arm ends: where each one stands and its colour.</summary>
internal static class HandAssemblyMarks {
  private const float HalfWidth = 3f / 16f;
  private const float Thickness = 1f / 16f;
  private const float Standoff = 0.005f;

  private static readonly Vec4f Joined = new(
    94f / 255f,
    215f / 255f,
    150f / 255f,
    1f
  );
  private static readonly Vec4f Open = new(
    240f / 255f,
    180f / 255f,
    60f / 255f,
    1f
  );

  /// <summary>The mark of the arm toward <paramref name="arm"/>, in block-local units where the ghost's cell is 0..1 on each axis: a square plate 6/16 wide and 1/16 thick, centred on that face and standing just outside it.</summary>
  internal static Cuboidf Box(BlockFacing arm) {
    Vec3i n = arm.Normali;
    (float x1, float x2) = Span(n.X);
    (float y1, float y2) = Span(n.Y);
    (float z1, float z2) = Span(n.Z);
    return new Cuboidf(x1, y1, z1, x2, y2, z2);
  }

  /// <summary>The tint of a mark: amber for an open end, green for a join. Alpha is 1.</summary>
  internal static Vec4f Color(bool open) => (open ? Open : Joined).Clone();

  private static (float, float) Span(int normal) =>
    normal switch {
      > 0 => (1f + Standoff, 1f + Standoff + Thickness),
      < 0 => (-Standoff - Thickness, -Standoff),
      _ => (0.5f - HalfWidth, 0.5f + HalfWidth),
    };
}
