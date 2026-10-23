using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The codes <see cref="DefinitionCodes.Expand(ExBlockDef, bool)"/> lists are the ones the
/// loader registers.</summary>
public class DefinitionCodesTests {
  private static ExBlockDef Slab() =>
    ExBlockDef
      .Create("test", "slab")
      .VariantGroup("side", "up", "down")
      .VariantGroup("cover", "bare", "snow")
      .SkipVariants("*-up-snow");

  // Fails when Expand ignores skipVariants and lists a variant the loader never registers.
  [Fact]
  public void A_variant_matching_skipVariants_is_not_listed() {
    Assert.Equal(
      ["test:slab-up-bare", "test:slab-down-bare", "test:slab-down-snow"],
      DefinitionCodes.Expand(Slab()).Select(r => r.Code)
    );
  }

  // Fails when the wildcard rendering of a worldproperty group runs skipVariants against its
  // literal star.
  [Fact]
  public void A_wildcard_expansion_keeps_every_code() {
    Assert.Equal(
      4,
      DefinitionCodes.Expand(Slab(), propertyGroupsAsWildcard: true).Count()
    );
  }
}
