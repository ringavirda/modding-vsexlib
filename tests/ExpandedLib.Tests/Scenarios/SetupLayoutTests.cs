using System.IO;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios;

/// <summary>What a setup file's layout reads as, and each drawing the reader refuses with a message
/// naming where it is wrong (setups.md, The layout and What the reader refuses).</summary>
public class SetupLayoutTests {
  private const string File = "plant.json";

  #region Fixtures

  private static SetupJson.Where Where => new(File, "layout");

  private static SetupLayout Load(string layout) =>
    SetupLayout.Parse(JObject.Parse(layout), Where, []);

  private static string Refused(string layout) =>
    Assert.Throws<SetupFileException>(() => Load(layout)).Message;

  private static string Cells(SetupLayout layout) =>
    string.Join(
      " ",
      layout.Cells.Select(c => $"{c.Symbol}({c.X},{c.Y},{c.Z})")
    );

  private static string Path(LayoutRun run) =>
    string.Join(
      " ",
      run.Cells.Select(c => $"({c.Cell.X},{c.Cell.Y},{c.Cell.Z})")
    );

  private static string Letters(
    System.Collections.Generic.IEnumerable<BlockFacing> faces
  ) => string.Concat(faces.Select(f => f.Code[0]));

  private static JObject Pilot =>
    JObject.Parse(
      System.IO.File.ReadAllText(
        System.IO.Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Scenarios",
          "Fixtures",
          "starter-pilot.json"
        )
      )
    );

  private static SetupLayout LoadPilot() {
    JObject root = Pilot;
    var overlays = ((JObject)root["variants"]!)
      .Properties()
      .Where(v => v.Value["layers"] != null)
      .Select(v => ((JObject)v.Value["layers"]!, Where.In(v.Name)));
    return SetupLayout.Parse((JObject)root["layout"]!, Where, overlays);
  }

  private const string Pipe = "\"code\": \"x:pipe-straight-we-iron\"";

  #endregion

  #region The starter pilot

  // Fails when a run's cells are not read in the plant's path order: the end first in reading
  // order, then depth first with joins taken north, east, south, west, up, down.
  [Fact]
  public void The_starter_pilot_reads_to_its_runs_in_the_plants_order() {
    SetupLayout layout = LoadPilot();

    LayoutRun steam = layout.Runs.Single(r => r.Id == "steam");
    LayoutRun pond = layout.Runs.Single(r => r.Id == "pond");
    LayoutRun feed = layout.Runs.Single(r => r.Id == "feed");
    Assert.Equal(
      "(0,10,-2) (1,10,-2) (2,10,-2) (2,9,-2) (2,8,-2) (3,8,-2)",
      Path(steam)
    );
    Assert.Equal("(3,7,-5)", Path(pond));
    Assert.Equal(
      "(4,8,-3) (4,8,-4) (4,8,-5) (4,8,-6) (3,8,-6) (2,8,-6) (1,8,-6) (1,7,-6) (2,7,-6) "
        + string.Join(" ", Enumerable.Range(-5, 8).Select(z => $"(2,7,{z})"))
        + " (2,7,3) (1,7,3) (0,7,3) (0,7,2) (0,7,1) (0,7,0)",
      Path(feed)
    );
    Assert.Equal(6, steam.Cells.Count);
    Assert.Equal(1, pond.Cells.Count);
    Assert.Equal(23, feed.Cells.Count);
  }

  // Fails when the legend's order is not kept: it is the placement order.
  [Fact]
  public void The_starter_pilot_keeps_its_legend_order_and_the_runs_first_entries() {
    SetupLayout layout = LoadPilot();

    Assert.Equal(
      "BboFXC-V=I~#EePW^",
      string.Concat(layout.Entries.Select(e => e.Glyph))
    );
    Assert.Equal(
      new[] { "steam", "pond", "feed" },
      layout.Runs.Select(r => r.Id).ToArray()
    );
    Assert.Equal(
      new[] { "~", "#" },
      layout
        .Runs.Single(r => r.Id == "feed")
        .Entries.Select(e => e.Glyph.ToString())
        .ToArray()
    );
  }

  // Fails when apart still joins the two cells it names, or a port cell is not an open face.
  [Fact]
  public void The_starter_pilot_leaves_the_feeds_apart_cell_unjoined_and_opens_the_steam_run_onto_the_port() {
    SetupLayout layout = LoadPilot();

    LayoutRunCell apart = layout
      .Runs.Single(r => r.Id == "feed")
      .Cells.Single(c => (c.Cell.X, c.Cell.Y, c.Cell.Z) == (2, 8, -6));
    LayoutRunCell below = layout
      .Runs.Single(r => r.Id == "feed")
      .Cells.Single(c => (c.Cell.X, c.Cell.Y, c.Cell.Z) == (2, 7, -6));
    LayoutRunCell port = layout.Runs.Single(r => r.Id == "steam").Cells[0];
    Assert.Equal("ew", Letters(apart.Joins));
    Assert.Equal("sw", Letters(below.Joins));
    Assert.Equal("d", Letters(port.Opens));
    Assert.Equal("e", Letters(port.Joins));
  }

  // Fails when the glyph a variant alone draws counts as unused, or its layers are not laid
  // over the main drawing.
  [Fact]
  public void A_variants_layers_replace_the_cells_they_draw() {
    SetupLayout main = LoadPilot();
    JObject variant = (JObject)Pilot["variants"]!["open-end"]!;

    SetupLayout open = main.ForVariant(variant, Where);

    LayoutCell end = open.Cells.Single(c => c.Symbol == '^');
    Assert.Equal((1, 10, -3), (end.X, end.Y, end.Z));
    Assert.Equal(main.Cells.Count + 1, open.Cells.Count);
    Assert.DoesNotContain(main.Cells, c => c.Symbol == '^');
    LayoutRunCell beside = open
      .Runs.Single(r => r.Id == "steam")
      .Cells.Single(c => (c.Cell.X, c.Cell.Y, c.Cell.Z) == (1, 10, -2));
    Assert.Equal("n", Letters(beside.Opens));
  }

  #endregion

  #region Reading

  // Fails when a blank row is not counted, when "." is not air, or when the origin is not the
  // first glyph's cell.
  [Fact]
  public void A_layer_counts_every_row_and_reads_a_dot_and_a_space_as_air() {
    SetupLayout layout = Load(
      """
      { origin: [5, -2], legend: { "A": { code: "x:a" } },
        layers: { "3": ["", "", " A.A"] } }
      """
    );

    Assert.Equal("A(6,3,0) A(8,3,0)", Cells(layout));
  }

  // Fails when layers are read lowest first, or rows or columns out of order.
  [Fact]
  public void Cells_are_read_from_the_highest_layer_down_each_row_left_to_right() {
    SetupLayout layout = Load(
      """
      { legend: { "A": { code: "x:a" }, "B": { code: "x:b" } },
        layers: { "1": ["AB", "BA"], "0": ["A", "B"], "-1": ["B"] } }
      """
    );

    Assert.Equal(
      "A(0,1,0) B(1,1,0) B(0,1,1) A(1,1,1) A(0,0,0) B(0,0,1) B(0,-1,0)",
      Cells(layout)
    );
  }

  // Fails when a run reads a join across layers, joins another run's cell, or starts anywhere
  // but the first end in reading order.
  [Fact]
  public void A_run_joins_in_all_six_directions_but_never_a_cell_of_another_run() {
    SetupLayout layout = Load(
      $$"""
      { legend: { "a": { run: "one", {{Pipe}} }, "b": { run: "two", {{Pipe}} } },
        layers: { "1": ["a"], "0": ["aab"] } }
      """
    );

    LayoutRun one = layout.Runs.Single(r => r.Id == "one");
    LayoutRun two = layout.Runs.Single(r => r.Id == "two");
    Assert.Equal("(0,1,0) (0,0,0) (1,0,0)", Path(one));
    Assert.Equal("d", Letters(one.Cells[0].Joins));
    Assert.Equal("eu", Letters(one.Cells[1].Joins));
    Assert.Equal("w", Letters(one.Cells[2].Joins));
    Assert.Equal("(2,0,0)", Path(two));
    Assert.Equal("", Letters(two.Cells[0].Joins));
  }

  // Fails when a loop does not start at its first cell in reading order, or from is ignored.
  [Fact]
  public void A_loop_starts_at_its_first_cell_and_from_names_the_start() {
    string loop = """
      { legend: { "a": { run: "ring", code: "x:pipe-straight-we-iron" FROM } },
        layers: { "0": ["aa", "aa"] } }
      """;

    Assert.Equal(
      "(0,0,0) (1,0,0) (1,0,1) (0,0,1)",
      Path(Load(loop.Replace("FROM", "")).Runs[0])
    );
    Assert.Equal(
      "(1,0,1) (1,0,0) (0,0,0) (0,0,1)",
      Path(Load(loop.Replace("FROM", ", from: [1, 0, 1]")).Runs[0])
    );
  }

  // Fails when a straight pipe code takes anything but the four shapes of its material.
  [Fact]
  public void A_straight_pipe_code_takes_the_four_shapes_of_its_material() {
    SetupLayout layout = Load(
      """
      { legend: { "a": { run: "r", code: "ppex:pipe-straight-ns-cast-iron" } }, layers: { "0": ["a"] } }
      """
    );

    Assert.Equal(
      new[]
      {
        "ppex:pipe-straight-*-cast-iron",
        "ppex:pipe-bend-*-cast-iron",
        "ppex:pipe-tjunction-*-cast-iron",
        "ppex:pipe-xjunction-*-cast-iron",
      },
      layout.Runs[0].Entries[0].Family.ToArray()
    );
  }

  #endregion

  #region Refusals

  // Fails when a glyph the legend lacks is drawn without a refusal, or the message loses the layer,
  // row, column or cell.
  [Fact]
  public void A_glyph_the_legend_lacks_is_refused_naming_its_place() {
    string message = Refused(
      """
      { origin: [2, 3], legend: { "A": { code: "x:a" } }, layers: { "4": ["A", "AQ"] } }
      """
    );

    Assert.Contains("layout.layers.4[1]", message);
    Assert.Contains("'Q'", message);
    Assert.Contains("column 1", message);
    Assert.Contains("(3, 4, 4)", message);
  }

  // Fails when a legend key no layer draws is kept.
  [Fact]
  public void A_legend_key_no_layer_draws_is_refused() {
    string message = Refused(
      """
      { legend: { "A": { code: "x:a" }, "Z": { code: "x:z" } }, layers: { "0": ["A"] } }
      """
    );

    Assert.Contains("layout.legend.Z", message);
    Assert.Contains("draws this glyph", message);
  }

  // Fails when a key longer than one glyph, a space or a dot is accepted.
  [Theory]
  [InlineData("AB")]
  [InlineData(" ")]
  [InlineData(".")]
  [InlineData("\\\\")]
  public void A_legend_key_that_is_not_one_glyph_is_refused(string key) {
    string message = Refused(
      $$"""
      { legend: { "{{key}}": { code: "x:a" } }, layers: { "0": ["A"] } }
      """
    );

    Assert.Contains("one printable ASCII glyph", message);
  }

  // Fails when an entry with an id drawn twice is accepted.
  [Fact]
  public void An_entry_with_an_id_drawn_twice_is_refused() {
    string message = Refused(
      """
      { legend: { "M": { id: "m", code: "x:m" } }, layers: { "0": ["MM"] } }
      """
    );

    Assert.Contains("layout.legend.M", message);
    Assert.Contains("\"m\" is drawn 2 times", message);
  }

  // Fails when an entry with an id that a variant's layers clear away is accepted.
  [Fact]
  public void An_entry_with_an_id_drawn_by_no_cell_is_refused() {
    SetupLayout main = Load(
      """
      { legend: { "M": { id: "m", code: "x:m" } }, layers: { "0": ["M"] } }
      """
    );

    var e = Assert.Throws<SetupFileException>(() =>
      main.ForVariant(JObject.Parse("""{ layers: { "0": ["."] } }"""), Where)
    );

    Assert.Contains("\"m\" is drawn 0 times", e.Message);
  }

  // Fails when a layer key that is not a whole number is accepted.
  [Fact]
  public void A_layer_key_that_is_not_a_whole_number_is_refused() {
    string message = Refused(
      """
      { legend: { "A": { code: "x:a" } }, layers: { "high": ["A"] } }
      """
    );

    Assert.Contains("layout.layers.high", message);
    Assert.Contains("not a whole number", message);
  }

  // Fails when layers written from the lowest up, or a layer repeated, are accepted.
  [Theory]
  [InlineData("'0': ['A'], '1': ['A']")]
  [InlineData("'1': ['A'], '01': ['A']")]
  public void Layers_out_of_order_are_refused(string layers) {
    string message = Refused(
      $$"""
      { legend: { "A": { code: "x:a" } }, layers: { {{layers}} } }
      """
    );

    Assert.Contains("from the highest down", message);
  }

  // Fails when a row holding a tab is accepted.
  [Fact]
  public void A_row_holding_a_tab_is_refused() {
    string message = Refused(
      """
      { legend: { "A": { code: "x:a" } }, layers: { "0": ["A	A"] } }
      """
    );

    Assert.Contains("layout.layers.0[0]", message);
    Assert.Contains("column 1 holds a tab", message);
  }

  // Fails when a cells or port entry naming an id no block entry has is accepted.
  [Theory]
  [InlineData("cells")]
  [InlineData("port")]
  public void A_cells_or_port_entry_naming_no_block_entry_is_refused(
    string kind
  ) {
    string message = Refused(
      $$"""
      { legend: { "A": { code: "x:a" }, "c": { {{kind}}: "ghost" }, "p": { run: "r", {{Pipe}} } },
        layers: { "0": ["Acp"] } }
      """
    );

    Assert.Contains("layout.legend.c", message);
    Assert.Contains("\"ghost\" is the id of no block entry", message);
  }

  // Fails when a port or open cell beside no run cell is accepted.
  [Theory]
  [InlineData("{ port: 'm' }", "port")]
  [InlineData("{ open: true }", "open")]
  public void A_port_or_open_cell_beside_no_run_cell_is_refused(
    string entry,
    string kind
  ) {
    string message = Refused(
      $$"""
      { legend: { "M": { id: "m", code: "x:m" }, "o": {{entry}}, "p": { run: "r", {{Pipe}} } },
        layers: { "0": ["Mo.p"] } }
      """
    );

    Assert.Contains($"the {kind} cell (1, 0, 0)", message);
  }

  // Fails when a run in two pieces is accepted.
  [Fact]
  public void A_run_that_is_not_one_connected_piece_is_refused() {
    string message = Refused(
      $$"""
      { legend: { "p": { run: "r", {{Pipe}} } }, layers: { "0": ["p.p"] } }
      """
    );

    Assert.Contains("run \"r\" is not one connected piece", message);
    Assert.Contains("(2, 0, 0)", message);
  }

  // Fails when an apart naming no join of its run, or a cell its entry does not draw, is accepted.
  [Theory]
  [InlineData("at: [0, 0, 0], face: 'n'", "has no join to")]
  [InlineData("at: [1, 0, 0], face: 'w'", "not drawn with this entry's glyph")]
  public void An_apart_naming_no_join_of_its_run_is_refused(
    string apart,
    string reason
  ) {
    string message = Refused(
      $$"""
      { legend: { "p": { run: "r", {{Pipe}}, apart: [{ {{apart}} }] }, "q": { run: "r", {{Pipe}} } },
        layers: { "0": ["pq"] } }
      """
    );

    Assert.Contains("layout.legend.p.apart[0]", message);
    Assert.Contains(reason, message);
  }

  // Fails when a run code that is not a straight pipe is accepted without a family.
  [Fact]
  public void A_run_entry_that_is_no_straight_pipe_needs_its_family() {
    string message = Refused(
      """
      { legend: { "p": { run: "r", code: "x:pipe-fluidintake-w" } }, layers: { "0": ["p"] } }
      """
    );

    Assert.Contains("layout.legend.p", message);
    Assert.Contains("needs \"family\"", message);
  }

  // Fails when an entry that is none of the five kinds, or two of them, is accepted.
  [Theory]
  [InlineData("{ tree: {} }", "is no block, run, cells, port or open entry")]
  [InlineData(
    "{ cells: 'm', port: 'm' }",
    "takes one of run, cells, port or open"
  )]
  public void An_entry_of_no_kind_or_two_kinds_is_refused(
    string entry,
    string reason
  ) {
    string message = Refused(
      $$"""
      { legend: { "A": {{entry}} }, layers: { "0": ["A"] } }
      """
    );

    Assert.Contains(reason, message);
  }

  // Fails when a from naming a cell outside its run is accepted.
  [Fact]
  public void A_from_naming_no_cell_of_the_run_is_refused() {
    string message = Refused(
      $$"""
      { legend: { "p": { run: "r", {{Pipe}}, from: [4, 0, 0] } }, layers: { "0": ["pp"] } }
      """
    );

    Assert.Contains("layout.legend.p.from", message);
    Assert.Contains("(4, 0, 0) is no cell of run \"r\"", message);
  }

  // Fails when an origin of three numbers is accepted.
  [Fact]
  public void An_origin_that_is_not_a_pair_is_refused() {
    string message = Refused(
      """
      { origin: [1, 2, 3], legend: { "A": { code: "x:a" } }, layers: { "0": ["A"] } }
      """
    );

    Assert.Contains("layout.origin", message);
    Assert.Contains("[x, z]", message);
  }

  // Fails when an open entry set to false is accepted.
  [Fact]
  public void An_open_entry_that_is_not_true_is_refused() {
    string message = Refused(
      $$"""
      { legend: { "o": { open: false }, "p": { run: "r", {{Pipe}} } }, layers: { "0": ["op"] } }
      """
    );

    Assert.Contains("layout.legend.o.open", message);
    Assert.Contains("expected true", message);
  }

  // Fails when a key the entry kind does not take is accepted.
  [Fact]
  public void An_entry_key_its_kind_does_not_take_is_refused() {
    string message = Refused(
      $$"""
      { legend: { "p": { run: "r", {{Pipe}}, tree: {} } }, layers: { "0": ["p"] } }
      """
    );

    Assert.Contains("layout.legend.p", message);
    Assert.Contains("unknown key \"tree\"", message);
  }

  #endregion

  #region Variants

  // Fails when a dot in the overlay does not clear a cell, a space does not keep one, or the
  // overlay shifts the origin.
  [Fact]
  public void An_overlay_replaces_clears_and_keeps_cells() {
    SetupLayout main = Load(
      """
      { origin: [1, 1], legend: { "A": { code: "x:a" }, "B": { code: "x:b" } },
        layers: { "0": ["AAA", "AAB"] } }
      """
    );

    SetupLayout variant = main.ForVariant(
      JObject.Parse("""{ layers: { "1": [" B"], "0": ["B.", " ."] } }"""),
      Where
    );

    Assert.Equal(
      "B(2,1,1) B(1,0,1) A(3,0,1) A(1,0,2) B(3,0,2)",
      Cells(variant)
    );
  }

  // Fails when a dropped id leaves its cells, or the cells entries naming it, drawn.
  [Fact]
  public void A_drop_removes_the_entry_its_cells_and_the_footprint_marks_naming_it() {
    SetupLayout main = Load(
      """
      { legend: { "M": { id: "m", code: "x:m" }, "c": { cells: "m" }, "A": { code: "x:a" } },
        layers: { "0": ["McA"] } }
      """
    );

    SetupLayout variant = main.ForVariant(
      JObject.Parse("""{ drop: ["m"] }"""),
      Where
    );

    Assert.Equal("A(2,0,0)", Cells(variant));
    Assert.Equal("A", string.Concat(variant.Entries.Select(e => e.Glyph)));
  }

  // Fails when a replaced entry loses its id or keeps its old code.
  [Fact]
  public void A_replace_gives_the_entry_new_keys_and_keeps_its_id() {
    SetupLayout main = Load(
      """
      { legend: { "V": { id: "valve", code: "x:v", tree: { gate: 1 } } }, layers: { "0": ["V"] } }
      """
    );

    SetupLayout variant = main.ForVariant(
      JObject.Parse(
        """{ replace: { valve: { code: "x:v", tree: { gate: 2 } } } }"""
      ),
      Where
    );

    LayoutEntry valve = variant.Entries.Single();
    Assert.Equal("valve", valve.Id);
    Assert.Equal(2, (int)valve.Body["tree"]!["gate"]!);
  }

  // Fails when a variant's drawing is not checked again: a layer that splits a run is refused.
  [Fact]
  public void A_variant_drawing_that_breaks_a_rule_is_refused() {
    SetupLayout main = Load(
      $$"""
      { legend: { "p": { run: "r", {{Pipe}} } }, layers: { "0": ["ppp"] } }
      """
    );

    var e = Assert.Throws<SetupFileException>(() =>
      main.ForVariant(
        JObject.Parse("""{ layers: { "0": [" ."] } }"""),
        Where.In("variants").In("cut")
      )
    );

    Assert.Contains("not one connected piece", e.Message);
  }

  // Fails when a drop or replace naming no entry (a step's id) changes the drawing.
  [Fact]
  public void A_drop_or_replace_naming_no_entry_leaves_the_drawing() {
    SetupLayout main = Load(
      """
      { legend: { "A": { code: "x:a" } }, layers: { "0": ["A"] } }
      """
    );

    SetupLayout variant = main.ForVariant(
      JObject.Parse(
        """{ drop: ["step"], replace: { step: { code: "x:b" } } }"""
      ),
      Where
    );

    Assert.Equal("A(0,0,0)", Cells(variant));
  }

  // Fails when a layout is not read by the file model, or its refusals lose the file's name.
  [Fact]
  public void The_file_model_reads_its_layout_and_refuses_one_that_breaks_a_rule() {
    string file = """
      { setup: "s", description: "d", mods: [], seconds: 10, steps: [],
        record: { machines: { a: { series: { state: "on" } } } },
        layout: { legend: { "A": { id: "a", code: "x:a" } }, layers: { "0": ["A"] } } }
      """;

    SetupFileModel model = SetupFileModel.Parse(file, "s.json");
    var e = Assert.Throws<SetupFileException>(() =>
      SetupFileModel.Parse(file.Replace("[\"A\"]", "[\"AZ\"]"), "s.json")
    );

    Assert.Equal("A(0,0,0)", Cells(model.Drawn!));
    Assert.StartsWith("s.json: layout.layers.0[0]", e.Message);
  }

  // Fails when the file model checks the main drawing only and not a variant's.
  [Fact]
  public void The_file_model_refuses_a_variant_drawing_that_breaks_a_rule() {
    string file = """
      { setup: "s", description: "d", mods: [], seconds: 10, steps: [],
        record: { machines: { a: { series: { state: "on" } } } },
        layout: { legend: { "a": { id: "a", code: "x:a" }, "p": { run: "r", code: "x:pipe-straight-we-iron" } }, layers: { "0": ["appp"] } },
        variants: { cut: { layers: { "0": ["  ."] } } } }
      """;

    var e = Assert.Throws<SetupFileException>(() =>
      SetupFileModel.Parse(file, "s.json")
    );

    Assert.Contains("not one connected piece", e.Message);
  }

  #endregion
}
