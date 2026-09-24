using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="DefinitionGoldens.CheckGolden"/> and
/// <see cref="DefinitionGoldens.CheckCompleteness"/> over a planted block and a golden root under a
/// temporary directory.</summary>
public class DefinitionGoldensTests {
  public DefinitionGoldensTests() => TestModDomain.Register();

  private const string Domain = "plantedgoldens";
  private static readonly Assembly Here =
    typeof(DefinitionGoldensTests).Assembly;

  private static ExBlockDef Anvil(string domain) =>
    ExBlockDef.Create(domain, "anvil").Resistance(3);

  /// <summary>Declares its block only under <see cref="Domain"/>, so other scans of this assembly
  /// never see it.</summary>
  private sealed class Anvils : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain == Domain ? [Anvil(domain)] : [];
  }

  private static readonly string AnvilPath = DefinitionGoldens.RelativePath(
    Anvil(Domain)
  );

  // Every def the domain collects gets its golden: this assembly's other providers answer any
  // domain.
  private static PlantedFiles GoldenRoot() {
    var files = new PlantedFiles();
    foreach (IExDef def in DefinitionGoldens.Collect(Domain, Here))
      files.Write(DefinitionGoldens.RelativePath(def), def.ToJson().ToString());
    return files;
  }

  [Fact]
  [PlantedDefect(
    typeof(DefinitionGoldens),
    nameof(DefinitionGoldens.CheckGolden)
  )]
  public void A_def_that_differs_from_its_golden_is_reported() {
    using var files = new PlantedFiles();
    files.Write(AnvilPath, Anvil(Domain).Resistance(4).ToJson().ToString());

    var (ok, message) = DefinitionGoldens.CheckGolden(
      Domain,
      Here,
      AnvilPath,
      files.Root
    );

    Assert.False(ok);
    Assert.StartsWith(
      "plantedgoldens/blocktypes/anvil.json code-first def diverged from its golden",
      message
    );
  }

  [Fact]
  public void A_def_matching_its_golden_passes() {
    using var files = GoldenRoot();

    Assert.Equal(
      (true, ""),
      DefinitionGoldens.CheckGolden(Domain, Here, AnvilPath, files.Root)
    );
  }

  [Fact]
  [PlantedDefect(
    typeof(DefinitionGoldens),
    nameof(DefinitionGoldens.CheckCompleteness)
  )]
  public void A_def_with_no_golden_is_reported_missing() {
    using var files = GoldenRoot();
    File.Delete(files.Path(AnvilPath));

    var (missing, orphans) = DefinitionGoldens.CheckCompleteness(
      Domain,
      Here,
      files.Root
    );

    Assert.Equal([AnvilPath], missing);
    Assert.Empty(orphans);
  }

  [Fact]
  [PlantedDefect(
    typeof(DefinitionGoldens),
    nameof(DefinitionGoldens.CheckCompleteness)
  )]
  public void A_golden_no_def_claims_is_reported_orphaned() {
    using var files = GoldenRoot();
    files.Write("plantedgoldens/blocktypes/stray.json", "{}");

    var (missing, orphans) = DefinitionGoldens.CheckCompleteness(
      Domain,
      Here,
      files.Root
    );

    Assert.Empty(missing);
    Assert.Equal(["plantedgoldens/blocktypes/stray.json"], orphans);
  }

  [Fact]
  public void A_golden_per_def_and_no_more_passes() {
    using var files = GoldenRoot();

    var (missing, orphans) = DefinitionGoldens.CheckCompleteness(
      Domain,
      Here,
      files.Root
    );

    Assert.Empty(missing);
    Assert.Empty(orphans);
    Assert.Contains(
      DefinitionGoldens.Collect(Domain, Here),
      d => DefinitionGoldens.RelativePath(d) == AnvilPath
    );
  }
}
