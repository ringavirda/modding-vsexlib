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

  // A shared golden root and its series sibling, both inside one planted directory.
  private const string Series = "1.21";

  private static (PlantedFiles files, string root) SharedAndSeries() {
    var files = new PlantedFiles();
    foreach (IExDef def in DefinitionGoldens.Collect(Domain, Here))
      files.Write(
        "goldens/" + DefinitionGoldens.RelativePath(def),
        def.ToJson().ToString()
      );
    return (files, files.Path("goldens"));
  }

  // Fails when CheckGolden reads the shared golden on a series that has its own.
  [Fact]
  [PlantedDefect(
    typeof(DefinitionGoldens),
    nameof(DefinitionGoldens.CheckGolden)
  )]
  public void A_series_golden_overrides_the_shared_one_on_that_series() {
    var (files, root) = SharedAndSeries();
    using var _ = files;
    files.Write(
      "goldens/" + AnvilPath,
      Anvil(Domain).Resistance(4).ToJson().ToString()
    );
    files.Write("goldens-1.21/" + AnvilPath, Anvil(Domain).ToJson().ToString());

    Assert.Equal(
      (true, ""),
      DefinitionGoldens.CheckGolden(Domain, Here, AnvilPath, root, Series)
    );
    Assert.False(
      DefinitionGoldens.CheckGolden(Domain, Here, AnvilPath, root, null).ok
    );
  }

  // Fails when CheckCompleteness leaves the series folder unread.
  [Fact]
  [PlantedDefect(
    typeof(DefinitionGoldens),
    nameof(DefinitionGoldens.CheckCompleteness)
  )]
  public void A_series_golden_no_def_claims_is_reported_orphaned() {
    var (files, root) = SharedAndSeries();
    using var _ = files;
    files.Write("goldens-1.21/plantedgoldens/blocktypes/stray.json", "{}");

    var (missing, orphans) = DefinitionGoldens.CheckCompleteness(
      Domain,
      Here,
      root,
      Series
    );

    Assert.Empty(missing);
    Assert.Equal(
      ["goldens-1.21/plantedgoldens/blocktypes/stray.json"],
      orphans
    );
  }

  // Fails when a series write rewrites the shared goldens, writes a series golden that matches the
  // shared one, or keeps a series golden that no longer differs.
  [Fact]
  public void A_series_write_keeps_only_the_goldens_that_differ_on_that_series() {
    var (files, root) = SharedAndSeries();
    using var _ = files;
    string sharedAnvil = files.Write(
      "goldens/" + AnvilPath,
      Anvil(Domain).Resistance(4).ToJson().ToString()
    );
    string other = DefinitionGoldens
      .Collect(Domain, Here)
      .Select(DefinitionGoldens.RelativePath)
      .First(p => p != AnvilPath);
    string stale = files.Write("goldens-1.21/" + other, "{}");

    DefinitionGoldens.WriteAll(Domain, Here, root, "1", Series);

    Assert.Equal(
      Anvil(Domain).Resistance(4).ToJson().ToString(),
      File.ReadAllText(sharedAnvil)
    );
    Assert.Equal(
      Anvil(Domain).ToJson().ToString(),
      File.ReadAllText(files.Path("goldens-1.21/" + AnvilPath))
    );
    Assert.False(File.Exists(stale));
    Assert.Equal(
      [files.Path("goldens-1.21/" + AnvilPath)],
      Directory.EnumerateFiles(
        files.Path("goldens-1.21"),
        "*.json",
        SearchOption.AllDirectories
      )
    );
  }

  // Fails when CheckCompleteness looks for a def's golden only among the shared goldens, so a def
  // only an older series has stays missing there.
  [Fact]
  [PlantedDefect(
    typeof(DefinitionGoldens),
    nameof(DefinitionGoldens.CheckCompleteness)
  )]
  public void A_def_only_the_series_has_is_satisfied_by_its_series_golden() {
    var (files, root) = SharedAndSeries();
    using var _ = files;
    File.Delete(files.Path("goldens/" + AnvilPath));
    files.Write("goldens-1.21/" + AnvilPath, Anvil(Domain).ToJson().ToString());

    var (missing, orphans) = DefinitionGoldens.CheckCompleteness(
      Domain,
      Here,
      root,
      Series
    );

    Assert.Empty(missing);
    Assert.Empty(orphans);
    Assert.Equal(
      [AnvilPath],
      DefinitionGoldens.CheckCompleteness(Domain, Here, root, null).missing
    );
  }

  // Fails when a marker stops excusing the shared golden of a def the series lacks.
  [Fact]
  [PlantedDefect(
    typeof(DefinitionGoldens),
    nameof(DefinitionGoldens.CheckCompleteness)
  )]
  public void A_shared_golden_the_series_lacks_is_excused_by_its_marker() {
    var (files, root) = SharedAndSeries();
    using var _ = files;
    files.Write("goldens/" + Gone, "{}");

    string[] unmarked =
    [
      .. DefinitionGoldens
        .CheckCompleteness(Domain, Here, root, Series)
        .orphans,
    ];
    files.Write("goldens-1.21/" + Gone + DefinitionGoldens.AbsentSuffix, "");

    Assert.Equal([Gone], unmarked);
    Assert.Empty(
      DefinitionGoldens.CheckCompleteness(Domain, Here, root, Series).orphans
    );
    Assert.Equal(
      [Gone],
      DefinitionGoldens.CheckCompleteness(Domain, Here, root, null).orphans
    );
  }

  // Fails when a marker whose def the series has, or whose shared golden is gone, passes as a
  // marker that excuses something.
  [Fact]
  [PlantedDefect(
    typeof(DefinitionGoldens),
    nameof(DefinitionGoldens.CheckCompleteness)
  )]
  public void A_stale_marker_is_an_orphan() {
    var (files, root) = SharedAndSeries();
    using var _ = files;
    files.Write(
      "goldens-1.21/" + AnvilPath + DefinitionGoldens.AbsentSuffix,
      ""
    );
    files.Write("goldens-1.21/" + Gone + DefinitionGoldens.AbsentSuffix, "");

    var (missing, orphans) = DefinitionGoldens.CheckCompleteness(
      Domain,
      Here,
      root,
      Series
    );

    Assert.Empty(missing);
    Assert.Equal(
      [
        "goldens-1.21/" + AnvilPath + ".absent",
        "goldens-1.21/" + Gone + ".absent",
      ],
      orphans
    );
  }

  // Fails when a series write leaves the shared golden of a def the series lacks unmarked, or
  // keeps a stale marker.
  [Fact]
  public void A_series_write_marks_what_the_series_lacks_and_drops_stale_markers() {
    var (files, root) = SharedAndSeries();
    using var _ = files;
    files.Write("goldens/" + Gone, "{}");
    string stale = files.Write(
      "goldens-1.21/" + AnvilPath + DefinitionGoldens.AbsentSuffix,
      ""
    );

    DefinitionGoldens.WriteAll(Domain, Here, root, "1", Series);

    Assert.True(
      File.Exists(
        files.Path("goldens-1.21/" + Gone + DefinitionGoldens.AbsentSuffix)
      )
    );
    Assert.False(File.Exists(stale));
    var (missing, orphans) = DefinitionGoldens.CheckCompleteness(
      Domain,
      Here,
      root,
      Series
    );
    Assert.Empty(missing);
    Assert.Empty(orphans);
  }

  // Fails when a series write refuses a fragment naming only a shared golden the series lacks.
  [Fact]
  public void A_series_write_takes_a_fragment_naming_a_golden_the_series_lacks() {
    var (files, root) = SharedAndSeries();
    using var _ = files;
    files.Write("goldens/" + Gone, "{}");

    DefinitionGoldens.WriteAll(
      Domain,
      Here,
      root,
      "plantedgoldens/blocktypes/gone",
      Series
    );

    Assert.True(
      File.Exists(
        files.Path("goldens-1.21/" + Gone + DefinitionGoldens.AbsentSuffix)
      )
    );
  }

  // A shared golden no def of this assembly claims, as one a series lacks.
  private const string Gone = "plantedgoldens/blocktypes/gone.json";
}
