using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="CodeLiterals.UnresolvableBareCodes"/> over a planted variant-grouped block
/// and a source file that names it.</summary>
public class CodeLiteralsTests {
  public CodeLiteralsTests() => TestModDomain.Register();

  private const string Domain = "plantedliterals";
  private static readonly Assembly Here = typeof(CodeLiteralsTests).Assembly;

  /// <summary>Declares its block only under <see cref="Domain"/>, so other scans of this assembly
  /// never see it.</summary>
  private sealed class Gadget : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain == Domain
        ? [ExBlockDef.Create(domain, "gadget").VariantGroup("side", "n", "s")]
        : [];
  }

  private static IReadOnlyList<string> Scan(string source) {
    using var files = new PlantedFiles();
    files.Write("src/Planted.cs", source);
    return CodeLiterals.UnresolvableBareCodes(Domain, Here, files.Path("src"));
  }

  [Fact]
  [PlantedDefect(
    typeof(CodeLiterals),
    nameof(CodeLiterals.UnresolvableBareCodes)
  )]
  public void A_literal_naming_a_variant_grouped_block_by_its_bare_code_is_reported() {
    string finding = Assert.Single(
      Scan("var code = \"plantedliterals:gadget\";")
    );

    Assert.StartsWith(
      "src/Planted.cs:1  \"plantedliterals:gadget\" - gadget declares variant groups",
      finding
    );
  }

  [Fact]
  public void Literals_naming_a_variant_or_a_wildcard_pass() {
    Assert.Empty(
      Scan(
        "var one = \"plantedliterals:gadget-n\";\n"
          + "var any = \"plantedliterals:gadget-*\";"
      )
    );
  }
}
