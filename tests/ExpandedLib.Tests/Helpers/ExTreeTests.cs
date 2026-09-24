using ExpandedLib.Helpers;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The defensive tree-value reader: a corrupt persisted field degrades to a fallback instead
/// of throwing (which would make the engine discard the whole block entity on load).</summary>
public class ExTreeTests {
  [Fact]
  public void Deserializes_a_well_formed_value() {
    string[] result = ExTree.SafeDeserialize<string[]>("[\"ns\",\"ew\"]", []);
    Assert.Equal(new[] { "ns", "ew" }, result);
  }

  [Fact]
  public void Null_or_empty_returns_the_fallback() {
    var fallback = new[] { "keep" };
    Assert.Same(fallback, ExTree.SafeDeserialize(null, fallback));
    Assert.Same(fallback, ExTree.SafeDeserialize("", fallback));
  }

  [Fact]
  public void Malformed_json_returns_the_fallback_instead_of_throwing() {
    // A corrupted/hand-edited orientation string must not throw out of FromTreeAttributes.
    string[] fallback = [];
    Assert.Same(fallback, ExTree.SafeDeserialize("{ not valid ]", fallback));
    Assert.Same(fallback, ExTree.SafeDeserialize("[\"unterminated", fallback));
  }

  [Fact]
  public void Json_literal_null_returns_the_fallback() {
    var fallback = new[] { "keep" };
    Assert.Same(fallback, ExTree.SafeDeserialize("null", fallback));
  }

  #region Differences over a reload

  private static TreeAttribute Tree(int level, string? name = "ore") {
    var tree = new TreeAttribute();
    tree.SetInt("level", level);
    if (name != null)
      tree.SetString("name", name);
    return tree;
  }

  // Fails when the type comparison is inverted and reports a key whose type held.
  [Fact]
  public void A_tree_that_survives_whole_has_no_differences() {
    Assert.Empty(ExTree.Differences(Tree(4), Tree(4), Tree(0)));
  }

  // Fails when a key the reloaded tree lacks is skipped.
  [Fact]
  public void A_key_missing_after_the_reload_is_named() {
    Assert.Equal(
      ["name: missing after the reload"],
      ExTree.Differences(Tree(4), Tree(4, name: null), Tree(0))
    );
  }

  // Fails when the attribute type is not compared.
  [Fact]
  public void A_key_reloaded_as_another_type_is_named() {
    TreeAttribute reloaded = Tree(4);
    reloaded.SetFloat("level", 4f);

    Assert.Equal(
      ["level: saved as IntAttribute, reloaded as FloatAttribute"],
      ExTree.Differences(Tree(4), reloaded, Tree(0))
    );
  }

  // Fails when a value back at the fresh instance's is not reported.
  [Fact]
  public void A_value_back_at_the_fresh_instances_is_named() {
    Assert.Equal(
      ["level: back at a fresh instance's value after the reload"],
      ExTree.Differences(Tree(4), Tree(0), Tree(0))
    );
  }

  // Fails when a value the live instance also held at the fresh one's counts as lost.
  [Fact]
  public void A_value_that_was_already_the_fresh_one_is_not_named() {
    Assert.Empty(ExTree.Differences(Tree(0), Tree(0), Tree(0)));
  }

  // Fails when a subtree is compared as one value rather than key by key.
  [Fact]
  public void A_subtree_is_compared_key_by_key_under_its_path() {
    var saved = new TreeAttribute();
    saved["inv"] = Tree(4);
    var reloaded = new TreeAttribute();
    reloaded["inv"] = Tree(4, name: null);
    var fresh = new TreeAttribute();
    fresh["inv"] = Tree(0);

    Assert.Equal(
      ["inv/name: missing after the reload"],
      ExTree.Differences(saved, reloaded, fresh)
    );
  }

  #endregion
}
