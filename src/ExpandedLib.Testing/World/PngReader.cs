using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace ExpandedLib.Testing;

/// <summary>The pixel rectangle that holds a texture's opaque pixels: <c>X0</c>, <c>Y0</c> inclusive,
/// <c>X1</c>, <c>Y1</c> exclusive.</summary>
internal readonly record struct OpaqueRect(int X0, int Y0, int X1, int Y1) {
  internal int Width => X1 - X0;
  internal int Height => Y1 - Y0;
}

/// <summary>A managed PNG reader for the opaque bounds of a texture: 8-bit colour types 0, 2, 3, 4
/// and 6, not interlaced. The game's own image library needs a native library at test time.</summary>
internal static class PngReader {
  private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

  /// <summary>Finds the smallest rectangle holding every pixel whose alpha is above 0. A palette
  /// image takes its alpha from <c>tRNS</c> and is opaque without one.</summary>
  /// <param name="png">The file's bytes.</param>
  /// <returns>The rectangle, or null when no pixel is opaque.</returns>
  /// <exception cref="NotSupportedException">The image is interlaced, not 8-bit, of another colour
  /// type, or keys a transparent colour in a type 0 or 2 image; the message is the reason.</exception>
  /// <exception cref="InvalidDataException">The bytes are not a well-formed PNG.</exception>
  internal static OpaqueRect? OpaqueBounds(byte[] png) {
    if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
      throw new InvalidDataException("Not a PNG.");
    int width = 0,
      height = 0,
      depth = 0,
      colorType = -1,
      interlace = 0;
    byte[]? palette = null;
    byte[]? trns = null;
    var idat = new MemoryStream();
    int pos = 8;
    while (pos + 8 <= png.Length) {
      int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
      string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
      if (len < 0 || pos + 12 + len > png.Length)
        throw new InvalidDataException($"Chunk {type} runs past the end.");
      ReadOnlySpan<byte> data = png.AsSpan(pos + 8, len);
      switch (type) {
        case "IHDR":
          width = BinaryPrimitives.ReadInt32BigEndian(data);
          height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
          depth = data[8];
          colorType = data[9];
          interlace = data[12];
          break;
        case "PLTE":
          palette = data.ToArray();
          break;
        case "tRNS":
          trns = data.ToArray();
          break;
        case "IDAT":
          idat.Write(data);
          break;
      }
      pos += 12 + len;
    }
    if (colorType < 0)
      throw new InvalidDataException("No IHDR.");
    if (depth != 8)
      throw new NotSupportedException($"Bit depth {depth}.");
    if (interlace != 0)
      throw new NotSupportedException("Interlaced.");
    int channels = colorType switch {
      0 or 3 => 1,
      2 => 3,
      4 => 2,
      6 => 4,
      _ => throw new NotSupportedException($"Colour type {colorType}."),
    };
    if (trns != null && colorType is 0 or 2)
      throw new NotSupportedException(
        $"A transparent colour key on type {colorType}."
      );
    if (colorType == 3 && palette == null)
      throw new InvalidDataException("A palette image with no PLTE.");

    int stride = width * channels;
    var raw = new byte[(stride + 1) * height];
    idat.Position = 0;
    using (var z = new ZLibStream(idat, CompressionMode.Decompress))
      z.ReadExactly(raw);

    int x0 = width,
      y0 = height,
      x1 = 0,
      y1 = 0;
    var prev = new byte[stride];
    var row = new byte[stride];
    for (int y = 0; y < height; y++) {
      int at = y * (stride + 1);
      byte filter = raw[at];
      Array.Copy(raw, at + 1, row, 0, stride);
      Unfilter(filter, row, prev, channels);
      for (int x = 0; x < width; x++)
        if (Alpha(row, x, channels, colorType, trns) > 0) {
          x0 = Math.Min(x0, x);
          x1 = Math.Max(x1, x + 1);
          y0 = Math.Min(y0, y);
          y1 = y + 1;
        }
      (prev, row) = (row, prev);
    }
    return x1 == 0 ? null : new OpaqueRect(x0, y0, x1, y1);
  }

  private static int Alpha(
    byte[] row,
    int x,
    int channels,
    int colorType,
    byte[]? trns
  ) =>
    colorType switch {
      6 => row[x * 4 + 3],
      4 => row[x * 2 + 1],
      3 => trns != null && row[x] < trns.Length ? trns[row[x]] : 255,
      _ => 255,
    };

  private static void Unfilter(byte filter, byte[] row, byte[] prev, int bpp) {
    for (int i = 0; i < row.Length; i++) {
      int a = i >= bpp ? row[i - bpp] : 0;
      int b = prev[i];
      int c = i >= bpp ? prev[i - bpp] : 0;
      row[i] = (byte)(
        row[i]
        + filter switch {
          0 => 0,
          1 => a,
          2 => b,
          3 => (a + b) / 2,
          4 => Paeth(a, b, c),
          _ => throw new InvalidDataException($"Filter type {filter}."),
        }
      );
    }
  }

  private static int Paeth(int a, int b, int c) {
    int p = a + b - c;
    int pa = Math.Abs(p - a),
      pb = Math.Abs(p - b),
      pc = Math.Abs(p - c);
    return pa <= pb && pa <= pc ? a
      : pb <= pc ? b
      : c;
  }
}
