using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>The mesh of an open belt strung between two pulleys on parallel shafts, built without a client.</summary>
public static class BeltMesh {
  private const float Voxel = 1f / 16f;

  /// <summary>Strap width along the shafts, in blocks; <see cref="BeltPath.WidthBlocks"/>.</summary>
  public const float StrapWidth = (float)BeltPath.WidthBlocks;

  /// <summary>Strap thickness, in blocks (0.25 voxel).</summary>
  public const float StrapThickness = 0.25f * Voxel;

  /// <summary>How far the strap rides above the rim, in blocks (the crown's 0.10 voxel).</summary>
  public const float CrownLift = 0.10f * Voxel;

  /// <summary>Wrap segment count per full turn: the pulleys' 16-gon.</summary>
  public const int SegmentsPerTurn = 16;

  /// <summary>The rim radius of a pulley, in blocks: half <see cref="MpBeltWear.PulleyDiameter"/>.</summary>
  /// <param name="size">The pulley size, 1 or 2.</param>
  /// <exception cref="ArgumentException"><paramref name="size"/> is not 1 or 2.</exception>
  public static float RimRadius(int size) => MpBeltWear.PulleyDiameter(size) / 2f;

  /// <summary>The angle the strap wraps round each rim, in radians.</summary>
  /// <param name="driverSize">The driving pulley's size.</param>
  /// <param name="drivenSize">The driven pulley's size.</param>
  /// <param name="centreDistance">Axis to axis across the shafts, in blocks; over the sum of the rim radii.</param>
  /// <returns>The driver's wrap and the driven's, each in (0, 2 pi); equal radii give pi each.</returns>
  /// <exception cref="ArgumentException">A size is not 1 or 2, or <paramref name="centreDistance"/> is at or under the sum of the rim radii.</exception>
  public static (float Driver, float Driven) WrapAngles(
    int driverSize,
    int drivenSize,
    float centreDistance
  ) {
    RequireClear(driverSize, drivenSize, centreDistance);
    float phi = TangentAngle(driverSize, drivenSize, centreDistance);
    return (MathF.Tau - 2f * phi, 2f * phi);
  }

  private static void RequireClear(
    int driverSize,
    int drivenSize,
    float centreDistance
  ) {
    if (centreDistance <= RimRadius(driverSize) + RimRadius(drivenSize))
      throw new ArgumentException(
        $"Pulleys {centreDistance} blocks apart touch or overlap.",
        nameof(centreDistance)
      );
  }

  private static float TangentAngle(
    int driverSize,
    int drivenSize,
    float centreDistance
  ) {
    float ratio =
      (RimRadius(driverSize) - RimRadius(drivenSize)) / centreDistance;
    return MathF.Acos(Math.Clamp(ratio, -1f, 1f));
  }

  /// <summary>Builds the belt: its two straight runs and the wrap round each rim, as boxes of <see cref="StrapWidth"/> by <see cref="StrapThickness"/>.</summary>
  /// <param name="axis">The shafts' axis.</param>
  /// <param name="offset">The driven pulley's cell minus the driving pulley's; the component along <paramref name="axis"/> is ignored. The in-plane distance must exceed the sum of the rim radii.</param>
  /// <param name="driverSize">The driving pulley's size, 1 or 2.</param>
  /// <param name="drivenSize">The driven pulley's size, 1 or 2.</param>
  /// <exception cref="ArgumentException">A size is not 1 or 2, or the pulleys' in-plane distance is at or under the sum of their rim radii.</exception>
  /// <returns>Vertices in blocks, the driving pulley's cell corner the origin, with texture coordinates 0 to 1 over each quad and white vertex colours.</returns>
  public static MeshData Build(
    EnumAxis axis,
    Vec3i offset,
    int driverSize,
    int drivenSize
  ) {
    (float du, float dv) = InPlane(axis, offset.X, offset.Y, offset.Z);
    float distance = MathF.Sqrt(du * du + dv * dv);
    float heading = MathF.Atan2(dv, du);
    float phi = TangentAngle(driverSize, drivenSize, distance);
    (float driverWrap, float drivenWrap) = WrapAngles(
      driverSize,
      drivenSize,
      distance
    );

    List<(float Cu, float Cv, float Radius, float Angle)> path = [];
    AddArc(path, 0f, 0f, driverSize, heading + phi, driverWrap);
    AddArc(path, du, dv, drivenSize, heading - phi, drivenWrap);

    MeshData mesh = new(16, 24);
    for (int i = 0; i < path.Count; i++)
      AddSegment(mesh, axis, path[i], path[(i + 1) % path.Count]);
    mesh.Rgba.Fill(byte.MaxValue);
    return mesh;
  }

  private static void AddArc(
    List<(float, float, float, float)> path,
    float cu,
    float cv,
    int size,
    float start,
    float wrap
  ) {
    int segments = Math.Max(
      1,
      (int)MathF.Ceiling(wrap / (MathF.Tau / SegmentsPerTurn) - 1e-4f)
    );
    for (int i = 0; i <= segments; i++)
      path.Add((cu, cv, RimRadius(size), start + wrap * i / segments));
  }

  private static (float, float) InPlane(
    EnumAxis axis,
    float x,
    float y,
    float z
  ) =>
    axis switch {
      EnumAxis.X => (y, z),
      EnumAxis.Y => (x, z),
      _ => (x, y),
    };

  // The four corners of the strap's cross-section at one path sample, in blocks.
  private static Vec3f[] Section(
    EnumAxis axis,
    (float Cu, float Cv, float Radius, float Angle) sample
  ) {
    float inner = sample.Radius + CrownLift;
    float outer = inner + StrapThickness;
    float cos = MathF.Cos(sample.Angle),
      sin = MathF.Sin(sample.Angle);
    float half = StrapWidth / 2f;
    (float R, float A)[] corners =
    [
      (inner, -half),
      (inner, half),
      (outer, half),
      (outer, -half),
    ];
    Vec3f[] section = new Vec3f[4];
    for (int i = 0; i < 4; i++) {
      float u = 0.5f + sample.Cu + corners[i].R * cos;
      float v = 0.5f + sample.Cv + corners[i].R * sin;
      float a = 0.5f + corners[i].A;
      section[i] = axis switch {
        EnumAxis.X => new Vec3f(a, u, v),
        EnumAxis.Y => new Vec3f(u, a, v),
        _ => new Vec3f(u, v, a),
      };
    }
    return section;
  }

  private static void AddSegment(
    MeshData mesh,
    EnumAxis axis,
    (float, float, float, float) from,
    (float, float, float, float) to
  ) {
    Vec3f[] a = Section(axis, from),
      b = Section(axis, to);
    for (int k = 0; k < 4; k++) {
      int n = (k + 1) % 4;
      int start = mesh.VerticesCount;
      mesh.AddVertex(a[k].X, a[k].Y, a[k].Z, 0f, 0f);
      mesh.AddVertex(a[n].X, a[n].Y, a[n].Z, 1f, 0f);
      mesh.AddVertex(b[n].X, b[n].Y, b[n].Z, 1f, 1f);
      mesh.AddVertex(b[k].X, b[k].Y, b[k].Z, 0f, 1f);
      mesh.AddIndex(start);
      mesh.AddIndex(start + 1);
      mesh.AddIndex(start + 2);
      mesh.AddIndex(start);
      mesh.AddIndex(start + 2);
      mesh.AddIndex(start + 3);
    }
  }
}
