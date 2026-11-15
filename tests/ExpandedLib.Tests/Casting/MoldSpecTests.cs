using System.Linq;
using ExpandedLib.Industry.Casting;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Casting;

/// <summary>The <c>mold</c> attribute parser against a registry of station kinds.</summary>
public class MoldSpecTests {
  private const string TwoMouldFlask = """
    { "stations": [ { "code": "shutflask", "moulds": ["open", "closed"],
      "sand": { "base": "test:b", "half": "test:h", "item": "test:sand" } } ] }
    """;

  // Fails when the station lookup reads a fixed list of names in place of the registry.
  [Fact]
  public void A_pattern_naming_a_json_declared_station_parses() {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile
    );

    bool ok = MoldSpec.TryParse(
      CastingTestData.Mold("testflask"),
      "testflask",
      out MoldSpec? spec,
      out string? error,
      registry
    );

    Assert.True(ok, error);
    Assert.Equal("testflask", spec!.Station);
    Assert.Equal(100, spec.Capacity);
    Assert.Equal(MouldKind.Open, spec.Mould);
  }

  [Fact]
  public void An_unknown_station_code_is_named_in_the_error() {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile
    );

    bool ok = MoldSpec.TryParseAll(
      CastingTestData.Mold("smallflask"),
      out _,
      out string? error,
      registry
    );

    Assert.False(ok);
    Assert.Contains("'smallflask'", error);
    Assert.Contains("'testflask'", error);
  }

  // Fails when the mould is not checked against the kind's list.
  [Fact]
  public void A_closed_mould_at_a_station_that_does_not_take_it_is_refused() {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile
    );

    bool ok = MoldSpec.TryParseAll(
      CastingTestData.Mold("testflask", ", \"mould\": \"closed\""),
      out _,
      out string? error,
      registry
    );

    Assert.False(ok);
    Assert.Contains("'closed'", error);
    Assert.Contains("'testflask'", error);
  }

  [Fact]
  public void A_closed_mould_at_a_station_that_takes_it_parses() {
    CastingStationRegistry registry = CastingTestData.Registry(TwoMouldFlask);

    bool ok = MoldSpec.TryParse(
      CastingTestData.Mold("shutflask", ", \"mould\": \"closed\""),
      "SHUTFLASK",
      out MoldSpec? spec,
      out string? error,
      registry
    );

    Assert.True(ok, error);
    Assert.Equal(MouldKind.Closed, spec!.Mould);
  }

  [Fact]
  public void A_pattern_not_cast_at_the_asked_station_names_it() {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile,
      TwoMouldFlask
    );

    bool ok = MoldSpec.TryParse(
      CastingTestData.Mold("testflask"),
      "shutflask",
      out _,
      out string? error,
      registry
    );

    Assert.False(ok);
    Assert.Equal("not cast at shutflask", error);
  }

  private const string ThreeBoxPiece = """
    , "cavity": [ [
      { "x1": 1, "y1": 1, "z1": 3, "x2": 3, "y2": 4, "z2": 13 },
      { "x1": 6, "y1": 1, "z1": 3, "x2": 8, "y2": 4, "z2": 13 },
      { "x1": 11, "y1": 1, "z1": 3, "x2": 13, "y2": 4, "z2": 13 }
    ] ]
    """;

  private static MoldSpec Parse(string extra) {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile
    );
    Assert.True(
      MoldSpec.TryParse(
        CastingTestData.Mold("testflask", extra),
        "testflask",
        out MoldSpec? spec,
        out string? error,
        registry
      ),
      error
    );
    return spec!;
  }

  // Fails when a nested cavity entry is read as three pieces: Pieces and Capacity triple.
  [Fact]
  public void A_piece_of_three_boxes_parses_as_one_piece() {
    MoldSpec spec = Parse(ThreeBoxPiece);

    Assert.Equal(1, spec.Pieces);
    Assert.Equal(100, spec.Capacity);
    Assert.Equal(100, spec.UnitsPerPiece);
    Cuboidf[] boxes = Assert.Single(spec.BoxesPerPiece);
    Assert.Equal([1f, 6f, 11f], boxes.Select(b => b.X1));
    Cuboidf bounds = Assert.Single(spec.Cavity);
    Assert.Equal((1f, 13f), (bounds.X1, bounds.X2));
  }

  // Fails when a one-box declaration gains PieceBoxes or its Cavity box changes.
  [Fact]
  public void A_one_box_piece_keeps_its_cavity_and_declares_no_groups() {
    MoldSpec spec = Parse("");

    Assert.Null(spec.PieceBoxes);
    Assert.Same(
      spec.Cavity[0],
      Assert.Single(Assert.Single(spec.BoxesPerPiece))
    );
  }

  // Fails when an empty piece array is accepted.
  [Fact]
  public void A_piece_with_no_boxes_is_refused() {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile
    );

    bool ok = MoldSpec.TryParseAll(
      CastingTestData.Mold("testflask", ", \"cavity\": [ [] ]"),
      out _,
      out string? error,
      registry
    );

    Assert.False(ok);
    Assert.Contains("no boxes", error);
  }

  private static MoldSpec CoredSpec(string extra) {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile
    );
    Assert.True(
      MoldSpec.TryParse(
        CastingTestData.Mold("testflask", extra),
        "testflask",
        out MoldSpec? spec,
        out string? error,
        registry
      ),
      error
    );
    return spec!;
  }

  // Fails when a station entry without a core is refused or reads a core from elsewhere.
  [Fact]
  public void Absent_core_is_uncored() {
    Assert.Null(CoredSpec("").Core);
  }

  // Fails when the core is not read from the station entry.
  [Fact]
  public void A_named_core_is_read() {
    Assert.Equal(
      "test:drycore",
      CoredSpec(", \"core\": \"test:drycore\"").Core
    );
  }

  // Fails when a blank core is taken as a core.
  [Fact]
  public void A_blank_core_is_refused() {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile
    );

    bool ok = MoldSpec.TryParseAll(
      CastingTestData.Mold("testflask", ", \"core\": \" \""),
      out _,
      out string? error,
      registry
    );

    Assert.False(ok);
    Assert.Contains("'core'", error);
  }
}
