using System;
using System.Collections.Generic;
using Cairo;

namespace ExpandedLib.Testing;

/// <summary>One texture or item the composer drew into a <see cref="WindowFrame"/>.</summary>
/// <param name="Kind"><c>texture</c> for a composed surface, <c>stack</c> for an item stack.</param>
/// <param name="X">Left edge in frame pixels.</param>
/// <param name="Y">Top edge in frame pixels.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Label">For a stack, its code path and count; empty for a texture.</param>
public sealed record WindowDraw(
  string Kind,
  double X,
  double Y,
  double Width,
  double Height,
  string Label
);

/// <summary>One frame of a dialog rendered by <see cref="WindowRig"/>: the dialog's rectangle, with
/// transparent pixels where nothing was drawn.</summary>
public sealed class WindowFrame : IDisposable {
  private readonly ImageSurface _surface;

  internal WindowFrame(
    ImageSurface surface,
    double originX,
    double originY,
    IReadOnlyList<WindowDraw> draws
  ) {
    _surface = surface;
    OriginX = originX;
    OriginY = originY;
    Draws = draws;
  }

  /// <summary>Frame width in pixels.</summary>
  public int Width => _surface.Width;

  /// <summary>Frame height in pixels.</summary>
  public int Height => _surface.Height;

  /// <summary>Screen x of the frame's left edge, so a bounds' <c>renderX</c> minus this is its x in the
  /// frame.</summary>
  public double OriginX { get; }

  /// <summary>Screen y of the frame's top edge.</summary>
  public double OriginY { get; }

  /// <summary>Every texture and stack drawn, in draw order, in frame pixels.</summary>
  public IReadOnlyList<WindowDraw> Draws { get; }

  /// <summary>Counts the pixels of a rectangle that are not fully transparent.</summary>
  /// <param name="x">Left edge in frame pixels; clipped to the frame.</param>
  /// <param name="y">Top edge in frame pixels; clipped to the frame.</param>
  /// <param name="width">Width in pixels.</param>
  /// <param name="height">Height in pixels.</param>
  /// <returns>The number of pixels with alpha above zero; 0 when the rectangle lies outside the
  /// frame.</returns>
  public int InkIn(double x, double y, double width, double height) {
    _surface.Flush();
    int stride = _surface.Stride;
    byte[] data = _surface.Data;
    int x0 = Math.Max(0, (int)Math.Floor(x));
    int y0 = Math.Max(0, (int)Math.Floor(y));
    int x1 = Math.Min(Width, (int)Math.Ceiling(x + width));
    int y1 = Math.Min(Height, (int)Math.Ceiling(y + height));
    int count = 0;
    for (int py = y0; py < y1; py++)
      for (int px = x0; px < x1; px++)
        if (data[py * stride + px * 4 + 3] != 0)
          count++;
    return count;
  }

  /// <summary>Writes the frame as a PNG over a dark backdrop, so light text and the dialog's own
  /// translucent parts can be read.</summary>
  /// <param name="path">The file to write; its folder is created.</param>
  public void Save(string path) {
    string? dir = System.IO.Path.GetDirectoryName(
      System.IO.Path.GetFullPath(path)
    );
    if (dir != null)
      System.IO.Directory.CreateDirectory(dir);
    using var backed = new ImageSurface(Format.Argb32, Width, Height);
    using (var ctx = new Context(backed)) {
      ctx.SetSourceRGBA(0.19, 0.19, 0.19, 1);
      ctx.Paint();
      ctx.SetSourceSurface(_surface, 0, 0);
      ctx.Paint();
    }
    backed.WriteToPng(path);
  }

  public void Dispose() => _surface.Dispose();
}
