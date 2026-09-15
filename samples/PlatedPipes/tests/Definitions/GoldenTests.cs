using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace PlatedPipes.Tests;

/// <summary>
/// Golden-file parity for every platedpipes code-first def: each def must reproduce its committed
/// <c>goldens/platedpipes/{Location.Path}</c> file, and the golden set must exactly cover the defs.
/// </summary>
public class GoldenTests {
  private const string Domain = "platedpipes";
  private static readonly Assembly Mod = typeof(PlatedPipeDefinitions).Assembly;
  private static readonly string GoldenRoot = System.IO.Path.Combine(
    RepoPaths.Mod("platedpipes"),
    "tests",
    "goldens"
  );

  public static IEnumerable<object[]> Defs() =>
    DefinitionGoldens.Cases(Domain, Mod);

  [Theory]
  [MemberData(nameof(Defs))]
  public void Def_reproduces_its_golden(string relativePath) {
    var (ok, message) = DefinitionGoldens.CheckGolden(
      Domain,
      Mod,
      relativePath,
      GoldenRoot
    );
    Assert.True(ok, message);
  }

  [Fact]
  public void Goldens_exactly_cover_the_defs() {
    var (missing, orphans) = DefinitionGoldens.CheckCompleteness(
      Domain,
      Mod,
      GoldenRoot
    );
    Assert.True(
      missing.Count == 0,
      "defs with no golden file (unmigrated?): " + string.Join(", ", missing)
    );
    Assert.True(
      orphans.Count == 0,
      "golden files with no def (deleted?): " + string.Join(", ", orphans)
    );
  }

  [Fact]
  public void Blesses_the_goldens_when_requested() {
    if (!DefinitionGoldens.WriteRequested)
      return;
    DefinitionGoldens.WriteAll(Domain, Mod, GoldenRoot);
  }
}
