using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace SmokeStack.Tests;

/// <summary>Every smokestack layout code writes its alternations in the regex form; the rule is
/// <see cref="LayoutWildcards"/>.</summary>
[GuardOf(typeof(LayoutWildcards), nameof(LayoutWildcards.Check))]
public class LayoutWildcardGuards(ITestOutputHelper output) {
  private static readonly Dictionary<string, string> Allowed = new();

  private static readonly Dictionary<string, string> KnownFindings = new();

  [Fact]
  public void Every_alternation_in_a_layout_uses_the_regex_form() {
    var defs = DefinitionGoldens.Collect(
      "smokestack",
      typeof(Blocks.BlockSmokeStackIntake).Assembly
    );
    Premise.Covers(defs.Select(DefinitionGoldens.RelativePath), "smokestack");
    LayoutWildcards.Result result = LayoutWildcards.Check(
      defs.OfType<ExBlockDef>()
    );
    output.WriteLine(
      $"{result.Layouts} layouts, {result.Alternations} alternations"
    );

    Assert.True(
      result.Layouts > 0,
      "no smokestack definition carries a layout"
    );
    Assert.True(
      result.Alternations > 0,
      "no smokestack layout code carries an alternation"
    );
    FindingLists.Assert(result.Findings, Allowed, KnownFindings);
  }
}
