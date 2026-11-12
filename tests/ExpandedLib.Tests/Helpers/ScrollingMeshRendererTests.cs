using ExpandedLib.Helpers;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ScrollingMeshRenderer"/>: the scroll's offset over frames, the UVs it writes, and
/// the mesh and texture it hands the render API.</summary>
public class ScrollingMeshRendererTests {
  // Fails when the offset is not wrapped: 0.9 + 0.2 stays 1.1.
  [Fact]
  public void Advance_WrapsPastOne() {
    Assert.Equal(0.1f, ScrollingMeshRenderer.Advance(0.9f, 0.5f, 0.4f), 4);
  }

  // Fails when a negative remainder is not lifted into 0-1: 0.1 - 0.2 stays -0.1.
  [Fact]
  public void Advance_BackwardWrapsBelowZero() {
    Assert.Equal(0.9f, ScrollingMeshRenderer.Advance(0.1f, -0.5f, 0.4f), 4);
  }

  // Fails when a backstep too small for a float below 1 is returned as 1, a whole length the next
  // frame's wrap would have to take back.
  [Fact]
  public void Advance_TinyBackstepStaysBelowOne() {
    float phase = ScrollingMeshRenderer.Advance(0f, -1e-9f, 1f);

    Assert.InRange(phase, 0f, 0.9999999f);
  }

  // Fails when the offset is speed times the whole elapsed time: a speed set mid-run would then jump
  // the texture (0.5 x 0.75 s = 0.375 here) instead of carrying on from 0.25.
  [Fact]
  public void Advance_SpeedChangedMidRunCarriesOn() {
    float phase = ScrollingMeshRenderer.Advance(0f, 1f, 0.25f);
    phase = ScrollingMeshRenderer.Advance(phase, 0.5f, 0.5f);

    Assert.Equal(0.5f, phase, 4);
  }

  // Fails when the axis is ignored or the offset added: u takes 0.25 off, v is copied.
  [Fact]
  public void Offset_U_ScrollsUOnly() {
    float[] baseUv = [0f, 0f, 2f, 0.25f];
    float[] uv = new float[4];

    ScrollingMeshRenderer.Offset(baseUv, uv, 2, ScrollAxis.U, 0.25f);

    Assert.Equal([-0.25f, 0f, 1.75f, 0.25f], uv);
  }

  // Fails when V scrolls the first coordinate.
  [Fact]
  public void Offset_V_ScrollsVOnly() {
    float[] baseUv = [0f, 0f, 2f, 0.25f];
    float[] uv = new float[4];

    ScrollingMeshRenderer.Offset(baseUv, uv, 2, ScrollAxis.V, 0.25f);

    Assert.Equal([0f, -0.25f, 2f, 0f], uv);
  }

  private static MeshData Strip() {
    MeshData mesh = new(4, 6);
    mesh.AddVertex(0f, 0f, 0f, 0f, 0f);
    mesh.AddVertex(1f, 0f, 0f, 1f, 0f);
    mesh.AddVertex(1f, 0.25f, 0f, 1f, 0.25f);
    mesh.AddVertex(0f, 0.25f, 0f, 0f, 0.25f);
    mesh.AddIndices(0, 1, 2, 0, 2, 3);
    return mesh;
  }

  // Fails when the renderer uploads and later scrolls the caller's own mesh.
  [Fact]
  public void Constructor_UploadsACopy() {
    ICoreClientAPI api = Substitute.For<ICoreClientAPI>();
    MeshData? uploaded = null;
    api.Render.UploadMesh(Arg.Do<MeshData>(m => uploaded = m));
    MeshData mesh = Strip();

    _ = new ScrollingMeshRenderer(
      api,
      new BlockPos(0, 0, 0),
      mesh,
      new AssetLocation("game:block/leather/plain"),
      ScrollAxis.U
    );

    Assert.NotNull(uploaded);
    Assert.NotSame(mesh, uploaded);
    Assert.Equal(mesh.Uv, uploaded!.Uv);
  }

  // Fails when the texture is asked for by its bare asset name, which the texture cache does not find, or
  // when the scroll advances on a frame that draws nothing.
  [Fact]
  public void OnRenderFrame_LoadsTheTextureFile() {
    ICoreClientAPI api = Substitute.For<ICoreClientAPI>();
    var renderer = new ScrollingMeshRenderer(
      api,
      new BlockPos(0, 0, 0),
      Strip(),
      new AssetLocation("game:block/leather/plain"),
      ScrollAxis.U
    ) {
      Speed = 1f,
    };

    renderer.OnRenderFrame(0.25f, EnumRenderStage.Opaque);

    api.Render.Received()
      .GetOrLoadTexture(
        Arg.Is<AssetLocation>(a =>
          a.ToString() == "game:textures/block/leather/plain.png"
        )
      );
    Assert.Equal(0f, renderer.Phase);
  }
}
