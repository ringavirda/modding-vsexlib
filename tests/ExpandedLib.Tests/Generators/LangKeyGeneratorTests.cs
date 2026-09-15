using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using ExpandedLib.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The <c>ExLangKeyGenerator</c> output: a bare <c>en.json</c> key becomes an
/// <c>ExlibLang</c> constant, and EXLIB0003/EXLIB0004 diagnostics for a missing or colliding
/// key.</summary>
public class LangKeyGeneratorTests {
  [Fact]
  public void Bare_keys_are_domain_qualified() {
    Assert.Equal("exlib:network-hi-on", ExlibLang.NetworkHiOn);
    Assert.Equal("exlib:block-structurefiller", ExlibLang.BlockStructurefiller);
    Assert.Equal("exlib:command-recipes-set", ExlibLang.CommandRecipesSet);
  }

  [Fact]
  public void Already_qualified_keys_pass_through_verbatim() {
    Assert.Equal(
      "game:placefailure-exlib-noorientation",
      ExlibLang.PlacefailureExlibNoorientation
    );
  }

  /// <summary>An <c>AdditionalText</c> over an in-memory JSON blob.</summary>
  private sealed class FakeAdditionalText(string path, string content)
    : AdditionalText {
    public override string Path { get; } = path;

    public override SourceText GetText(
      CancellationToken cancellationToken = default
    ) => SourceText.From(content, Encoding.UTF8);
  }

  /// <summary>A flat <c>build_property.*</c> map.</summary>
  private sealed class FakeAnalyzerConfigOptions(
    Dictionary<string, string> values
  ) : AnalyzerConfigOptions {
    public override bool TryGetValue(string key, out string value) =>
      values.TryGetValue(key, out value!);
  }

  private sealed class FakeOptionsProvider(AnalyzerConfigOptions global)
    : AnalyzerConfigOptionsProvider {
    public override AnalyzerConfigOptions GlobalOptions { get; } = global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
      GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
      GlobalOptions;
  }

  private static IIncrementalGenerator NewGenerator() =>
    GeneratorLoader.Load("ExLangKeyGenerator");

  /// <summary>Runs <c>ExLangKeyGenerator</c> over <paramref name="langFiles"/> (path, JSON content
  /// pairs) with <paramref name="assetDomain"/> as <c>$(AssetDomain)</c>, and returns the reported
  /// diagnostics.</summary>
  private static IReadOnlyList<Diagnostic> Run(
    string? assetDomain,
    params (string Path, string Json)[] langFiles
  ) {
    var compilation = CSharpCompilation.Create(
      "LangKeyGeneratorTest",
      Array.Empty<Microsoft.CodeAnalysis.SyntaxTree>(),
      Array.Empty<MetadataReference>(),
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );

    var values = new Dictionary<string, string>();
    if (assetDomain is not null)
      values["build_property.AssetDomain"] = assetDomain;
    var options = new FakeOptionsProvider(
      new FakeAnalyzerConfigOptions(values)
    );
    var texts = langFiles
      .Select(f => (AdditionalText)new FakeAdditionalText(f.Path, f.Json))
      .ToArray();

    GeneratorDriver driver = CSharpGeneratorDriver.Create(
      new[] { NewGenerator().AsSourceGenerator() },
      texts,
      CSharpParseOptions.Default,
      options
    );
    driver = driver.RunGeneratorsAndUpdateCompilation(
      compilation,
      out _,
      out var diagnostics
    );
    return diagnostics;
  }

  [Fact]
  public void AssetDomain_with_no_matching_lang_file_reports_a_diagnostic() {
    IReadOnlyList<Diagnostic> diagnostics = Run("nomatch");

    Diagnostic diagnostic = Assert.Single(
      diagnostics,
      d => d.Id == "EXLIB0003"
    );
    Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    Assert.Contains("nomatch", diagnostic.GetMessage());
  }

  [Fact]
  public void AssetDomain_with_a_matching_lang_file_reports_nothing() {
    IReadOnlyList<Diagnostic> diagnostics = Run(
      "gentest",
      ("/proj/assets/gentest/lang/en.json", """{ "k": "v" }""")
    );

    Assert.DoesNotContain(diagnostics, d => d.Id == "EXLIB0003");
  }

  [Fact]
  public void AssetDomain_with_a_malformed_lang_file_reports_a_diagnostic() {
    IReadOnlyList<Diagnostic> diagnostics = Run(
      "gentest",
      ("/proj/assets/gentest/lang/en.json", "not json")
    );

    Diagnostic diagnostic = Assert.Single(
      diagnostics,
      d => d.Id == "EXLIB0003"
    );
    Assert.Contains("gentest", diagnostic.GetMessage());
  }

  [Fact]
  public void No_AssetDomain_reports_nothing() {
    IReadOnlyList<Diagnostic> diagnostics = Run(null);

    Assert.DoesNotContain(diagnostics, d => d.Id == "EXLIB0003");
  }

  [Fact]
  public void A_sanitised_name_collision_reports_both_keys_and_the_member() {
    // "foo bar" and "foo-bar" both sanitise to "FooBar"; ordinal sort orders "foo bar" first.
    IReadOnlyList<Diagnostic> diagnostics = Run(
      "gentest",
      (
        "/proj/assets/gentest/lang/en.json",
        """{ "foo bar": "a", "foo-bar": "b" }"""
      )
    );

    Diagnostic diagnostic = Assert.Single(
      diagnostics,
      d => d.Id == "EXLIB0004"
    );
    Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    string message = diagnostic.GetMessage();
    Assert.Contains("foo bar", message);
    Assert.Contains("foo-bar", message);
    Assert.Contains("FooBar_2", message);
  }
}
