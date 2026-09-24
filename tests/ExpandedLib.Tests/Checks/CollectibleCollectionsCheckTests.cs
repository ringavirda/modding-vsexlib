using System.Collections.Generic;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="CollectibleCollectionsCheck"/> over planted collectibles whose collections
/// a mod's code cleared after load.</summary>
public class CollectibleCollectionsCheckTests(ITestOutputHelper output) {
  // Fails when Run stops reading a null creative tab list or a block's null Variant, reads an
  // item's Variant, or reads another domain's collectibles.
  [Fact]
  [PlantedDefect(
    typeof(CollectibleCollectionsCheck),
    nameof(CollectibleCollectionsCheck.Run)
  )]
  public void A_null_creative_tab_list_and_a_blocks_null_variant_are_reported() {
    LoadedStubGame game = new LoadedStubGame(
      new RecipeStubSource().Covering("stub")
    )
      .Item("stub:untabbed", item => item.CreativeInventoryTabs = null!)
      .Block("stub:unvaried", block => block.Variant = null!)
      .Item("stub:plain", item => item.Variant = null!)
      .Block("game:air", block => block.CreativeInventoryTabs = null!);

    IReadOnlyList<string> found = CollectibleCollectionsCheck
      .Run(game, "stub")
      .Errors;
    foreach (string line in found)
      output.WriteLine(line);

    Assert.Equal(
      [
        "stub:untabbed: CreativeInventoryTabs is null, which looking at a mount dereferences",
        "stub:unvaried: Variant is null, which the snowball system dereferences",
      ],
      found
    );
  }
}
