using System;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>
/// Turns an ASCII layout into block placements: each non-blank glyph is mapped by a legend to a
/// placement action, one horizontal (X/Z) plane per <see cref="Layer"/>. A thin forwarder over
/// <see cref="SceneGrid"/>.
/// </summary>
public sealed class SceneDiagram {
  private readonly SceneGrid _grid = new();

  /// <summary>Maps a glyph to the action that places it at a resolved world position.</summary>
  public SceneDiagram On(char glyph, Action<BlockPos> place) {
    _grid.On(glyph, place);
    return this;
  }

  /// <summary>
  /// Applies one horizontal layer at height <paramref name="y"/>, with the top-left glyph at
  /// (<paramref name="originX"/>, <paramref name="y"/>, <paramref name="originZ"/>). Blank and unmapped
  /// glyphs are skipped; leading and trailing blank lines are trimmed.
  /// </summary>
  public SceneDiagram Layer(
    string ascii,
    int y = 0,
    int originX = 0,
    int originZ = 0
  ) {
    _grid.Layer(ascii, y, originX, originZ);
    return this;
  }

  /// <summary>
  /// Stacks several horizontal layers along the Y axis in one call. <paramref name="layers"/>[0] sits at
  /// <paramref name="baseY"/>, [1] at <c>baseY + 1</c>, and so on; each entry is itself a multi-line X/Z
  /// plane in the same format as <see cref="Layer"/>. All layers share the
  /// (<paramref name="originX"/>, <paramref name="originZ"/>) origin.
  /// </summary>
  public SceneDiagram Stack(
    int baseY,
    int originX,
    int originZ,
    params string[] layers
  ) {
    _grid.Stack(baseY, originX, originZ, layers);
    return this;
  }

  /// <summary>Stacks layers from <paramref name="baseY"/> upward at origin (0,0); see the fuller overload.</summary>
  public SceneDiagram Stack(int baseY, params string[] layers) =>
    Stack(baseY, 0, 0, layers);
}
