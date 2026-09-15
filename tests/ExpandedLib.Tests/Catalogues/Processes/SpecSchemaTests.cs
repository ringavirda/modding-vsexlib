using ExpandedLib.Catalogues;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Tests <see cref="SpecSchema"/>: every shipped form reads, an unknown one is
/// refused by name.</summary>
public class SpecSchemaTests {
  private static JsonObject Json(string json) => new(JToken.Parse(json));

  private const int Current = 2;

  private static int Read(string json, int current = Current) {
    Assert.True(
      SpecSchema.TryRead(
        Json(json),
        current,
        out int schema,
        out string? error
      ),
      error
    );
    return schema;
  }

  private static string Rejects(string json, int current = Current) {
    Assert.False(
      SpecSchema.TryRead(Json(json), current, out _, out string? error)
    );
    Assert.NotNull(error);
    return error!;
  }

  [Fact]
  public void A_declaration_with_no_schema_reads_as_the_first_one() {
    // The first form has a number whether or not it was written down.
    Assert.Equal(SpecSchema.First, Read("""{ "family": "shingledbar" }"""));
    Assert.Equal(1, SpecSchema.First);
  }

  [Fact]
  public void A_declared_schema_is_read_back() {
    Assert.Equal(2, Read("""{ "schema": 2 }"""));
  }

  [Fact]
  public void An_older_form_is_still_read() {
    // A spec written against schema 1 keeps loading on a build that has moved to 2.
    Assert.Equal(1, Read("""{ "schema": 1 }""", current: 2));
  }

  [Fact]
  public void A_schema_from_a_newer_build_is_refused_by_name() {
    // The error names both schema numbers.
    string error = Rejects("""{ "schema": 3 }""", current: 2);

    Assert.Contains("3", error);
    Assert.Contains("2", error);
  }

  [Fact]
  public void A_schema_below_the_first_one_is_malformed() {
    Assert.Contains("schema", Rejects("""{ "schema": 0 }"""));
    Assert.Contains("schema", Rejects("""{ "schema": -1 }"""));
  }

  [Fact]
  public void A_missing_node_reads_as_the_first_form_rather_than_throwing() {
    Assert.True(SpecSchema.TryRead(null, Current, out int schema, out _));
    Assert.Equal(SpecSchema.First, schema);
  }
}
