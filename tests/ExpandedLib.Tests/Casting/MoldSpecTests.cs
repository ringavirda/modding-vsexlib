using ExpandedLib.Industry.Casting;
using Xunit;

namespace ExpandedLib.Tests.Casting;

/// <summary>The <c>mold</c> attribute parser against a registry of station kinds.</summary>
public class MoldSpecTests {
  private const string TwoMouldFlask = """
    { "stations": [ { "code": "shutflask", "moulds": ["open", "closed"],
      "sand": { "base": "test:b", "half": "test:h" } } ] }
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
}
