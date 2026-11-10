using System;
using System.Linq;
using ExpandedLib.Industry.MechanicalPower;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The free belt's mesh: <see cref="BeltMesh"/>.</summary>
public class BeltMeshTests {
  private const float Tolerance = 1e-4f;

  private static Vec3f[] Vertices(MeshData mesh) =>
    Enumerable
      .Range(0, mesh.VerticesCount)
      .Select(i => new Vec3f(
        mesh.xyz[i * 3],
        mesh.xyz[i * 3 + 1],
        mesh.xyz[i * 3 + 2]
      ))
      .ToArray();

  // Distance of a vertex from a pulley axis along X through the cell centre at (y, z), in blocks.
  private static float Radial(Vec3f v, float y, float z) =>
    MathF.Sqrt((v.Y - y) * (v.Y - y) + (v.Z - z) * (v.Z - z));

  [Fact]
  public void Runs_touch_both_rims() {
    // Offset (3, 4): the tangent points sit on the rims, not on the centre-to-centre line.
    MeshData mesh = BeltMesh.Build(EnumAxis.X, new Vec3i(0, 3, 4), 1, 1);
    Vec3f[] vs = Vertices(mesh);
    float rim = BeltMesh.RimRadius(1) + BeltMesh.CrownLift;

    float nearDriver = vs.Min(v => Radial(v, 0.5f, 0.5f));
    float nearDriven = vs.Min(v => Radial(v, 3.5f, 4.5f));
    Assert.Equal(rim, nearDriver, Tolerance);
    Assert.Equal(rim, nearDriven, Tolerance);

    // Two runs leave the driver's rim square to the line between the centres.
    int tangents = vs.Where(v =>
        MathF.Abs(Radial(v, 0.5f, 0.5f) - rim) < Tolerance
        && MathF.Abs(v.X - 0.5f + BeltMesh.StrapWidth / 2f) < Tolerance
        && MathF.Abs((v.Y - 0.5f) * 3f + (v.Z - 0.5f) * 4f) < 1e-3f
      )
      .Select(v => (MathF.Round(v.Y, 3), MathF.Round(v.Z, 3)))
      .Distinct()
      .Count();
    Assert.Equal(2, tangents);
  }

  [Fact]
  public void Strap_is_4_wide() {
    MeshData mesh = BeltMesh.Build(EnumAxis.X, new Vec3i(0, 3, 4), 1, 1);
    Vec3f[] vs = Vertices(mesh);

    Assert.Equal(0.25f, vs.Max(v => v.X) - vs.Min(v => v.X), Tolerance);
    Assert.Equal(0.5f, (vs.Max(v => v.X) + vs.Min(v => v.X)) / 2f, Tolerance);
  }

  [Fact]
  public void Wrap_angles_follow_the_sizes() {
    (float small, float large) = BeltMesh.WrapAngles(1, 2, 3f);
    float expectedSmall = MathF.PI - 2f * MathF.Asin(0.5f / 3f);
    Assert.Equal(expectedSmall, small, Tolerance);
    Assert.True(small < MathF.PI);

    (float driver, float driven) = BeltMesh.WrapAngles(2, 1, 3f);
    Assert.Equal(MathF.PI + 2f * MathF.Asin(0.5f / 3f), driver, Tolerance);
    Assert.Equal(MathF.Tau, large + small, Tolerance);
    Assert.Equal(MathF.Tau, driver + driven, Tolerance);

    (float equalA, float equalB) = BeltMesh.WrapAngles(1, 1, 3f);
    Assert.Equal(MathF.PI, equalA, Tolerance);
    Assert.Equal(MathF.PI, equalB, Tolerance);
  }

  [Fact]
  public void Mesh_follows_the_wrap_angles() {
    // On a 1:2 belt the large rim carries more vertices than the small one.
    MeshData mesh = BeltMesh.Build(EnumAxis.X, new Vec3i(0, 3, 0), 1, 2);
    Vec3f[] vs = Vertices(mesh);
    float onSmall = vs.Count(v =>
      MathF.Abs(Radial(v, 0.5f, 0.5f) - 0.5f - BeltMesh.CrownLift) < Tolerance
    );
    float onLarge = vs.Count(v =>
      MathF.Abs(Radial(v, 3.5f, 0.5f) - 1f - BeltMesh.CrownLift) < Tolerance
    );

    Assert.True(onLarge > onSmall);
  }

  [Fact]
  public void Mesh_follows_the_offset_direction() {
    MeshData below = BeltMesh.Build(EnumAxis.X, new Vec3i(0, -3, 0), 1, 1);
    MeshData above = BeltMesh.Build(EnumAxis.X, new Vec3i(0, 3, 0), 1, 1);

    Assert.True(Vertices(below).Min(v => v.Y) < -2f);
    Assert.True(Vertices(below).Max(v => v.Y) < 1.5f);
    Assert.True(Vertices(above).Max(v => v.Y) > 4f);
    Assert.True(Vertices(above).Min(v => v.Y) > -0.5f);
  }

  [Fact]
  public void Vertices_in_the_driver_frame() {
    MeshData mesh = BeltMesh.Build(EnumAxis.X, new Vec3i(0, 0, 3), 1, 1);
    Vec3f[] vs = Vertices(mesh);

    // The driver's rim, in the driver cell's own frame: centre (0.5, 0.5) and no world offset.
    Assert.Equal(
      0.5f
        - BeltMesh.RimRadius(1)
        - BeltMesh.CrownLift
        - BeltMesh.StrapThickness,
      vs.Min(v => v.Y),
      Tolerance
    );
    Assert.Equal(
      0.5f
        + BeltMesh.RimRadius(1)
        + BeltMesh.CrownLift
        + BeltMesh.StrapThickness,
      vs.Max(v => v.Y),
      Tolerance
    );
  }

  [Fact]
  public void Axis_decides_the_plane() {
    MeshData mesh = BeltMesh.Build(EnumAxis.Z, new Vec3i(3, 0, 0), 1, 1);
    Vec3f[] vs = Vertices(mesh);

    Assert.Equal(0.25f, vs.Max(v => v.Z) - vs.Min(v => v.Z), Tolerance);
    Assert.True(vs.Max(v => v.X) > 3.5f);
  }

  [Theory]
  [InlineData(0, 0, 1, 1)]
  [InlineData(0, 1, 1, 1)]
  [InlineData(0, 1, 1, 2)]
  [InlineData(0, 2, 2, 2)]
  public void Pulleys_touching_or_overlapping_are_refused(
    int y,
    int z,
    int driver,
    int driven
  ) {
    Assert.Throws<ArgumentException>(() =>
      BeltMesh.Build(EnumAxis.X, new Vec3i(0, y, z), driver, driven)
    );
    Assert.Throws<ArgumentException>(() =>
      BeltMesh.WrapAngles(driver, driven, MathF.Sqrt(y * y + z * z))
    );
  }

  [Fact]
  public void Pulleys_clear_by_a_hair_are_accepted() {
    (float driver, float driven) = BeltMesh.WrapAngles(1, 1, 1.001f);
    Assert.True(float.IsFinite(driver) && float.IsFinite(driven));
  }
}
