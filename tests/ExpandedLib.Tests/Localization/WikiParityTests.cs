using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Definitions;
using ExpandedLib.Industry;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every symbol the wiki writes as code must resolve against the shipped assembly.</summary>
public class WikiParityTests {
  /// <summary>Identifiers the wiki writes as code that are deliberately not exlib types.</summary>
  private static readonly string[] KnownAbsent =
  [
    // Compiled only under the legacy !GAME_GE_1_22 guard; absent from this guard's assembly.
    "ExConstructionIngredient",
    "ExConstructionStage",
    "ExRightClickConstruction",
    // Registered class strings, not C# class names; a JSON blocktype writes these.
    "ExFilledMegastructure",
    "ExMultiblock",
    // Registered behaviour string for BlockBehaviorExOrientable, not a C# class name.
    "ExOrientable",
  ];

  private static string WikiDirectory => Path.Combine(RepoPaths.Root, "wiki");

  /// <summary>The source generators the wiki documents, read from source since they ship no runtime
  /// assembly.</summary>
  private static IEnumerable<string> GeneratorTypeNames() {
    string dir = Path.Combine(RepoPaths.Root, "src/ExpandedLib.Generators");
    var declared = new Regex(
      @"\bclass\s+(?<name>\w+)\s*:\s*[^{\r\n]*\bIIncrementalGenerator\b"
    );
    foreach (string file in Directory.EnumerateFiles(dir, "*.cs"))
      foreach (Match m in declared.Matches(File.ReadAllText(file)))
        yield return m.Groups["name"].Value;
  }

  // Both assemblies the exlib mod ships: the framework and the domain layer beside it.
  private static WikiParity.Report Run() =>
    WikiParity.Check(
      WikiDirectory,
      [typeof(ExDefinitions).Assembly, typeof(IndustryModule).Assembly],
      KnownAbsent,
      GeneratorTypeNames()
    );

  [Fact]
  public void Every_symbol_the_wiki_writes_as_code_resolves_against_the_assembly() {
    WikiParity.Report report = Run();

    Assert.True(
      report.Findings.Count == 0,
      $"{report.Findings.Count} wiki symbol(s) name something exlib does not have, out of "
        + $"{report.SymbolsChecked} checked. A reader who copies one of these gets a compile error, or "
        + "a call that no longer exists:\n  "
        + string.Join("\n  ", report.Findings.Select(f => f.ToString()))
    );
  }

  /// <summary>Points the declaration-shape checks at a throwaway markdown directory, removed on
  /// dispose.</summary>
  private sealed class TempWikiPage : System.IDisposable {
    private readonly string _dir = Path.Combine(
      Path.GetTempPath(),
      "exlib_wikiparitytest_" + System.Guid.NewGuid().ToString("N")
    );

    public TempWikiPage(string contents) {
      Directory.CreateDirectory(_dir);
      File.WriteAllText(Path.Combine(_dir, "Page.md"), contents);
    }

    public string Dir => _dir;

    public void Dispose() {
      try {
        Directory.Delete(_dir, recursive: true);
      } catch { /* best-effort cleanup */
      }
    }
  }

  [Fact]
  public void A_member_declared_virtual_where_the_code_declares_it_abstract_is_a_finding() {
    using var page = new TempWikiPage(
      """
      ## `BlockEntityProductionMachine`

      ```csharp
      public abstract class BlockEntityProductionMachine : BlockEntity
      {
          protected virtual bool CanRunProduction { get; }
      }
      ```
      """
    );

    WikiParity.Report report = WikiParity.Check(
      page.Dir,
      typeof(ExDefinitions).Assembly
    );

    Assert.Contains(
      report.Findings,
      f =>
        f.Symbol == "BlockEntityProductionMachine.CanRunProduction"
        && f.Reason.Contains("'virtual'")
    );
  }

  [Fact]
  public void The_guard_reads_the_wiki_and_reaches_real_api() {
    WikiParity.Report report = Run();

    Assert.True(
      report.FilesRead > 0,
      $"no wiki markdown under {WikiDirectory} - the guard is inert"
    );
    Assert.True(
      report.SymbolsChecked > 0,
      "no wiki symbol resolved to an exlib type - the extractor read nothing the assembly owns"
    );
    Assert.True(
      GeneratorTypeNames().Any(),
      "no IIncrementalGenerator found in src/ExpandedLib.Generators/ - the generator names the wiki "
        + "may cite would be resolved from an empty set, so any of them would read as valid"
    );

    string[] wikiFiles = Directory.GetFiles(
      WikiDirectory,
      "*.md",
      SearchOption.AllDirectories
    );
    foreach (string entry in KnownAbsent)
      Assert.True(
        wikiFiles.Any(f => File.ReadAllText(f).Contains(entry)),
        $"{entry} no longer appears in the wiki - delete its stale suppression from KnownAbsent"
      );
  }

  [Fact]
  public void An_override_of_a_member_the_type_inherits_but_does_not_itself_declare_is_not_a_finding() {
    // ExBlockEntityContainer inherits Inventory from vanilla's BlockEntityContainer without
    // overriding it.
    using var page = new TempWikiPage(
      """
      ```csharp
      public abstract class ExBlockEntityContainer : BlockEntityContainer
      {
          public override InventoryBase Inventory { get; }
      }
      ```
      """
    );

    WikiParity.Report report = WikiParity.Check(
      page.Dir,
      typeof(ExDefinitions).Assembly
    );

    Assert.DoesNotContain(
      report.Findings,
      f => f.Symbol == "ExBlockEntityContainer.Inventory"
    );
  }

  [Fact]
  public void An_override_declared_where_the_code_declares_it_abstract_is_a_finding() {
    using var page = new TempWikiPage(
      """
      ## `BlockEntityProductionMachine`

      ```csharp
      protected override bool CanRunProduction => true;
      ```
      """
    );

    WikiParity.Report report = WikiParity.Check(
      page.Dir,
      typeof(ExDefinitions).Assembly
    );

    Assert.Contains(
      report.Findings,
      f =>
        f.Symbol == "BlockEntityProductionMachine.CanRunProduction"
        && f.Reason.Contains("'override'")
    );
  }
}
