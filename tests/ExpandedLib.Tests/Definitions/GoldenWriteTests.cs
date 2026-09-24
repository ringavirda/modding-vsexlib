using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="DefinitionGoldens.WriteAll(string, Assembly, string)"/> under each shape of
/// <c>EXLIB_WRITE_GOLDENS</c>, writing into a scratch golden root.</summary>
public sealed class GoldenWriteTests : IDisposable {
  private const string Domain = "exlib";
  private static readonly Assembly Mod = typeof(ExBlockDef).Assembly;
  private readonly string root = Path.Combine(
    Path.GetTempPath(),
    $"goldenwrite-{Guid.NewGuid():N}"
  );

  public void Dispose() {
    if (Directory.Exists(root))
      Directory.Delete(root, true);
  }

  // Fails when WriteAll stops throwing on a fragment that matches no golden, or writes the goldens
  // the value's other fragments match before it throws.
  [Fact]
  public void A_value_matching_no_golden_throws_naming_it_and_writes_nothing() {
    var thrown = Assert.Throws<InvalidOperationException>(() =>
      DefinitionGoldens.WriteAll(
        Domain,
        Mod,
        root,
        "exlib/blocktypes/structurefiller,nomatch"
      )
    );

    Assert.Equal(
      "EXLIB_WRITE_GOLDENS=exlib/blocktypes/structurefiller,nomatch names no exlib golden: "
        + "nomatch; a fragment starts with its domain, as in exlib/",
      thrown.Message
    );
    Assert.False(Directory.Exists(root));
  }

  // Fails when WriteAll throws on another domain's fragment whose second segment is a game asset
  // category, or takes the categories from this suite's goldens (exlib's are blocktypes only).
  [Theory]
  [InlineData("siex/blocktypes/nomatch")]
  [InlineData("burdenmaker/itemtypes/x")]
  [InlineData("burdenmaker/recipes/x")]
  public void A_fragment_of_another_domain_is_left_to_that_domains_goldens(
    string fragment
  ) {
    DefinitionGoldens.WriteAll(Domain, Mod, root, fragment);

    Assert.False(Directory.Exists(root));
  }

  // Fails when the game's asset categories are read before VintagestoryAPI fills them.
  [Fact]
  public void The_game_asset_categories_are_filled_when_read() {
    Assert.Superset(
      new System.Collections.Generic.HashSet<string>
      {
        "blocktypes",
        "itemtypes",
        "recipes",
        "entities",
        "patches",
      },
      Vintagestory.API.Common.AssetCategory.categories.Keys.ToHashSet()
    );
  }

  // Fails when WriteAll leaves to another suite a fragment whose second segment is no game asset
  // category: one missing its domain, or one whose second segment is not a category.
  [Theory]
  [InlineData("blocktypes/nomatch")]
  [InlineData("siex/nomatch/furnace")]
  public void A_fragment_not_starting_with_a_domain_and_category_throws(
    string fragment
  ) {
    var thrown = Assert.Throws<InvalidOperationException>(() =>
      DefinitionGoldens.WriteAll(Domain, Mod, root, fragment)
    );

    Assert.Equal(
      $"EXLIB_WRITE_GOLDENS={fragment} names no exlib golden: {fragment}; "
        + "a fragment starts with its domain, as in exlib/",
      thrown.Message
    );
  }

  // Fails when WriteAll writes a golden its value does not name, or skips the one it names.
  [Fact]
  public void A_matching_fragment_writes_its_golden() {
    DefinitionGoldens.WriteAll(
      Domain,
      Mod,
      root,
      "exlib/blocktypes/structurefiller.json"
    );

    Assert.Equal(
      ["exlib/blocktypes/structurefiller.json"],
      Directory
        .GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
    );
  }
}
