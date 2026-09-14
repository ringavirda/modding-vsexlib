using ExpandedLib.Testing;
using Xunit;

namespace HandMill.Tests;

/// <summary>
/// The per-mod JSON-defect rule (<see cref="ShippedJson"/>) over the sample's own tree.
/// </summary>
public class ShippedAssetJsonTests {
  [Fact]
  public void Handmills_shipped_json_carries_no_defect() {
    var offenders = ShippedJson.Check(RepoPaths.Assets("handmill"));
    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }
}
