using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Pins golden-file parity for exlib's own code-first defs via <see cref="DefinitionGoldens"/>.</summary>
public class ExlibDefinitionGoldenTests {
  private const string Domain = "exlib";
  private static readonly Assembly Mod = typeof(ExBlockDef).Assembly;
  private static readonly string GoldenRoot = System.IO.Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
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

  /// <summary>Every shape a def names must exist; the goldens do not pin shape file presence.</summary>
  [Fact]
  public void Every_shape_reference_resolves_to_a_shipped_file() {
    IReadOnlyList<string> missing = DefinitionAssets.MissingShapes(Domain, Mod);

    Assert.True(
      missing.Count == 0,
      "definitions naming shapes that do not exist: "
        + string.Join("; ", missing)
    );
  }

  [Fact]
  public void Regenerate_goldens_when_requested() {
    if (DefinitionGoldens.WriteRequested)
      DefinitionGoldens.WriteAll(Domain, Mod, GoldenRoot);
  }
}
