using System;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The ring drawn at each arm end of the hand-assembly ghost, on the flange face, in block units.</summary>
internal static class HandAssemblyMarks {
  /// <summary>Inner radius from the face centre in blocks: the cast flange rim, outside the bore (radius 2/16).</summary>
  internal const float Inner = 2.5f / 16f;

  /// <summary>Outer radius from the face centre in blocks: the cast flange rim.</summary>
  internal const float Outer = 4f / 16f;

  /// <summary>Quads around the ring.</summary>
  internal const int Segments = 16;

  /// <summary>Distance in blocks the ring stands outside the face.</summary>
  internal const float Standoff = 0.005f;

  /// <summary>A fresh mesh of the ring toward <paramref name="arm"/> in the cell's frame (0..1 on each axis): <see cref="Segments"/> white quads, flat on the plane <see cref="Standoff"/> outside that face.</summary>
  internal static MeshData Ring(BlockFacing arm) {
    var mesh = new MeshData(
      2 * Segments,
      6 * Segments,
      false,
      true,
      true,
      true
    );
    Vec3i n = arm.Normali;
    float plane = n.X + n.Y + n.Z > 0 ? 1f + Standoff : -Standoff;
    for (int k = 0; k < Segments; k++) {
      double angle = 2 * Math.PI * k / Segments;
      float cos = (float)Math.Cos(angle);
      float sin = (float)Math.Sin(angle);
      Add(mesh, n, plane, Inner * cos, Inner * sin);
      Add(mesh, n, plane, Outer * cos, Outer * sin);
    }
    for (int k = 0; k < Segments; k++) {
      int i0 = 2 * k;
      int i1 = 2 * ((k + 1) % Segments);
      mesh.AddIndices(i0, i0 + 1, i1 + 1, i0, i1 + 1, i1);
    }
    return mesh;
  }

  /// <summary>The tint of a ring: amber for an open end, green for a join, alpha 0x60.</summary>
  internal static Vec4f Color(bool open) =>
    ProjectionColors.Tint(
      open ? ProjectionColors.Open : ProjectionColors.Joined
    );

  private static void Add(
    MeshData mesh,
    Vec3i normal,
    float plane,
    float a,
    float b
  ) {
    float x = normal.X != 0 ? plane : 0.5f + a;
    float y =
      normal.Y != 0 ? plane
      : normal.X != 0 ? 0.5f + a
      : 0.5f + b;
    float z = normal.Z != 0 ? plane : 0.5f + b;
    mesh.AddVertexWithFlags(x, y, z, 0.5f, 0.5f, ColorUtil.WhiteArgb, 0);
  }
}
