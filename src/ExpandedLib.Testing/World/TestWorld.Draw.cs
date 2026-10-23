using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
#if GAME_GE_1_22
using Vintagestory.Client.NoObf;
#endif

namespace ExpandedLib.Testing;

public sealed partial class TestWorld {
  private readonly HashSet<BlockPos> _redrawMarks = new();

  internal void MarkRedraw(BlockPos pos) => _redrawMarks.Add(pos.Copy());

  internal bool TakeRedrawMark(BlockPos pos) => _redrawMarks.Remove(pos);

#if GAME_GE_1_22
  private DrawTesselator? _draw;
  private readonly Dictionary<AssetLocation, UnloadableShape?> _drawShapes =
    new();

  /// <summary>Whether <see cref="ClientApi"/>'s <c>Tesselator</c> is the game's own shape
  /// tesselator, so a client twin builds the meshes the game would, instead of the empty mesh the
  /// default stub answers. False by default.</summary>
  /// <remarks>Read at each call, so it may change between two twins; set it before standing any twin
  /// whose <c>Initialize</c> or <c>FromTreeAttributes</c> tesselates. Shapes are read from the
  /// client assets the world names (<see cref="ClientAssets"/>) and need a game install. Texture
  /// sources answer every key with one rectangle; the keys asked are on
  /// <see cref="DrawnCell.TextureKeys"/>.</remarks>
  public bool ClientTesselates { get; set; }

  internal DrawTesselator DrawTesselator =>
    _draw ??= new DrawTesselator(DrawShape);

  private UnloadableShape? DrawShape(AssetLocation shape)
  {
    if (_drawShapes.TryGetValue(shape, out UnloadableShape? known))
      return known;
    UnloadableShape? loaded = null;
    if (
      ClientShape(
        shape.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json"),
        true
      )
      is IAsset asset
    )
    {
      loaded = asset.ToObject<UnloadableShape>();
      loaded.ResolveReferences(Log, shape.ToString());
      loaded.Loaded = true;
    }
    return _drawShapes[shape] = loaded;
  }
#endif
}
