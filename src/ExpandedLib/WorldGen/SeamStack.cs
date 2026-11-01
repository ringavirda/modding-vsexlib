#if GAME_GE_1_22
using System;
using System.Collections.Generic;

namespace ExpandedLib.WorldGen;

/// <summary>A block of layer <paramref name="Layer"/> at height <paramref name="Y"/>.</summary>
internal readonly record struct SeamCell(int Y, int Layer);

/// <summary>A layer's extent in one column, as world heights before rounding.</summary>
internal readonly record struct SeamSpan(float Bottom, float Top);

/// <summary>The layer stack of one column of a seam body.</summary>
internal static class SeamStack
{
  /// <summary>
  /// The unrounded bottom and top of every layer: boundaries accumulate from the base, each
  /// layer's offset and thickness multiplied by the taper factor.
  /// </summary>
  /// <param name="layers">Layers bottom first.</param>
  /// <param name="thicknesses">The body's drawn thickness per layer.</param>
  /// <param name="baseY">The base plane height at the column.</param>
  /// <param name="taper">Thickness factor 0..1 at the column.</param>
  public static SeamSpan[] Spans(
    IReadOnlyList<SeamLayer> layers,
    IReadOnlyList<float> thicknesses,
    float baseY,
    float taper
  )
  {
    var spans = new SeamSpan[layers.Count];
    float cursor = baseY;
    for (int i = 0; i < spans.Length; i++)
    {
      float bottom = cursor + layers[i].Offset * taper;
      float top = bottom + thicknesses[i] * taper;
      spans[i] = new SeamSpan(bottom, top);
      cursor = top;
    }
    return spans;
  }

  /// <summary>
  /// The blocks of a column, bottom up. Each boundary rounds as floor(boundary + r), so layers
  /// never overlap and a layer thinner than a block is present in a share of columns.
  /// </summary>
  /// <param name="layers">Layers bottom first.</param>
  /// <param name="thicknesses">The body's drawn thickness per layer.</param>
  /// <param name="baseY">The base plane height at the column.</param>
  /// <param name="taper">Thickness factor 0..1 at the column.</param>
  /// <param name="r">The column's hash fraction 0..1, shared by every boundary.</param>
  /// <param name="minCover">Blocks of rock kept above every layer block; 0 clips nothing.</param>
  /// <param name="rockTop">Highest rock block of the column; read only when
  /// <paramref name="minCover"/> is above 0.</param>
  public static List<SeamCell> Lay(
    IReadOnlyList<SeamLayer> layers,
    IReadOnlyList<float> thicknesses,
    float baseY,
    float taper,
    float r,
    int minCover,
    int rockTop
  )
  {
    var cells = new List<SeamCell>();
    int ceiling = minCover > 0 ? rockTop - minCover : int.MaxValue;
    var spans = Spans(layers, thicknesses, baseY, taper);
    for (int i = 0; i < spans.Length; i++)
    {
      int first = (int)MathF.Floor(spans[i].Bottom + r);
      int last = (int)MathF.Floor(spans[i].Top + r);
      for (int y = first; y < last && y <= ceiling; y++)
      {
        cells.Add(new SeamCell(y, i));
      }
    }
    return cells;
  }
}
#endif
