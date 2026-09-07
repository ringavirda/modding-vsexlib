using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Config;
using ExpandedLib.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <c>ExConfigGenerator</c>'s <c>[ExRecipeProfile]</c> failure paths: none of these can be exercised
/// through a fixture compiled into this assembly (a bad fixture would fail the whole assembly's
/// build, since the generated <c>#error</c> stops compilation), so each drives the generator directly
/// over a small standalone compilation and inspects what it emits. The generator ships no runtime
/// assembly (it is analyzer-only, referenced by every mod project at compile time only), so its
/// already-built analyzer DLL is loaded by reflection rather than by adding a compile reference.
/// </summary>
public class ExConfigGeneratorFailureTests {
  private static readonly MetadataReference[] References = BuildReferences();

  private static MetadataReference[] BuildReferences() {
    // Every trusted platform assembly (mscorlib, System.Runtime, System.Collections, ...) plus
    // exlib itself, so the compilation resolves Dictionary<,>, the [ExConfigRegister]/
    // [ExRecipeProfile] attributes and RecipeCostEntry the same way a real mod project would.
    string[] platform = (
      (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
    ).Split(Path.PathSeparator);

    var refs = platform
      .Where(File.Exists)
      .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
      .ToList();
    refs.Add(
      MetadataReference.CreateFromFile(typeof(ExConfigRegisterAttribute).Assembly.Location)
    );
    return refs.ToArray();
  }

  /// <summary>Loads whichever build of <c>ExpandedLib.Generators.dll</c> ran most recently, and
  /// instantiates its <c>ExConfigGenerator</c> by name.</summary>
  private static IIncrementalGenerator NewGenerator() {
    string binRoot = Path.Combine(RepoPaths.Root, "generators", "bin");
    string path = Directory
      .EnumerateFiles(binRoot, "ExpandedLib.Generators.dll", SearchOption.AllDirectories)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault()
      ?? throw new InvalidOperationException(
        $"No built ExpandedLib.Generators.dll found under {binRoot}."
      );

    Assembly generators = Assembly.LoadFrom(path);
    Type generatorType =
      generators.GetType("ExpandedLib.Generators.ExConfigGenerator")
      ?? throw new InvalidOperationException(
        $"{path} carries no ExpandedLib.Generators.ExConfigGenerator type."
      );
    return (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
  }

  /// <summary>Runs <c>ExConfigGenerator</c> over a standalone source (no reference to this assembly's
  /// own fixtures), and returns the one <c>#error</c>-carrying accessor it emits alongside any
  /// diagnostics the generator itself reported.</summary>
  private static (string? Generated, IReadOnlyList<Diagnostic> Diagnostics) Run(string source) {
    var compilation = CSharpCompilation.Create(
      "ExConfigGeneratorFailureTest",
      new[] { CSharpSyntaxTree.ParseText(source) },
      References,
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );

    GeneratorDriver driver = CSharpGeneratorDriver.Create(NewGenerator());
    driver = driver.RunGeneratorsAndUpdateCompilation(
      compilation,
      out _,
      out var diagnostics
    );

    GeneratorDriverRunResult result = driver.GetRunResult();
    string? generated = result
      .Results.SelectMany(r => r.GeneratedSources)
      .Select(s => s.SourceText.ToString())
      .FirstOrDefault();
    return (generated, diagnostics);
  }

  /// <summary>0-based line of the first occurrence of <paramref name="needle"/> in
  /// <paramref name="source"/> - what <c>ClassDeclarationSyntax.GetLocation()</c> reports for a
  /// class whose attribute lists start there, matching <see cref="Diagnostic.Location"/>'s own
  /// line numbering.</summary>
  private static int LineOf(string source, string needle) {
    int index = source.IndexOf(needle, StringComparison.Ordinal);
    return source[..index].Count(c => c == '\n');
  }

  [Fact]
  public void A_config_with_no_catalogue_property_gets_an_error_naming_the_missing_member() {
    string source = """
      using ExpandedLib.Config;

      namespace GenTest;

      [ExConfigRegister("gentest.json", "gentest")]
      [ExRecipeProfile]
      public class BadConfig : IExVersionedConfig {
        public string? ConfigVersion { get; set; }
      }
      """;
    (string? generated, IReadOnlyList<Diagnostic> diagnostics) = Run(source);

    Assert.NotNull(generated);
    Assert.Contains("#error", generated);
    Assert.Contains("Dictionary<string, RecipeCostEntry>", generated);
    Assert.Contains("found none", generated);

    Diagnostic diagnostic = Assert.Single(diagnostics, d => d.Id == "EXLIB0002");
    Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    Assert.Contains("found none", diagnostic.GetMessage());
    Assert.Equal(
      LineOf(source, "[ExConfigRegister"),
      diagnostic.Location.GetLineSpan().StartLinePosition.Line
    );
  }

  [Fact]
  public void A_private_DefaultCatalogue_gets_an_error_instead_of_generating_an_inaccessible_call() {
    string source = """
      using System.Collections.Generic;
      using ExpandedLib.Config;
      using ExpandedLib.Registries;

      namespace GenTest;

      [ExConfigRegister("gentest.json", "gentest")]
      [ExRecipeProfile]
      public class BadConfig : IExVersionedConfig {
        public string? ConfigVersion { get; set; }
        public string RecipeLevel { get; set; } = "normal";
        public Dictionary<string, RecipeCostEntry> Recipes { get; set; } = new();
        private static Dictionary<string, RecipeCostEntry> DefaultCatalogue() => new();
      }
      """;
    (string? generated, IReadOnlyList<Diagnostic> diagnostics) = Run(source);

    Assert.NotNull(generated);
    Assert.Contains("#error", generated);
    Assert.Contains("DefaultCatalogue", generated);

    Diagnostic diagnostic = Assert.Single(diagnostics, d => d.Id == "EXLIB0002");
    Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    Assert.Contains("DefaultCatalogue", diagnostic.GetMessage());
    Assert.Equal(
      LineOf(source, "[ExConfigRegister"),
      diagnostic.Location.GetLineSpan().StartLinePosition.Line
    );
  }

  [Fact]
  public void A_wrongly_typed_DefaultCatalogue_gets_an_error_instead_of_generating_a_conversion_error() {
    string source = """
      using System.Collections.Generic;
      using ExpandedLib.Config;
      using ExpandedLib.Registries;

      namespace GenTest;

      [ExConfigRegister("gentest.json", "gentest")]
      [ExRecipeProfile]
      public class BadConfig : IExVersionedConfig {
        public string? ConfigVersion { get; set; }
        public string RecipeLevel { get; set; } = "normal";
        public Dictionary<string, RecipeCostEntry> Recipes { get; set; } = new();
        public static Dictionary<string, string> DefaultCatalogue() => new();
      }
      """;
    (string? generated, IReadOnlyList<Diagnostic> diagnostics) = Run(source);

    Assert.NotNull(generated);
    Assert.Contains("#error", generated);
    Assert.Contains("DefaultCatalogue", generated);

    Diagnostic diagnostic = Assert.Single(diagnostics, d => d.Id == "EXLIB0002");
    Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    Assert.Contains("DefaultCatalogue", diagnostic.GetMessage());
    Assert.Equal(
      LineOf(source, "[ExConfigRegister"),
      diagnostic.Location.GetLineSpan().StartLinePosition.Line
    );
  }

  [Fact]
  public void A_missing_RecipeLevel_property_gets_an_error_naming_it() {
    string source = """
      using System.Collections.Generic;
      using ExpandedLib.Config;
      using ExpandedLib.Registries;

      namespace GenTest;

      [ExConfigRegister("gentest.json", "gentest")]
      [ExRecipeProfile]
      public class BadConfig : IExVersionedConfig {
        public string? ConfigVersion { get; set; }
        public Dictionary<string, RecipeCostEntry> Recipes { get; set; } = new();
        public static Dictionary<string, RecipeCostEntry> DefaultCatalogue() => new();
      }
      """;
    (string? generated, IReadOnlyList<Diagnostic> diagnostics) = Run(source);

    Assert.NotNull(generated);
    Assert.Contains("#error", generated);
    Assert.Contains("'RecipeLevel'", generated);

    Diagnostic diagnostic = Assert.Single(diagnostics, d => d.Id == "EXLIB0002");
    Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    Assert.Contains("'RecipeLevel'", diagnostic.GetMessage());
    Assert.Equal(
      LineOf(source, "[ExConfigRegister"),
      diagnostic.Location.GetLineSpan().StartLinePosition.Line
    );
  }

  [Fact]
  public void A_LevelConfig_with_no_ExConfigRegister_gets_an_error_naming_it() {
    string source = """
      using System.Collections.Generic;
      using ExpandedLib.Config;
      using ExpandedLib.Registries;

      namespace GenTest;

      public class PlainLevelHolder {
        public string RecipeLevel { get; set; } = "normal";
      }

      [ExConfigRegister("gentest.json", "gentest")]
      [ExRecipeProfile(LevelConfig = typeof(PlainLevelHolder))]
      public class BadConfig : IExVersionedConfig {
        public string? ConfigVersion { get; set; }
        public Dictionary<string, RecipeCostEntry> Recipes { get; set; } = new();
        public static Dictionary<string, RecipeCostEntry> DefaultCatalogue() => new();
      }
      """;
    (string? generated, IReadOnlyList<Diagnostic> diagnostics) = Run(source);

    Assert.NotNull(generated);
    Assert.Contains("#error", generated);
    Assert.Contains("PlainLevelHolder", generated);
    Assert.Contains("[ExConfigRegister]", generated);

    Diagnostic diagnostic = Assert.Single(diagnostics, d => d.Id == "EXLIB0002");
    Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    Assert.Contains("PlainLevelHolder", diagnostic.GetMessage());
    Assert.Equal(
      LineOf(source, "[ExConfigRegister"),
      diagnostic.Location.GetLineSpan().StartLinePosition.Line
    );
  }

  [Fact]
  public void ExRecipeProfile_with_no_companion_ExConfigRegister_reports_a_diagnostic() {
    (string? generated, IReadOnlyList<Diagnostic> diagnostics) = Run(
      """
      using ExpandedLib.Config;

      namespace GenTest;

      [ExRecipeProfile]
      public class OrphanConfig : IExVersionedConfig {
        public string? ConfigVersion { get; set; }
      }
      """
    );

    Assert.Null(generated); // no [ExConfigRegister]: the accessor pipeline never runs at all
    Diagnostic diagnostic = Assert.Single(diagnostics, d => d.Id == "EXLIB0001");
    Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    Assert.Contains("OrphanConfig", diagnostic.GetMessage());
  }
}
