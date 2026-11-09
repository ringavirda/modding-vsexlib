using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Casting;
using Xunit;

namespace ExpandedLib.Tests.Casting;

/// <summary>The casting station catalogue: JSON declarations, the merge rule, the C# route.</summary>
public class CastingStationTests {
  [Fact]
  public void A_station_declared_in_json_is_registered_with_its_moulds_and_sand() {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile
    );

    Assert.True(registry.TryGet("testflask", out CastingStationKind? kind));
    Assert.Equal([MouldKind.Open], kind!.Moulds);
    Assert.Equal("test:casting/base", kind.SandBase);
    Assert.Equal("test:casting/half", kind.SandHalf);
    Assert.Equal("test:greensand", kind.SandItem);
  }

  // Fails when the loader skips the sand item.
  [Fact]
  public void A_station_without_a_sand_item_is_refused() {
    var registry = new CastingStationRegistry();

    List<string> errors = CastingStationLoader.Load(
      [
        (
          "test:config/castingstations/x.json",
          """{ "stations": [ { "code": "test", "sand": { "base": "a", "half": "b" } } ] }"""
        ),
      ],
      registry
    );

    Assert.Contains("sand.item", Assert.Single(errors));
    Assert.Equal(0, registry.Count);
  }

  // Fails when SameAs leaves the sand item out.
  [Fact]
  public void A_kind_redeclared_with_another_sand_item_is_a_conflict() {
    var registry = new CastingStationRegistry();
    var first = new CastingStationKind(
      "testflask",
      [MouldKind.Open],
      "a:base",
      "a:half",
      "a:sand"
    );
    var second = first with { SandItem = "b:sand" };

    Assert.False(first.SameAs(second));
    Assert.Empty(registry.Contribute(new CastingStationSet(1, [first])));
    Assert.Single(registry.Contribute(new CastingStationSet(1, [second])));
  }

  // Fails when the wildcard is compared as a plain string.
  [Theory]
  [InlineData("game:sand-granite", true)]
  [InlineData("game:gravel-granite", false)]
  [InlineData("othermod:sand-granite", false)]
  [InlineData("", false)]
  [InlineData(null, false)]
  public void A_sand_item_wildcard_matches_the_family_it_names(
    string? code,
    bool expected
  ) {
    var kind = new CastingStationKind(
      "testflask",
      [MouldKind.Open],
      "a:base",
      "a:half",
      "game:sand-*"
    );

    Assert.Equal(expected, kind.Matches(code));
  }

  // Fails when the sand item is compared as a raw string: a domainless catalogue item matches no game: item.
  [Theory]
  [InlineData("sand-granite", "game:sand-granite", true)]
  [InlineData("sand-*", "game:sand-granite", true)]
  [InlineData("sand-*", "othermod:sand-granite", false)]
  public void A_sand_item_without_a_domain_is_the_game_domain(
    string sandItem,
    string code,
    bool expected
  ) {
    var kind = new CastingStationKind(
      "testflask",
      [MouldKind.Open],
      "a:base",
      "a:half",
      sandItem
    );

    Assert.Equal(expected, kind.Matches(code));
  }

  // Fails when a redeclaration with different sand replaces the held kind.
  [Fact]
  public void A_kind_redeclared_with_different_sand_is_reported_and_the_first_stands() {
    var registry = new CastingStationRegistry();
    var first = new CastingStationKind(
      "testflask",
      [MouldKind.Open],
      "a:base",
      "a:half",
      "a:sand"
    );
    var second = new CastingStationKind(
      "testflask",
      [MouldKind.Open],
      "b:base",
      "b:half",
      "a:sand"
    );

    Assert.Empty(registry.Contribute(new CastingStationSet(1, [first])));
    IReadOnlyList<string> conflicts = registry.Contribute(
      new CastingStationSet(1, [second])
    );

    Assert.Contains("testflask", Assert.Single(conflicts));
    Assert.True(registry.TryGet("testflask", out CastingStationKind? held));
    Assert.Equal("a:base", held!.SandBase);
  }

  [Fact]
  public void An_identical_redeclaration_is_a_no_op() {
    var registry = new CastingStationRegistry();
    var kind = new CastingStationKind(
      "testflask",
      [MouldKind.Open, MouldKind.Closed],
      "a:base",
      "a:half",
      "a:sand"
    );
    var same = new CastingStationKind(
      "TestFlask",
      [MouldKind.Closed, MouldKind.Open],
      "a:base",
      "a:half",
      "a:sand"
    );

    registry.Contribute(new CastingStationSet(1, [kind]));
    Assert.Empty(registry.Contribute(new CastingStationSet(1, [same])));
    Assert.Equal(1, registry.Count);
  }

  [Fact]
  public void A_station_file_with_an_unknown_key_is_reported_and_skipped() {
    var registry = new CastingStationRegistry();

    List<string> errors = CastingStationLoader.Load(
      [
        (
          "test:config/castingstations/bad.json",
          """{ "stations": [ { "code": "testflask", "mould": ["open"], "sand": { "base": "a", "half": "b", "item": "t:s" } } ] }"""
        ),
      ],
      registry
    );

    string error = Assert.Single(errors);
    Assert.Contains("bad.json", error);
    Assert.Contains("mould", error);
    Assert.Equal(0, registry.Count);
  }

  [Theory]
  [InlineData(
    """{ "stations": [ { "code": "Test", "sand": { "base": "a", "half": "b", "item": "t:s" } } ] }""",
    "bare lowercase"
  )]
  [InlineData(
    """{ "stations": [ { "code": "test", "moulds": [] , "sand": { "base": "a", "half": "b", "item": "t:s" } } ] }""",
    "no moulds"
  )]
  [InlineData(
    """{ "stations": [ { "code": "test", "moulds": ["wet"], "sand": { "base": "a", "half": "b", "item": "t:s" } } ] }""",
    "invalid mould 'wet'"
  )]
  [InlineData("""{ "stations": [ { "code": "test" } ] }""", "'sand' pair")]
  public void A_malformed_station_is_refused_with_its_reason(
    string json,
    string reason
  ) {
    var registry = new CastingStationRegistry();

    List<string> errors = CastingStationLoader.Load(
      [("test:config/castingstations/x.json", json)],
      registry
    );

    Assert.Contains(reason, Assert.Single(errors));
    Assert.Equal(0, registry.Count);
  }
}
