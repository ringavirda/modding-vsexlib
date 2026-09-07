using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The selector-coverage rule (<see cref="SelectorCoverage"/>) over exlib's own golden blocktypes.
/// </summary>
public class EmittedBlocktypeShapeTests {
  [Fact]
  public void Every_block_variant_resolves_a_shape() {
    var findings = new List<string>();
    foreach (string path in SelectorCoverage.GoldenBlocktypes("exlib"))
      findings.AddRange(SelectorCoverage.Check(path).shapeByType);

    Assert.True(findings.Count == 0, string.Join("\n", findings));
  }

  [Fact]
  public void Every_handbook_group_selector_matches_a_shipped_code() {
    var findings = new List<string>();
    foreach (string path in SelectorCoverage.GoldenBlocktypes("exlib"))
      findings.AddRange(SelectorCoverage.Check(path).groupBy);

    Assert.True(findings.Count == 0, string.Join("\n", findings));
  }

  [Fact]
  public void The_golden_corpus_is_not_empty() {
    // exlib's own corpus is one file today (goldens/exlib/blocktypes/structurefiller.json), which
    // declares neither shapeByType nor a handbook groupBy - both rules above pass vacuously on it.
    // Asserting the corpus itself is non-empty is what stops a renamed goldens/ or blocktypes/
    // folder from reading as "every selector resolves".
    Assert.NotEmpty(SelectorCoverage.GoldenBlocktypes("exlib"));
  }
}
