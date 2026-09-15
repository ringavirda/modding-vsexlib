using System.Collections.Generic;
using ExpandedLib.Definitions;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>A legend symbol's registered settings: attach, hosted behaviours, boxes or a network port.</summary>
internal readonly record struct FillerGlyph(
  bool AllowAttach,
  IReadOnlyList<FillerBehaviorSpec>? Hosted = null,
  IReadOnlyList<Cuboidf>? Boxes = null,
  string? PortFace = null,
  string? PortNetwork = null
);

/// <summary>Authors a mega-block <c>fillerOffsets</c> footprint from ASCII diagrams, drawn as layers, slices or faces.</summary>
public sealed class FillerLayoutBuilder {
  // The two spellings of the principal marker are folded to this one before parsing.
  private const char AnchorGlyph = '0';

  private int _originA;
  private int _originB;
  private readonly SymbolLegend<FillerGlyph> _legend =
    new SymbolLegend<FillerGlyph>(DuplicatePolicy.Replace)
      .Map('#', new FillerGlyph(AllowAttach: false))
      .Map('+', new FillerGlyph(AllowAttach: true));
  private readonly List<(int Y, string Grid)> _layers = new();
  private readonly List<(int X, string Grid)> _slices = new();
  private readonly List<(int Z, string Grid)> _faces = new();

  /// <summary>Sets the top-left cell of every grid. Defaults to <c>(0, 0)</c>.</summary>
  public FillerLayoutBuilder Origin(int a, int b) {
    _originA = a;
    _originB = b;
    return this;
  }

  /// <summary>Registers a character as a plain filler cell (other blocks may not attach), overriding the
  /// default <c>'#'</c> mapping.</summary>
  public FillerLayoutBuilder Solid(char symbol) {
    _legend.Map(symbol, new FillerGlyph(AllowAttach: false));
    return this;
  }

  /// <summary>Registers a character as a filler other blocks may attach to (the JSON <c>allowAttach: true</c>),
  /// overriding the default <c>'+'</c> mapping.</summary>
  public FillerLayoutBuilder Attach(char symbol) {
    _legend.Map(symbol, new FillerGlyph(AllowAttach: true));
    return this;
  }

  /// <summary>Registers a character as an attach-allowing filler cell hosting <paramref name="behaviors"/> on the principal's behalf.</summary>
  public FillerLayoutBuilder Host(
    char symbol,
    params FillerBehaviorSpec[] behaviors
  ) {
    _legend.Map(symbol, new FillerGlyph(AllowAttach: true, Hosted: behaviors));
    return this;
  }

  /// <summary>Registers a character as a filler that fills only the half of its cell against <paramref name="half"/>.</summary>
  public FillerLayoutBuilder Slab(char symbol, BlockFacing half) {
    _legend.Map(
      symbol,
      new FillerGlyph(AllowAttach: false, Boxes: [FillerSlab.Half(half)])
    );
    return this;
  }

  /// <summary>Registers a character as a filler carrying a passive network port on <paramref name="face"/>.</summary>
  public FillerLayoutBuilder Port(
    char symbol,
    BlockFacing face,
    string networkType
  ) {
    _legend.Map(
      symbol,
      new FillerGlyph(
        AllowAttach: false,
        PortFace: face.Code[0].ToString(),
        PortNetwork: networkType
      )
    );
    return this;
  }

  /// <summary>Adds one horizontal Y-level grid. Layers may be declared in any Y order.</summary>
  public FillerLayoutBuilder Layer(int y, string grid) {
    _layers.Add((y, grid));
    return this;
  }

  /// <summary>Adds one vertical X-level grid. Slices may be declared in any X order.</summary>
  public FillerLayoutBuilder Slice(int x, string grid) {
    _slices.Add((x, grid));
    return this;
  }

  /// <summary>Adds one vertical Z-level grid. Faces may be declared in any Z order.</summary>
  public FillerLayoutBuilder Face(int z, string grid) {
    _faces.Add((z, grid));
    return this;
  }

  internal IReadOnlyList<FillerCellSpec> Build() {
    var options = new GridOptions(Anchor: AnchorGlyph);
    var drawn = new List<LayoutCell>();
    ThreePlaneDraw.Draw(
      _layers,
      _slices,
      _faces,
      _originA,
      _originB,
      options,
      drawn,
      FoldAnchorGlyph
    );

    var cells = new List<FillerCellSpec>();
    foreach (LayoutCell cell in drawn) {
      if (cell.X == 0 && cell.Y == 0 && cell.Z == 0)
        continue; // the principal occupies the origin, never a filler
      if (cell.Symbol == AnchorGlyph)
        throw new System.InvalidOperationException(
          $"Filler layout marks the principal ('{cell.Symbol}') at ({cell.X},{cell.Y},{cell.Z}), which is "
            + "not the origin (0,0,0) - check the grid's Origin offsets."
        );
      if (!_legend.TryGet(cell.Symbol, out FillerGlyph glyph))
        throw new System.InvalidOperationException(
          $"Filler layout symbol '{cell.Symbol}' at ({cell.X},{cell.Y},{cell.Z}) is not registered "
            + "(use Solid/Attach/Slab/Host, or '#'/'+')."
        );
      cells.Add(
        new FillerCellSpec(
          cell.X,
          cell.Y,
          cell.Z,
          glyph.AllowAttach,
          glyph.Hosted,
          glyph.Boxes,
          glyph.PortFace,
          glyph.PortNetwork
        )
      );
    }
    StructureFootprint.Validate(cells);
    return cells;
  }

  // 'O' is the other spelling of the principal marker, folded to '0' before CellGrid sees the grid.
  private static string FoldAnchorGlyph(string grid) =>
    grid.Replace('O', AnchorGlyph);
}
