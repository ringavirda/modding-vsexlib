#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace ExpandedLib.Testing;

/// <summary>The texture source every draw-layer tesselation reads: it answers each key with the
/// same rectangle and records the keys asked.</summary>
internal sealed class DrawTextures : ITexPositionSource
{
  private readonly List<string> _keys = [];

  public Size2i AtlasSize { get; } = new(256, 256);

  internal IReadOnlyList<string> Keys => _keys;

  internal void Clear() => _keys.Clear();

  public TextureAtlasPosition this[string textureCode]
  {
    get
    {
      _keys.Add(textureCode);
      return new TextureAtlasPosition
      {
        x1 = 0,
        y1 = 0,
        x2 = 1f / 16,
        y2 = 1f / 16,
        atlasNumber = 0,
        atlasTextureId = 1,
      };
    }
  }
}

/// <summary>The game's own <see cref="ShapeTesselator"/>, built with no client, behind the
/// <see cref="ITesselatorAPI"/> a block entity's <c>OnTesselation</c> receives. A call that would
/// build the game's texture source or voxelize a texture is answered with the draw layer's texture
/// source or refused with <see cref="NotSupportedException"/>.</summary>
internal sealed class DrawTesselator : ITesselatorAPI
{
  private readonly ShapeTesselator _inner = new(null!, new(), new(), new());
  private readonly System.Func<AssetLocation, UnloadableShape?> _shapeOf;

  internal DrawTesselator(
    System.Func<AssetLocation, UnloadableShape?> shapeOf
  ) => _shapeOf = shapeOf;

  internal DrawTextures Textures { get; } = new();

  public void TesselateBlock(Block block, out MeshData modeldata) =>
    Composite("block", block.Code, block.Shape, out modeldata, Textures);

  public void TesselateItem(Item item, out MeshData modeldata) =>
    Composite("item", item.Code, item.Shape, out modeldata, Textures);

  public void TesselateItem(
    Item item,
    CompositeShape forShape,
    out MeshData modeldata
  ) => Composite("item", item.Code, forShape, out modeldata, Textures);

  public void TesselateItem(
    Item item,
    out MeshData modeldata,
    ITexPositionSource texSource
  ) => Composite("item", item.Code, item.Shape, out modeldata, texSource);

  public void TesselateShape(
    string type,
    AssetLocation sourceName,
    CompositeShape compositeShape,
    out MeshData modeldata,
    ITexPositionSource texSource,
    int generalGlowLevel = 0,
    byte climateColorMapIndex = 0,
    byte seasonColorMapIndex = 0,
    int? quantityElements = null,
    string[]? selectiveElements = null
  )
  {
    RefuseNonJson(compositeShape);
    Stock(compositeShape.Base);
    foreach (CompositeShape overlay in compositeShape.Overlays ?? [])
      Stock(overlay.Base);
    _inner.TesselateShape(
      type,
      sourceName,
      compositeShape,
      out modeldata,
      texSource,
      generalGlowLevel,
      climateColorMapIndex,
      seasonColorMapIndex,
      quantityElements,
      selectiveElements
    );
  }

  public void TesselateShape(
    CollectibleObject textureSourceCollectible,
    Shape shape,
    out MeshData modeldata,
    Vec3f? meshRotationDeg = null,
    int? quantityElements = null,
    string[]? selectiveElements = null
  ) =>
    _inner.TesselateShape(
      "collectible shape",
      shape,
      out modeldata,
      Textures,
      meshRotationDeg,
      0,
      0,
      0,
      quantityElements,
      selectiveElements
    );

  public void TesselateShape(
    string typeForLogging,
    Shape shapeBase,
    out MeshData modeldata,
    ITexPositionSource texSource,
    Vec3f? meshRotationDeg = null,
    int generalGlowLevel = 0,
    byte climateColorMapId = 0,
    byte seasonColorMapId = 0,
    int? quantityElements = null,
    string[]? selectiveElements = null
  ) =>
    _inner.TesselateShape(
      typeForLogging,
      shapeBase,
      out modeldata,
      texSource,
      meshRotationDeg,
      generalGlowLevel,
      climateColorMapId,
      seasonColorMapId,
      quantityElements,
      selectiveElements
    );

  public void TesselateShapeWithJointIds(
    string typeForLogging,
    Shape shapeBase,
    out MeshData modeldata,
    ITexPositionSource texSource,
    Vec3f rotation,
    int? quantityElements = null,
    string[]? selectiveElements = null
  ) =>
    _inner.TesselateShapeWithJointIds(
      typeForLogging,
      shapeBase,
      out modeldata,
      texSource,
      rotation,
      quantityElements,
      selectiveElements
    );

  public void TesselateShape(
    TesselationMetaData meta,
    Shape shapeBase,
    out MeshData modeldata
  ) => _inner.TesselateShape(meta, shapeBase, out modeldata);

  public MeshData VoxelizeTexture(
    CompositeTexture texture,
    Size2i atlasSize,
    TextureAtlasPosition atlasPos
  ) => throw Refused("VoxelizeTexture(CompositeTexture, ...)");

  public MeshData VoxelizeTexture(
    int[] texturePixels,
    int width,
    int height,
    Size2i atlasSize,
    TextureAtlasPosition pos
  ) => throw Refused("VoxelizeTexture(int[], ...)");

  public ITexPositionSource GetTextureSource(
    Block block,
    int altTextureNumber = 0,
    bool returnNullWhenMissing = false
  ) => Textures;

  [Obsolete("Use GetTextureSource instead")]
  public ITexPositionSource GetTexSource(
    Block block,
    int altTextureNumber = 0,
    bool returnNullWhenMissing = false
  ) => Textures;

  public ITexPositionSource GetTextureSource(
    Item item,
    bool returnNullWhenMissing = false
  ) => Textures;

  public ITexPositionSource GetTextureSource(
    Entity entity,
    Dictionary<string, CompositeTexture>? extraTextures = null,
    int altTextureNumber = 0,
    bool returnNullWhenMissing = false
  ) => Textures;

  private void Composite(
    string type,
    AssetLocation name,
    CompositeShape? shape,
    out MeshData modeldata,
    ITexPositionSource texSource
  )
  {
    if (shape?.Base == null)
    {
      modeldata = new MeshData();
      return;
    }
    TesselateShape(type, name, shape, out modeldata, texSource);
  }

  private void Stock(AssetLocation shape)
  {
    if (_inner.shapes.ContainsKey(shape))
      return;
    _inner.shapes[shape] =
      _shapeOf(shape)
      ?? throw new FileNotFoundException(
        $"Could not find shape file {shape} among the client shapes of the world."
      );
  }

  private static void RefuseNonJson(CompositeShape shape)
  {
    if (shape.Format != EnumShapeFormat.VintageStory)
      throw Refused($"TesselateShape of a {shape.Format} shape ({shape.Base})");
  }

  private static NotSupportedException Refused(string call) =>
    new($"The draw layer does not support {call}.");
}
#endif
