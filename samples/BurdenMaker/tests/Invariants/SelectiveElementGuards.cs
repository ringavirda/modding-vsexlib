using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace BurdenMaker.Tests;

/// <summary>Every burdenmaker selective-element path and construction-stage element names an element of
/// its shape, in the subtree form; the rule is <see cref="SelectiveElements"/>.</summary>
[GuardOf(typeof(SelectiveElements), nameof(SelectiveElements.Check))]
public class SelectiveElementGuards(ITestOutputHelper output) {
  private static readonly Dictionary<string, string> Allowed = new();

  private static readonly Dictionary<string, string> KnownFindings = new();

  [Fact]
  public void Every_selective_path_names_a_shape_element_in_subtree_form() {
    var defs = DefinitionGoldens.Collect(
      "burdenmaker",
      typeof(BurdenMaker.Blocks.BlockBurdenmaker).Assembly
    );
    Premise.Covers(defs.Select(DefinitionGoldens.RelativePath), "burdenmaker");
    SelectiveElements.Result result = SelectiveElements.Check(
      defs,
      "burdenmaker",
      Path.Combine(RepoPaths.Assets("burdenmaker"), "shapes")
    );
    output.WriteLine($"{result.Lists} lists, {result.Paths} paths");

    Assert.True(
      result.Lists > 0,
      "no burdenmaker definition carries selectiveElements"
    );
    FindingLists.Assert(result.Findings, Allowed, KnownFindings);
  }
}
