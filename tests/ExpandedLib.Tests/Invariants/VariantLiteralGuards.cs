using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>Every string exlib source compares with a variant value names a state some definition
/// declares; the rule is <see cref="VariantLiterals"/>. exlib declares no variant groups of its own,
/// so a comparison against a consumer's group is allowed by name.</summary>
[GuardOf(typeof(VariantLiterals), nameof(VariantLiterals.Check))]
public class VariantLiteralGuards(ITestOutputHelper output) {
  private static readonly Dictionary<string, string> Allowed = new() {
    [
      "Blocks/BlockExStairs.cs: Variant[\"verticalorientation\"] against \"up\": no definition declares that variant group"
    ] =
      "BlockExStairs serves the stair definitions of the mods and the game, which declare verticalorientation",
  };

  private static readonly Dictionary<string, string> KnownFindings = new();

  [Fact]
  public void Every_compared_literal_is_a_declared_state() {
    VariantLiterals.Result result = VariantLiterals.Check(
      new List<IExDef>(),
      RepoPaths.Src("exlib")
    );
    output.WriteLine($"{result.Comparisons} comparisons");

    Assert.True(
      result.Comparisons > 0,
      "no exlib source compares a variant literal"
    );
    FindingLists.Assert(result.Findings, Allowed, KnownFindings);
  }
}
