using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="StreamRenderer.StreamBox"/>: the box a pour draws, from the slope foot's stream
/// (x 7.25-8.75, z 8-9, lip 17.83 down to the basin metal at 14.4 in the north frame), and the
/// mesh a renderer uploads for it.
/// </summary>
public class StreamBoxTests {
  private static readonly Vec3f Lip = new(8f, 17.83f, 8.5f);

  // Fails when the rotation sign is flipped: 90 and 270 trade places.
  [Theory]
  [InlineData(0f, 7.25f, 8f, 8.75f, 9f)]
  [InlineData(90f, 8f, 7.25f, 9f, 8.75f)]
  [InlineData(180f, 7.25f, 7f, 8.75f, 8f)]
  [InlineData(270f, 7f, 7.25f, 8f, 8.75f)]
  public void StreamBox_TurnsWithBlock(
    float rotation,
    float x1,
    float z1,
    float x2,
    float z2
  ) {
    Cuboidf box = StreamRenderer.StreamBox(
      Lip,
      14.4f,
      1.5f,
      1f,
      rotation * GameMath.DEG2RAD
    );

    Assert.Equal(x1, box.X1, 3);
    Assert.Equal(z1, box.Z1, 3);
    Assert.Equal(x2, box.X2, 3);
    Assert.Equal(z2, box.Z2, 3);
    Assert.Equal(14.4f, box.Y1, 3);
    Assert.Equal(17.83f, box.Y2, 3);
  }

  // Fails when width and depth swap: x would span the depth.
  [Fact]
  public void StreamBox_WidthAndDepth() {
    Cuboidf box = StreamRenderer.StreamBox(Lip, 14.4f, 3f, 1f, 0f);

    Assert.Equal(3f, box.X2 - box.X1, 3);
    Assert.Equal(1f, box.Z2 - box.Z1, 3);
  }

  private sealed class TestStream(
    BlockPos pos,
    ICoreClientAPI api,
    float rotationY
  ) : StreamRenderer(pos, api, Lip, 14.4f, 1.5f, 1f, rotationY) {
    public override double RenderOrder => 0.5;

    protected override void ConfigureShader(
      IStandardShaderProgram shader,
      IRenderAPI render
    ) { }

    protected override bool BindStreamTexture(IRenderAPI render) => false;
  }

  private static MeshData UploadedMesh(float rotationY) {
    ICoreClientAPI api = Substitute.For<ICoreClientAPI>();
    MeshData? uploaded = null;
    api.Render.UploadMesh(Arg.Do<MeshData>(m => uploaded = m));
    _ = new TestStream(new BlockPos(0, 0, 0), api, rotationY);
    return uploaded!;
  }

  // Fails when the vertex colour is left at zero: the shader multiplies it in and discards alpha 0.
  [Fact]
  public void Mesh_VertexColourOpaqueWhite() {
    MeshData mesh = UploadedMesh(0f);

    Assert.Equal(mesh.VerticesCount * 4, mesh.Rgba.Length);
    Assert.All(mesh.Rgba, b => Assert.Equal(byte.MaxValue, b));
  }

  // Fails when the constructor passes 0 for the rotation to StreamBox.
  [Fact]
  public void Mesh_BoundsFollowRotation() {
    MeshData mesh = UploadedMesh(MathF.PI / 2f);
    Cuboidf box = StreamRenderer.StreamBox(Lip, 14.4f, 1.5f, 1f, MathF.PI / 2f);

    List<float> xs = [],
      ys = [],
      zs = [];
    for (int i = 0; i < mesh.VerticesCount; i++) {
      xs.Add(mesh.xyz[i * 3]);
      ys.Add(mesh.xyz[i * 3 + 1]);
      zs.Add(mesh.xyz[i * 3 + 2]);
    }

    Assert.Equal(box.X1 / 16f, xs.Min(), 3);
    Assert.Equal(box.X2 / 16f, xs.Max(), 3);
    Assert.Equal(box.Y1 / 16f, ys.Min(), 3);
    Assert.Equal(box.Y2 / 16f, ys.Max(), 3);
    Assert.Equal(box.Z1 / 16f, zs.Min(), 3);
    Assert.Equal(box.Z2 / 16f, zs.Max(), 3);
  }
}
