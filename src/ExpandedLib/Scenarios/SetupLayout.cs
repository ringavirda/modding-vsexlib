using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Scenarios;

/// <summary>The five kinds of legend entry (setups.md, The legend).</summary>
internal enum LayoutEntryKind {
  Block,
  Run,
  Cells,
  Port,
  Open,
}

/// <summary>One <c>apart</c> item: the cell at (X, Y, Z) and its neighbour across <paramref name="Face"/> stay unjoined.</summary>
internal sealed record LayoutApart(int X, int Y, int Z, BlockFacing Face);

/// <summary>
/// A legend entry. <see cref="Target"/> is the id a <c>cells</c> or <c>port</c> entry names;
/// <see cref="Body"/> is the entry as written, for the keys left to the harness.
/// </summary>
internal sealed record LayoutEntry(
  char Glyph,
  LayoutEntryKind Kind,
  string? Id,
  string? Code,
  string? Run,
  string? Target,
  IReadOnlyList<string> Family,
  IReadOnlyList<LayoutApart> Apart,
  (int X, int Y, int Z)? From,
  JObject Body
);

/// <summary>A cell of a run with the faces it joins to cells of its own run (rule 1) and the
/// faces it opens toward a <c>port</c> or <c>open</c> cell (rule 3), each in
/// <see cref="BlockFacing.ALLFACES"/> order.</summary>
internal sealed record LayoutRunCell(
  LayoutCell Cell,
  LayoutEntry Entry,
  IReadOnlyList<BlockFacing> Joins,
  IReadOnlyList<BlockFacing> Opens
);

/// <summary>A run: its entries in legend order and its cells in path order.</summary>
internal sealed record LayoutRun(
  string Id,
  IReadOnlyList<LayoutEntry> Entries,
  IReadOnlyList<LayoutRunCell> Cells
);

/// <summary>
/// A setup file's drawing (setups.md, The layout): entries in legend order, cells in reading order,
/// runs with their joins and path order. Rule 2 faces and family shapes are the harness reader's.
/// </summary>
internal sealed class SetupLayout {
  private static readonly Regex PipeStraight = new(
    "^(?<domain>[^:]+):pipe-straight-[neswud]+-(?<material>.+)$",
    RegexOptions.CultureInvariant
  );

  private static readonly string[] Shapes =
  [
    "straight",
    "bend",
    "tjunction",
    "xjunction",
  ];

  private readonly Dictionary<char, LayoutEntry> _byGlyph = [];
  private readonly Dictionary<(int X, int Y, int Z), char> _drawn = [];
  private readonly SetupJson.Where _where;
  private readonly bool _checkPorts;

  private SetupLayout(
    (int X, int Z) origin,
    IReadOnlyList<LayoutEntry> entries,
    Dictionary<(int X, int Y, int Z), char> drawn,
    SetupJson.Where where,
    bool checkPorts = true
  ) {
    Origin = origin;
    Entries = entries;
    _where = where;
    _checkPorts = checkPorts;
    foreach (LayoutEntry entry in entries)
      _byGlyph[entry.Glyph] = entry;
    foreach (var (pos, glyph) in drawn)
      _drawn[pos] = glyph;

    Cells =
    [
      .. _drawn
        .OrderBy(kv => -kv.Key.Y)
        .ThenBy(kv => kv.Key.Z)
        .ThenBy(kv => kv.Key.X)
        .Select(kv => new LayoutCell(kv.Key.X, kv.Key.Y, kv.Key.Z, kv.Value)),
    ];
    Validate();
    Runs = BuildRuns();
  }

  /// <summary>The <c>origin</c>: the plot-local x and z of the first glyph of a layer's first row.</summary>
  public (int X, int Z) Origin { get; }

  /// <summary>The legend's entries in legend order, which is the placement order.</summary>
  public IReadOnlyList<LayoutEntry> Entries { get; }

  /// <summary>Every drawn cell, air left out, layers from the highest down, rows top to bottom, each row left to right.</summary>
  public IReadOnlyList<LayoutCell> Cells { get; }

  /// <summary>The runs in the legend order of their first entry; a run with no cell is left out.</summary>
  public IReadOnlyList<LayoutRun> Runs { get; }

  /// <summary>The entry whose glyph is drawn in <paramref name="cell"/>.</summary>
  public LayoutEntry EntryOf(LayoutCell cell) => _byGlyph[cell.Symbol];

  /// <summary>
  /// Reads a <c>layout</c> object. <paramref name="variantLayers"/> are the <c>layers</c> overlays of
  /// the file's variants; a legend key is used when the main case or any of them draws it.
  /// </summary>
  /// <exception cref="SetupFileException">The layout breaks a rule of setups.md, What the reader refuses.</exception>
  public static SetupLayout Parse(
    JObject layout,
    SetupJson.Where where,
    IEnumerable<(JObject Layers, SetupJson.Where Where)> variantLayers
  ) {
    SetupJson.RefuseUnknown(layout, where, "origin", "legend", "layers");
    (int X, int Z) origin = (0, 0);
    if (layout["origin"] != null) {
      SetupJson.Where ow = where.In("origin");
      JArray o = SetupJson.Array(layout["origin"], ow);
      if (o.Count != 2)
        throw ow.Fail("expected [x, z].");
      origin = (
        SetupJson.Integer(o[0], ow.At(0)),
        SetupJson.Integer(o[1], ow.At(1))
      );
    }

    var legend = new SymbolLegend<LayoutEntry>(DuplicatePolicy.Throw);
    var entries = new List<LayoutEntry>();
    if (layout["legend"] != null) {
      SetupJson.Where lw = where.In("legend");
      foreach (
        JProperty p in SetupJson.Object(layout["legend"], lw).Properties()
      ) {
        SetupJson.Where ew = lw.In(p.Name);
        if (p.Name.Length != 1 || !IsGlyph(p.Name[0]))
          throw ew.Fail(
            "a legend key is one printable ASCII glyph other than space, \".\", a quote or a backslash."
          );
        LayoutEntry entry = ParseEntry(p.Name[0], p.Value, ew);
        legend.Map(entry.Glyph, entry);
        entries.Add(entry);
      }
    }

    CellGrid grid = Draw(layout["layers"], origin, legend, where.In("layers"));
    var overlays = new List<CellGrid>();
    foreach (var (layers, layersWhere) in variantLayers)
      overlays.Add(Draw(layers, origin, legend, layersWhere));
    foreach (char glyph in legend.Unused(grid))
      if (!overlays.Any(g => g.Drawn(glyph)))
        throw where
          .In("legend")
          .In(glyph.ToString())
          .Fail("no layer of the main case or a variant draws this glyph.");

    var drawn = new Dictionary<(int X, int Y, int Z), char>();
    foreach (LayoutCell c in grid.Cells)
      if (c.Symbol is not (' ' or '.'))
        drawn[(c.X, c.Y, c.Z)] = c.Symbol;
    return new SetupLayout(origin, entries, drawn, where);
  }

  /// <summary>
  /// The drawing of a variant: this layout with the variant's <c>layers</c> laid over it, then its
  /// <c>replace</c> entries swapped in and its <c>drop</c> ids removed, and every rule checked again.
  /// Ids that are not this layout's entries are left to the steps.
  /// </summary>
  /// <exception cref="SetupFileException">The drawing breaks a rule of setups.md, What the reader refuses.</exception>
  public SetupLayout ForVariant(JObject variant, SetupJson.Where where) {
    var drawn = new Dictionary<(int X, int Y, int Z), char>(_drawn);
    if (variant["layers"] != null) {
      SetupJson.Where lw = where.In("layers");
      var legend = new SymbolLegend<LayoutEntry>(DuplicatePolicy.Replace);
      foreach (LayoutEntry e in Entries)
        legend.Map(e.Glyph, e);
      CellGrid overlay = Draw(variant["layers"], Origin, legend, lw);
      foreach (LayoutCell c in overlay.Cells)
        switch (c.Symbol) {
          case ' ':
            break;
          case '.':
            drawn.Remove((c.X, c.Y, c.Z));
            break;
          default:
            drawn[(c.X, c.Y, c.Z)] = c.Symbol;
            break;
        }
    }
    SetupLayout result = new(Origin, Entries, drawn, where);
    if (variant["replace"] is JObject replace)
      foreach (JProperty p in replace.Properties())
        if (p.Value is JObject body)
          result = result.Replacing(
            p.Name,
            body,
            where.In("replace").In(p.Name)
          );
        else if (Entries.Any(e => e.Id == p.Name))
          throw where
            .In("replace")
            .In(p.Name)
            .Fail("expected a legend entry object.");
    if (variant["drop"] is JArray drop)
      result = result.Without(
        drop.Select(t => (string?)t).OfType<string>().ToList()
      );
    return result;
  }

  /// <summary>
  /// Refuses an entry with an <c>id</c> that none of this drawing and <paramref name="variants"/>
  /// (the results of <see cref="ForVariant"/>) draws.
  /// </summary>
  /// <exception cref="SetupFileException">An entry with an id is drawn in no drawing.</exception>
  public void RequireDrawn(IEnumerable<SetupLayout> variants) {
    SetupLayout[] drawings = [this, .. variants];
    foreach (LayoutEntry e in Entries.Where(e => e.Id != null))
      if (!drawings.Any(d => d.Cells.Any(c => c.Symbol == e.Glyph)))
        throw LegendWhere(e)
          .Fail(
            $"\"{e.Id}\" is drawn in no drawing; an entry with an id is drawn once in one."
          );
  }

  private SetupLayout Replacing(string id, JObject body, SetupJson.Where where) {
    LayoutEntry? old = Entries.FirstOrDefault(e => e.Id == id);
    if (old == null)
      return this;
    JObject copy = (JObject)body.DeepClone();
    bool places =
      copy["cells"] == null && copy["port"] == null && copy["open"] == null;
    if (places && copy["id"] == null)
      copy["id"] = id;
    LayoutEntry entry = ParseEntry(old.Glyph, copy, where);
    return new SetupLayout(
      Origin,
      [.. Entries.Select(e => e.Glyph == old.Glyph ? entry : e)],
      _drawn,
      _where,
      _checkPorts
    );
  }

  private SetupLayout Without(IReadOnlyList<string> ids) {
    var gone = new HashSet<char>(
      Entries
        .Where(e =>
          (e.Id != null && ids.Contains(e.Id))
          || (e.Run != null && ids.Contains(e.Run))
          || (e.Target != null && ids.Contains(e.Target))
        )
        .Select(e => e.Glyph)
    );
    return new SetupLayout(
      Origin,
      [.. Entries.Where(e => !gone.Contains(e.Glyph))],
      _drawn
        .Where(kv => !gone.Contains(kv.Value))
        .ToDictionary(kv => kv.Key, kv => kv.Value),
      _where,
      false
    );
  }

  private static bool IsGlyph(char c) =>
    c > ' ' && c < '\x7f' && c is not ('"' or '\\' or '.');

  private static CellGrid Draw(
    JToken? layers,
    (int X, int Z) origin,
    SymbolLegend<LayoutEntry> legend,
    SetupJson.Where where
  ) {
    var grid = new CellGrid(
      GridPlane.Horizontal,
      origin.X,
      origin.Z,
      new GridOptions(SpaceAdvancesColumn: true, Empty: '\0') {
        KeepBlankRows = true,
      }
    );
    if (layers == null)
      return grid;
    int? previous = null;
    foreach (JProperty layer in SetupJson.Object(layers, where).Properties()) {
      SetupJson.Where lw = where.In(layer.Name);
      if (
        !int.TryParse(
          layer.Name,
          NumberStyles.AllowLeadingSign,
          CultureInfo.InvariantCulture,
          out int y
        )
      )
        throw lw.Fail($"the layer key \"{layer.Name}\" is not a whole number.");
      if (previous != null && y >= previous)
        throw lw.Fail(
          $"layer {y} follows layer {previous}; layers are written from the highest down."
        );
      previous = y;
      JArray rows = SetupJson.Array(layer.Value, lw);
      var lines = new List<string>();
      for (int r = 0; r < rows.Count; r++) {
        SetupJson.Where rw = lw.At(r);
        string row = SetupJson.String(rows[r], rw);
        for (int c = 0; c < row.Length; c++) {
          char ch = row[c];
          if (ch == '\t')
            throw rw.Fail($"column {c} holds a tab.");
          if (ch is ' ' or '.')
            continue;
          if (!IsGlyph(ch))
            throw rw.Fail(
              $"column {c} holds a character that is not a printable ASCII glyph."
            );
          if (!legend.TryGet(ch, out _))
            throw rw.Fail(
              $"glyph '{ch}' at column {c} (cell ({origin.X + c}, {y}, {origin.Z + r})) is in no legend entry."
            );
        }
        lines.Add(row);
      }
      grid.Add(y, string.Join("\n", lines));
    }
    return grid;
  }

  private static LayoutEntry ParseEntry(
    char glyph,
    JToken token,
    SetupJson.Where where
  ) {
    JObject e = SetupJson.Object(token, where);
    string[] kinds = ["run", "cells", "port", "open"];
    string[] named = [.. kinds.Where(k => e[k] != null)];
    if (named.Length > 1)
      throw where.Fail(
        $"takes one of run, cells, port or open, not {string.Join(" and ", named)}."
      );
    string? Opt(string key) =>
      e[key] == null ? null : SetupJson.String(e[key], where.In(key));
    string Required(string key) =>
      e[key] == null
        ? throw where.Fail($"needs \"{key}\".")
        : SetupJson.String(e[key], where.In(key));
    var none = new List<LayoutApart>();
    if (named.Length == 0) {
      if (e["code"] == null)
        throw where.Fail("is no block, run, cells, port or open entry.");
      SetupJson.RefuseUnknown(
        e,
        where,
        "id",
        "code",
        "complete",
        "materials",
        "tree"
      );
      return new LayoutEntry(
        glyph,
        LayoutEntryKind.Block,
        Opt("id"),
        Required("code"),
        null,
        null,
        [],
        none,
        null,
        e
      );
    }
    switch (named[0]) {
      case "cells":
      case "port":
        SetupJson.RefuseUnknown(e, where, named[0]);
        return new LayoutEntry(
          glyph,
          named[0] == "cells" ? LayoutEntryKind.Cells : LayoutEntryKind.Port,
          null,
          null,
          null,
          Required(named[0]),
          [],
          none,
          null,
          e
        );
      case "open":
        SetupJson.RefuseUnknown(e, where, "open");
        if (!SetupJson.Bool(e["open"], where.In("open")))
          throw where.In("open").Fail("expected true.");
        return new LayoutEntry(
          glyph,
          LayoutEntryKind.Open,
          null,
          null,
          null,
          null,
          [],
          none,
          null,
          e
        );
      default:
        return ParseRun(glyph, e, where, Required, Opt);
    }
  }

  /// <summary>The wildcards of the codes a run's shapes come from: <paramref name="family"/> as
  /// written, or for a straight pipe code the four shapes of its material.</summary>
  /// <param name="code">The run's straight segment.</param>
  /// <param name="family">The <c>family</c> key, a string or an array, or null.</param>
  /// <param name="where">The entry or step the key belongs to.</param>
  /// <param name="owner">What the message calls the owner: <c>entry</c> or <c>step</c>.</param>
  internal static IReadOnlyList<string> RunFamily(
    string code,
    JToken? family,
    SetupJson.Where where,
    string owner
  ) {
    if (family == null) {
      Match m = PipeStraight.Match(code);
      if (!m.Success)
        throw where.Fail(
          $"the code \"{code}\" is not a straight pipe, so the {owner} needs \"family\"."
        );
      return
      [
        .. Shapes.Select(s =>
          $"{m.Groups["domain"].Value}:pipe-{s}-*-{m.Groups["material"].Value}"
        ),
      ];
    }
    return family is JArray
      ? SetupJson.Strings(family, where.In("family"))
      : [SetupJson.String(family, where.In("family"))];
  }

  private static LayoutEntry ParseRun(
    char glyph,
    JObject e,
    SetupJson.Where where,
    Func<string, string> required,
    Func<string, string?> opt
  ) {
    SetupJson.RefuseUnknown(
      e,
      where,
      "run",
      "code",
      "family",
      "id",
      "apart",
      "from"
    );
    string run = required("run");
    string code = required("code");
    IReadOnlyList<string> family = RunFamily(code, e["family"], where, "entry");

    var apart = new List<LayoutApart>();
    if (e["apart"] != null) {
      SetupJson.Where aw = where.In("apart");
      JArray list = SetupJson.Array(e["apart"], aw);
      for (int i = 0; i < list.Count; i++) {
        SetupJson.Where iw = aw.At(i);
        JObject item = SetupJson.Object(list[i], iw);
        SetupJson.RefuseUnknown(item, iw, "at", "face");
        (int X, int Y, int Z) at = Cell(item["at"], iw.In("at"));
        string face = SetupJson.String(item["face"], iw.In("face"));
        BlockFacing facing =
          FaceOf(face)
          ?? throw iw.In("face")
            .Fail($"\"{face}\" is not one of n, e, s, w, u, d.");
        apart.Add(new LayoutApart(at.X, at.Y, at.Z, facing));
      }
    }
    (int X, int Y, int Z)? from =
      e["from"] == null ? null : Cell(e["from"], where.In("from"));
    return new LayoutEntry(
      glyph,
      LayoutEntryKind.Run,
      opt("id"),
      code,
      run,
      null,
      family,
      apart,
      from,
      e
    );
  }

  private static (int X, int Y, int Z) Cell(
    JToken? token,
    SetupJson.Where where
  ) {
    JArray a = SetupJson.Array(token, where);
    if (a.Count != 3)
      throw where.Fail("expected [x, y, z].");
    return (
      SetupJson.Integer(a[0], where.At(0)),
      SetupJson.Integer(a[1], where.At(1)),
      SetupJson.Integer(a[2], where.At(2))
    );
  }

  internal static BlockFacing? FaceOf(string letter) =>
    letter switch {
      "n" => BlockFacing.NORTH,
      "e" => BlockFacing.EAST,
      "s" => BlockFacing.SOUTH,
      "w" => BlockFacing.WEST,
      "u" => BlockFacing.UP,
      "d" => BlockFacing.DOWN,
      _ => null,
    };

  private static (int X, int Y, int Z) Step(
    (int X, int Y, int Z) p,
    BlockFacing f
  ) => (p.X + f.Normali.X, p.Y + f.Normali.Y, p.Z + f.Normali.Z);

  private static string Describe((int X, int Y, int Z) p) =>
    $"cell ({p.X}, {p.Y}, {p.Z})";

  private SetupJson.Where LegendWhere(LayoutEntry e) =>
    _where.In("legend").In(e.Glyph.ToString());

  private LayoutEntry? EntryAt((int X, int Y, int Z) p) =>
    _drawn.TryGetValue(p, out char g) ? _byGlyph[g] : null;

  private void Validate() {
    var blockIds = new HashSet<string>(
      Entries
        .Where(e => e.Kind == LayoutEntryKind.Block && e.Id != null)
        .Select(e => e.Id!)
    );
    foreach (LayoutEntry e in Entries) {
      if (e.Id != null) {
        var at = Cells.Where(c => c.Symbol == e.Glyph).ToList();
        if (at.Count > 1)
          throw LegendWhere(e)
            .Fail(
              $"\"{e.Id}\" is drawn {at.Count} times, at {string.Join(", ", at.Select(c => Describe((c.X, c.Y, c.Z))))}; an entry with an id is drawn once in a drawing."
            );
      }
      if (e.Target != null && !blockIds.Contains(e.Target))
        throw LegendWhere(e)
          .Fail($"\"{e.Target}\" is the id of no block entry.");
    }
    foreach (LayoutCell c in Cells.Where(_ => _checkPorts)) {
      LayoutEntry e = EntryOf(c);
      if (e.Kind is not (LayoutEntryKind.Port or LayoutEntryKind.Open))
        continue;
      var p = (c.X, c.Y, c.Z);
      if (
        !BlockFacing.ALLFACES.Any(f =>
          EntryAt(Step(p, f))?.Kind == LayoutEntryKind.Run
        )
      )
        throw _where.Fail(
          $"the {e.Kind.ToString().ToLowerInvariant()} {Describe(p)} (legend \"{e.Glyph}\") is beside no run cell."
        );
    }
  }

  private List<LayoutRun> BuildRuns() {
    var runs = new List<LayoutRun>();
    foreach (
      IGrouping<string, LayoutEntry> group in Entries
        .Where(e => e.Kind == LayoutEntryKind.Run)
        .GroupBy(e => e.Run!)
    ) {
      var glyphs = new HashSet<char>(group.Select(e => e.Glyph));
      List<LayoutCell> cells = [.. Cells.Where(c => glyphs.Contains(c.Symbol))];
      if (cells.Count == 0)
        continue;
      foreach (LayoutEntry e in group.Skip(1).Where(e => e.From != null))
        throw LegendWhere(e)
          .In("from")
          .Fail("from sits on the run's first entry.");
      runs.Add(
        new LayoutRun(
          group.Key,
          [.. group],
          WalkRun(group.Key, [.. group], cells)
        )
      );
    }
    return runs;
  }

  private List<LayoutRunCell> WalkRun(
    string run,
    List<LayoutEntry> entries,
    List<LayoutCell> cells
  ) {
    var inRun = new HashSet<(int X, int Y, int Z)>(
      cells.Select(c => (c.X, c.Y, c.Z))
    );
    var blocked = new HashSet<((int X, int Y, int Z), BlockFacing)>();
    foreach (LayoutEntry entry in entries)
      for (int i = 0; i < entry.Apart.Count; i++) {
        LayoutApart a = entry.Apart[i];
        SetupJson.Where aw = LegendWhere(entry).In("apart").At(i);
        var at = (a.X, a.Y, a.Z);
        if (EntryAt(at) != entry)
          throw aw.Fail(
            $"{Describe(at)} is not drawn with this entry's glyph."
          );
        var across = Step(at, a.Face);
        if (!inRun.Contains(across))
          throw aw.Fail(
            $"{Describe(at)} has no join to {Describe(across)} in run \"{run}\"."
          );
        blocked.Add((at, a.Face));
        blocked.Add((across, a.Face.Opposite));
      }

    var joins = new Dictionary<(int X, int Y, int Z), List<BlockFacing>>();
    foreach (LayoutCell c in cells) {
      var p = (c.X, c.Y, c.Z);
      joins[p] =
      [
        .. BlockFacing.ALLFACES.Where(f =>
          inRun.Contains(Step(p, f)) && !blocked.Contains((p, f))
        ),
      ];
    }

    (int X, int Y, int Z) start = (cells[0].X, cells[0].Y, cells[0].Z);
    LayoutEntry first = entries[0];
    if (first.From != null) {
      if (!inRun.Contains(first.From.Value))
        throw LegendWhere(first)
          .In("from")
          .Fail($"{Describe(first.From.Value)} is no cell of run \"{run}\".");
      start = first.From.Value;
    } else {
      foreach (LayoutCell c in cells)
        if (joins[(c.X, c.Y, c.Z)].Count == 1) {
          start = (c.X, c.Y, c.Z);
          break;
        }
    }

    var order = new List<(int X, int Y, int Z)>();
    var seen = new HashSet<(int X, int Y, int Z)>();
    void Visit((int X, int Y, int Z) p) {
      seen.Add(p);
      order.Add(p);
      foreach (BlockFacing f in joins[p]) {
        var q = Step(p, f);
        if (!seen.Contains(q))
          Visit(q);
      }
    }
    Visit(start);
    if (order.Count != cells.Count) {
      var lost = cells.First(c => !seen.Contains((c.X, c.Y, c.Z)));
      throw _where.Fail(
        $"run \"{run}\" is not one connected piece: {Describe((lost.X, lost.Y, lost.Z))} does not join {Describe(start)}."
      );
    }

    return
    [
      .. order.Select(p =>
      {
        var cell = new LayoutCell(p.X, p.Y, p.Z, _drawn[p]);
        return new LayoutRunCell(
          cell,
          _byGlyph[cell.Symbol],
          joins[p],
          [
            .. BlockFacing.ALLFACES.Where(f =>
              EntryAt(Step(p, f))?.Kind
                is LayoutEntryKind.Port
                  or LayoutEntryKind.Open
            ),
          ]
        );
      }),
    ];
  }
}
