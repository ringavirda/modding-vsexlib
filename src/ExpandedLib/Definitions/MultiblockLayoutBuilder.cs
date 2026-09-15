using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Definitions;

/// <summary>
/// Authors a <c>multiblockStructure</c> from ASCII grids: <see cref="Legend"/> maps characters to block
/// codes, drawn via <see cref="Layer"/>, <see cref="Slice"/> or <see cref="Face"/> grids. Block numbers
/// and offsets are generated and validated through <see cref="MultiblockBuilder"/>.
/// </summary>
public sealed class MultiblockLayoutBuilder {
  private int _originA;
  private int _originB;
  private readonly List<(char Symbol, string Code)> _legend = new();
  private readonly List<(int Y, string Grid)> _layers = new();
  private readonly List<(int X, string Grid)> _slices = new();
  private readonly List<(int Z, string Grid)> _faces = new();
  private readonly Dictionary<string, IReadOnlyList<int>> _facingSegment =
    new();
  private readonly Dictionary<char, List<CellRole>> _roleOf = new();
  private readonly Dictionary<char, List<string>> _connectorOf = new();
  private char? _core;
  private JObject? _roles;
  private JObject? _connectors;

  /// <summary>Sets the top-left origin <paramref name="xLeft"/>/<paramref name="zTop"/> for a
  /// <see cref="Layer"/>, reinterpreted per grid kind; defaults to <c>(0, 0)</c>.</summary>
  public MultiblockLayoutBuilder Origin(int xLeft, int zTop) {
    _originA = xLeft;
    _originB = zTop;
    return this;
  }

  /// <summary>Maps a grid character to a block code, wildcard or selector; a side-segment code is
  /// oriented and rotates with the structure.</summary>
  public MultiblockLayoutBuilder Legend(char symbol, string code) =>
    AddLegend(symbol, code, oriented: true);

  /// <summary>Like <see cref="Legend"/> but never rotates the code's side segment.</summary>
  public MultiblockLayoutBuilder LegendAnyFacing(char symbol, string code) =>
    AddLegend(symbol, code, oriented: false);

  /// <summary>Marks a glyph's cells with a semantic role, retrievable at runtime by role name.</summary>
  public MultiblockLayoutBuilder Role(char symbol, CellRole role) {
    if (!_roleOf.TryGetValue(symbol, out List<CellRole>? roles))
      _roleOf[symbol] = roles = [];
    if (!roles.Contains(role))
      roles.Add(role);
    return this;
  }

  /// <summary>Demands that a glyph's cells hold a block exposing a network connector on each of
  /// <paramref name="outward"/>.</summary>
  public MultiblockLayoutBuilder Connector(
    char symbol,
    params BlockFacing[] outward
  ) {
    if (!_connectorOf.TryGetValue(symbol, out List<string>? faces))
      _connectorOf[symbol] = faces = [];

    foreach (BlockFacing face in outward) {
      string letter = ExOrientation.TokenOf(face, asLetter: true);
      if (!faces.Contains(letter))
        faces.Add(letter);
    }
    return this;
  }

  /// <summary>Marks <paramref name="symbol"/> as the anchor: the block the player places, which must
  /// land on <c>(0,0,0)</c>.</summary>
  public MultiblockLayoutBuilder Core(char symbol) {
    _core = symbol;
    return this;
  }

  private MultiblockLayoutBuilder AddLegend(
    char symbol,
    string code,
    bool oriented
  ) {
    if (symbol is '.' or ' ')
      throw new ArgumentException(
        "'.' and space are reserved for empty cells and cannot be legend symbols."
      );
    if (_legend.Any(e => e.Symbol == symbol))
      throw new ArgumentException(
        $"Multiblock layout declares symbol '{symbol}' twice; a glyph maps to one code."
      );
    _legend.Add((symbol, code));
    if (oriented) {
      RefuseNetworkToken(symbol, code);
      IReadOnlyList<int> segments = FindOrientationSegments(code);
      if (segments.Count > 0)
        // Keyed by the full domained form: ToShortString() elides the `game` domain.
        _facingSegment[new AssetLocation(code).ToString()] = segments;
    }
    return this;
  }

  /// <summary>Refuses a legend code carrying a multi-letter direction token (<c>ns</c>, <c>nw</c>,
  /// <c>uns</c>, <c>nswe</c>); use <see cref="Connector"/> instead.</summary>
  private static void RefuseNetworkToken(char symbol, string code) {
    int colon = code.IndexOf(':');
    string path = colon >= 0 ? code[(colon + 1)..] : code;

    foreach (string part in path.Split('-')) {
      if (
        part.Length < 2
        || !ExOrientations.All.Any(s => s.Tokens.Contains(part))
      )
        continue;

      throw new InvalidOperationException(
        $"Multiblock layout pins symbol '{symbol}' to '{code}', whose '{part}' segment is a network "
          + "node's own orientation token. A network node takes its orientation from its neighbours, so "
          + "the pin can be contradicted at any time; mark the cell with Connector instead, or use "
          + "LegendAnyFacing if the code really is meant literally."
      );
    }
  }

  /// <summary>The indices of every dash-separated segment of <paramref name="code"/>'s path naming an
  /// orientation a Y rotation moves.</summary>
  internal static IReadOnlyList<int> FindOrientationSegments(string code) {
    var segmented = new ExOrientation.SegmentedCode(code);
    var found = new List<int>();
    for (int i = 0; i < segmented.Count; i++)
      if (ExOrientation.RotatesUnderY(segmented[i]))
        found.Add(i);
    return found;
  }

  /// <summary>Adds one horizontal Y-level grid (a floor plan; rows run +Z, columns +X).</summary>
  public MultiblockLayoutBuilder Layer(int y, string grid) {
    _layers.Add((y, grid));
    return this;
  }

  /// <summary>Adds one vertical X-level grid (a front elevation; rows run down in -Y from the top,
  /// columns run +Z).</summary>
  public MultiblockLayoutBuilder Slice(int x, string grid) {
    _slices.Add((x, grid));
    return this;
  }

  /// <summary>Adds one vertical Z-level grid (a front elevation looking along -Z; rows run down in -Y
  /// from the top, columns run +X).</summary>
  public MultiblockLayoutBuilder Face(int z, string grid) {
    _faces.Add((z, grid));
    return this;
  }

  /// <summary>The oriented-legend table for <c>attributes.multiblockFacings</c>: block code to the
  /// dash-segment indices of every orientation a Y rotation moves.</summary>
  internal JObject? BuildFacings() {
    if (_facingSegment.Count == 0)
      return null;
    var o = new JObject();
    foreach ((string code, IReadOnlyList<int> segments) in _facingSegment)
      o[code] = new JArray(segments);
    return o;
  }

  /// <summary>The cell-role table for <c>attributes.multiblockRoles</c>, computed by
  /// <see cref="Build"/>.</summary>
  internal JObject? BuildRoles() => _roles;

  /// <summary>The connector table for <c>attributes.multiblockConnectors</c>, computed by
  /// <see cref="Build"/>.</summary>
  internal JObject? BuildConnectors() => _connectors;

  internal JObject Build() {
    var mb = new MultiblockBuilder();
    // Numbers are keyed by code, not by glyph, handed out in legend-declaration order.
    var wOfCode = new Dictionary<string, int>(StringComparer.Ordinal);
    var wOf = new Dictionary<char, int>();
    int w = 1;
    foreach ((char symbol, string code) in _legend) {
      if (!wOfCode.TryGetValue(code, out int cellW))
        wOfCode[code] = cellW = w++;
      wOf[symbol] = cellW;
      mb.Number(code, cellW);
    }

    ValidateRoles(wOf);
    if (_core is char core && !wOf.ContainsKey(core))
      throw new InvalidOperationException(
        $"Multiblock layout marks '{core}' as the anchor but has no Legend entry for it."
      );

    var roleCells = new Dictionary<CellRole, JArray>();
    var connectorCells = new Dictionary<string, JArray>();
    var drawn = new HashSet<char>();
    (int X, int Y, int Z)? anchor = DrawnCells(out List<LayoutCell> cells);
    foreach (LayoutCell cell in cells) {
      if (!wOf.TryGetValue(cell.Symbol, out int cellW))
        throw new InvalidOperationException(
          $"Multiblock layout uses symbol '{cell.Symbol}' at ({cell.X},{cell.Y},{cell.Z}) with no Legend entry."
        );
      mb.At(cell.X, cell.Y, cell.Z, cellW);
      drawn.Add(cell.Symbol);

      if (_core == cell.Symbol)
        anchor ??= (cell.X, cell.Y, cell.Z);

      if (_roleOf.TryGetValue(cell.Symbol, out List<CellRole>? roles))
        foreach (CellRole role in roles)
          RoleArray(roleCells, role)
            .Add(
              new JObject {
                ["x"] = cell.X,
                ["y"] = cell.Y,
                ["z"] = cell.Z,
              }
            );

      if (_connectorOf.TryGetValue(cell.Symbol, out List<string>? faces))
        foreach (string face in faces)
          FaceArray(connectorCells, face)
            .Add(
              new JObject {
                ["x"] = cell.X,
                ["y"] = cell.Y,
                ["z"] = cell.Z,
              }
            );
    }

    // Checked per glyph, not per role: a shared role needs every glyph drawn.
    foreach ((char symbol, List<CellRole> roles) in _roleOf)
      if (!drawn.Contains(symbol))
        throw new InvalidOperationException(
          $"Multiblock layout gives symbol '{symbol}' the role {string.Join(", ", roles)} but never draws it "
            + "in any Layer, so the role would resolve to nothing at runtime."
        );

    // An undrawn connector glyph leaves the structure with no facing demand.
    foreach ((char symbol, List<string> faces) in _connectorOf)
      if (!drawn.Contains(symbol))
        throw new InvalidOperationException(
          $"Multiblock layout demands symbol '{symbol}' open to {string.Join(", ", faces)} but never draws "
            + "it in any Layer, so the demand would resolve to nothing at runtime."
        );

    if (_core is char coreSymbol) {
      if (anchor is null)
        throw new InvalidOperationException(
          $"Multiblock layout marks '{coreSymbol}' as the anchor but never draws it in any Layer."
        );
      if (anchor.Value != (0, 0, 0))
        throw new InvalidOperationException(
          $"Multiblock layout declares Origin({_originA},{_originB}) but anchor '{coreSymbol}' lands at "
            + $"({anchor.Value.X},{anchor.Value.Y},{anchor.Value.Z}), not (0,0,0) - Origin should be "
            + $"({_originA - anchor.Value.X},{_originB - anchor.Value.Z})."
        );
    }

    ValidateRoleArity(roleCells);

    _roles = EmitRoles(roleCells);
    _connectors = EmitConnectors(connectorCells);
    return mb.Build();
  }

  /// <summary>Draws every declared <see cref="Layer"/>, <see cref="Slice"/> and <see cref="Face"/> grid
  /// into <paramref name="cells"/> and returns the <see cref="Core"/> glyph's position, or null.</summary>
  private (int X, int Y, int Z)? DrawnCells(out List<LayoutCell> cells) {
    var options = new GridOptions(Anchor: _core);
    cells = new List<LayoutCell>();
    return ThreePlaneDraw.Draw(
      _layers,
      _slices,
      _faces,
      _originA,
      _originB,
      options,
      cells
    );
  }

  /// <summary>Enforces <see cref="CellRole.IsSingle"/>: a single-cell role must be drawn exactly
  /// once.</summary>
  private static void ValidateRoleArity(Dictionary<CellRole, JArray> roleCells) {
    foreach ((CellRole role, JArray cells) in roleCells)
      if (CellRoles.IsSingleCell(role) && cells.Count != 1)
        throw new InvalidOperationException(
          $"Multiblock layout draws {cells.Count} cells with the role {role}, which is a single-cell role; "
            + "exactly one cell may carry it."
        );
  }

  /// <summary>Validates that every role and connector glyph has a <see cref="Legend"/> entry.</summary>
  private void ValidateRoles(Dictionary<char, int> wOf) {
    foreach ((char symbol, List<CellRole> roles) in _roleOf)
      if (!wOf.ContainsKey(symbol))
        throw new InvalidOperationException(
          $"Multiblock layout gives symbol '{symbol}' the role {string.Join(", ", roles)} but has no Legend "
            + "entry for it."
        );

    foreach ((char symbol, List<string> faces) in _connectorOf)
      if (!wOf.ContainsKey(symbol))
        throw new InvalidOperationException(
          $"Multiblock layout demands symbol '{symbol}' open to {string.Join(", ", faces)} but has no Legend "
            + "entry for it."
        );
  }

  private static JArray FaceArray(
    Dictionary<string, JArray> connectorCells,
    string face
  ) {
    if (!connectorCells.TryGetValue(face, out JArray? array))
      connectorCells[face] = array = new JArray();
    return array;
  }

  /// <summary>Serialises the collected connector cells: faces in <c>n e s w u d</c> order, each face's
  /// cells in drawing order.</summary>
  private static JObject? EmitConnectors(
    Dictionary<string, JArray> connectorCells
  ) {
    if (connectorCells.Count == 0)
      return null;
    var o = new JObject();
    foreach (string face in FaceOrder)
      if (connectorCells.TryGetValue(face, out JArray? cells))
        o[face] = cells;
    return o;
  }

  private static readonly string[] FaceOrder = ["n", "e", "s", "w", "u", "d"];

  private static JArray RoleArray(
    Dictionary<CellRole, JArray> roleCells,
    CellRole role
  ) {
    if (!roleCells.TryGetValue(role, out JArray? array))
      roleCells[role] = array = new JArray();
    return array;
  }

  /// <summary>Serialises the collected role cells: roles sorted by key (ordinal), each role's cells in
  /// drawing order.</summary>
  private static JObject? EmitRoles(Dictionary<CellRole, JArray> roleCells) {
    if (roleCells.Count == 0)
      return null;
    var o = new JObject();
    foreach (
      CellRole role in roleCells.Keys.OrderBy(
        r => r.Key,
        StringComparer.Ordinal
      )
    )
      o[role.ToString()] = roleCells[role];
    return o;
  }
}
