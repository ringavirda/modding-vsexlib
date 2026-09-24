using ExpandedLib.Helpers;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The choice a rig or a scenario makes out of a layout cell's wildcard code.</summary>
public class ExWildcardsTests {
  // Fails when FirstBranch returns the whole body in place of the part before the first |.
  [Fact]
  public void A_tagged_alternation_takes_its_first_branch() {
    Assert.Equal("air", ExWildcards.FirstAlternative("@(air|coalpile)"));
  }

  // Fails when FirstAlternative only collapses a group written with @.
  [Fact]
  public void A_bare_group_takes_its_first_branch() {
    Assert.Equal("brick-red", ExWildcards.FirstAlternative("brick-(red|tan)"));
  }

  // Fails when FirstBranch splits on a | inside a nested group, or the branch is not resolved
  // again.
  [Fact]
  public void A_nested_alternation_in_the_first_branch_is_resolved_too() {
    Assert.Equal(
      "brickcourse-.*-black",
      ExWildcards.FirstAlternative(
        "@(brickcourse-.*-(black|red|tan)|claybricks-good-fire)"
      )
    );
  }

  // Fails when MatchingParen stops at the first ) rather than the one at depth zero.
  [Fact]
  public void The_text_after_a_nested_group_is_kept() {
    Assert.Equal("a-x-tail", ExWildcards.FirstAlternative("@(a-(x|y)|b)-tail"));
  }

  // Fails when an unbalanced ( is dropped or throws.
  [Fact]
  public void An_unbalanced_group_is_kept_as_written() {
    Assert.Equal("brick-(red", ExWildcards.FirstAlternative("brick-(red"));
  }
}
