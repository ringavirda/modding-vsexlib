using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Molten;

/// <summary>Renders the glowing liquid-metal surface inside sand stations, canals, taps and ladles.</summary>
public class MoltenSurfaceRenderer : SurfaceRenderer {
  // Derived from block JSON attributes.
  private readonly float _fillStartY;
  private readonly float _fillHeightLevels;

  /// <summary>Fill ratio in [0, 1]; 0 hides the surface entirely.</summary>
  public float FillRatio;

  /// <summary>Metal temperature in C, drives the surface glow.</summary>
  public float Temperature;

  /// <summary>Optional explicit surface texture; usually derived from <see cref="MetalStack"/>.</summary>
  public AssetLocation? TextureName;

  /// <summary>The metal whose texture is drawn on the surface; <c>null</c> hides it.</summary>
  public ItemStack? MetalStack;

  public override double RenderOrder => 0.5;

  /// <summary>Creates a renderer whose surface footprint is <paramref name="footprintBoxes"/>, in 0-16 pixel space.</summary>
  /// <param name="fillStartY">Surface Y at fill ratio 0.</param>
  /// <param name="fillHeightLevels">1/16-unit steps the surface rises from ratio 0 to 1.</param>
  /// <param name="combine">True draws every footprint box at once as one surface (the arms of a bend or junction); false draws the one box the fill ratio selects.</param>
  public MoltenSurfaceRenderer(
    BlockPos pos,
    ICoreClientAPI api,
    Cuboidf[] footprintBoxes,
    float rotationY = 0f,
    float fillStartY = 0.125f,
    float fillHeightLevels = 12,
    bool combine = false
  )
    : base(pos, api, footprintBoxes, rotationY, combine) {
    _fillStartY = fillStartY;
    _fillHeightLevels = fillHeightLevels - 0.01f;
  }

  protected override bool ShouldRender => FillRatio > 0f && MetalStack != null;

  // Y offset from the trough floor, rising fillHeightLevels 1/16-unit steps at ratio 1.
  protected override float SurfaceY =>
    _fillStartY + FillRatio * _fillHeightLevels / 16f;

  // Selects the cross-section box for the current fill level; single-box footprints always resolve to 0.
  protected override int SelectMeshIndex() =>
    (int)(FillRatio * MeshRefs.Length);

  protected override void ConfigureShader(
    IStandardShaderProgram shader,
    IRenderAPI render
  ) => ApplyGlow(Api, shader, MetalStack!, Temperature);

  protected override bool BindSurfaceTexture(IRenderAPI render) =>
    BindMetalTexture(render, MetalStack!, ref TextureName);

  /// <summary>Sets the shader to draw <paramref name="metal"/> at <paramref name="temperature"/> in C: untinted, glowing from 550 C up.</summary>
  public static void ApplyGlow(
    ICoreClientAPI api,
    IStandardShaderProgram shader,
    ItemStack metal,
    float temperature
  ) {
    shader.RgbaTint = ColorUtil.WhiteArgbVec;
    shader.AverageColor = ColorUtil.ToRGBAVec4f(
      api.BlockTextureAtlas.GetAverageColor(
        (metal.Item?.FirstTexture ?? metal.Block.FirstTextureInventory)
          .Baked
          .TextureSubId
      )
    );
    shader.TempGlowMode = 1;

    float[] incandesence = ColorUtil.GetIncandescenceColorAsColor4f(
      (int)temperature
    );
    int glowLevel = (int)GameMath.Clamp((temperature - 550f) / 2f, 0f, 255f);
    shader.RgbaGlowIn = new Vec4f(
      incandesence[0],
      incandesence[1],
      incandesence[2],
      glowLevel / 255f
    );
    shader.ExtraGlow = glowLevel;
  }

  /// <summary>Binds the texture of <paramref name="metal"/>, falling back to the last <paramref name="textureName"/>; false when none loads.</summary>
  public static bool BindMetalTexture(
    IRenderAPI render,
    ItemStack metal,
    ref AssetLocation? textureName
  ) {
    var firstTex =
      metal.Item?.FirstTexture ?? metal.Block?.FirstTextureInventory;
    if (firstTex != null) {
      textureName = firstTex
        .Base.Clone()
        .WithPathPrefixOnce("textures/")
        .WithPathAppendixOnce(".png");
    }

    if (textureName == null)
      return false;

    int texId = render.GetOrLoadTexture(textureName);
    if (texId == 0)
      return false;

    render.BindTexture2d(texId);
    return true;
  }
}
