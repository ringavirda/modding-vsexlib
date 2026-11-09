using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ExpandedLib.Testing.Live;

/// <summary>A chunk-column rectangle of the probe, both corners inclusive.</summary>
internal readonly record struct ProbeTile(
  int MinColumnX,
  int MinColumnZ,
  int MaxColumnX,
  int MaxColumnZ
) {
  public int Columns =>
    (MaxColumnX - MinColumnX + 1) * (MaxColumnZ - MinColumnZ + 1);
}

/// <summary>The arguments of <c>/exmod probe run</c>: a rectangle of chunk columns, a tile size
/// and the block codes to count.</summary>
/// <param name="Recorded">Per code, whether every matching block's position is written to the
/// blocks file as well as counted.</param>
/// <param name="Terrain">Whether each column's terrain heights and geologic province are written to
/// the terrain file.</param>
/// <param name="CenterOnSpawn">The rectangle is centred on the world's default spawn column instead
/// of starting at <paramref name="MinColumnX"/>, <paramref name="MinColumnZ"/>.</param>
internal sealed record ProbeRequest(
  bool CenterOnSpawn,
  int MinColumnX,
  int MinColumnZ,
  int ColumnsX,
  int ColumnsZ,
  int TileColumns,
  IReadOnlyList<string> Codes,
  IReadOnlyList<bool> Recorded,
  bool Terrain
) {
  public const string Usage =
    "run <x|spawn> <z|spawn> <columnsX> <columnsZ> <tileColumns> <code...>; +code records positions, @terrain records heights and provinces";

  /// <summary>Parses <c>&lt;x|spawn&gt; &lt;z|spawn&gt; &lt;columnsX&gt; &lt;columnsZ&gt; &lt;tileColumns&gt;
  /// &lt;code...&gt;</c>. Columns are chunk columns (32 blocks); both corner words are <c>spawn</c> or
  /// both numbers; codes may hold <c>*</c> wildcards. A code written <c>+code</c> is recorded by
  /// position, the word <c>@terrain</c> is no code and turns the terrain file on. Returns null with
  /// the reason in <paramref name="error"/> when a count is not a positive whole number, a corner is
  /// not a number or <c>spawn</c>, or no code is given.</summary>
  public static ProbeRequest? Parse(string text, out string error) {
    string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    error = "";
    if (words.Length < 6) {
      error = $"Usage: {Usage}";
      return null;
    }
    bool spawn = words[0] == "spawn";
    int x = 0,
      z = 0;
    if (spawn != (words[1] == "spawn")) {
      error = "x and z are both spawn or both numbers.";
      return null;
    }
    if (!spawn && !(Int(words[0], out x) && Int(words[1], out z))) {
      error = "x and z must be whole numbers or spawn.";
      return null;
    }
    if (
      !(
        Int(words[2], out int columnsX)
        && Int(words[3], out int columnsZ)
        && Int(words[4], out int tile)
      )
      || columnsX < 1
      || columnsZ < 1
      || tile < 1
    ) {
      error =
        "columnsX, columnsZ and tileColumns must be positive whole numbers.";
      return null;
    }
    string[] rest = words.Skip(5).Where(w => w != "@terrain").ToArray();
    if (rest.Length == 0) {
      error = $"Usage: {Usage}";
      return null;
    }
    return new ProbeRequest(
      spawn,
      x,
      z,
      columnsX,
      columnsZ,
      tile,
      rest.Select(w => w.TrimStart('+')).ToArray(),
      rest.Select(w => w.StartsWith('+')).ToArray(),
      words.Skip(5).Contains("@terrain")
    );
  }

  private static bool Int(string word, out int value) =>
    int.TryParse(
      word,
      NumberStyles.Integer,
      CultureInfo.InvariantCulture,
      out value
    );

  /// <summary>The tiles that cover the rectangle from <paramref name="minX"/>, <paramref name="minZ"/>,
  /// row by row from the lowest Z, each at most <see cref="TileColumns"/> columns a side; the last
  /// of a row or column is clipped to the rectangle.</summary>
  public IReadOnlyList<ProbeTile> Tiles(int minX, int minZ) {
    var tiles = new List<ProbeTile>();
    for (int z = 0; z < ColumnsZ; z += TileColumns)
      for (int x = 0; x < ColumnsX; x += TileColumns)
        tiles.Add(
          new ProbeTile(
            minX + x,
            minZ + z,
            minX + Math.Min(x + TileColumns, ColumnsX) - 1,
            minZ + Math.Min(z + TileColumns, ColumnsZ) - 1
          )
        );
    return tiles;
  }
}
