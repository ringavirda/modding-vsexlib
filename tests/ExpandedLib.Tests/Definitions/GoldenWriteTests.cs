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

  // Fails when WriteAll throws on a fragment whose first segment is another domain and whose
  // second is a golden category of this suite.
  [Fact]
  public void A_fragment_of_another_domain_is_left_to_that_domains_goldens() {
    DefinitionGoldens.WriteAll(Domain, Mod, root, "siex/blocktypes/nomatch");

    Assert.False(Directory.Exists(root));
  }

  // Fails when WriteAll leaves to another suite a fragment whose second segment is no golden
  // category of this suite: one missing its domain, or one whose second segment is not a category.
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
