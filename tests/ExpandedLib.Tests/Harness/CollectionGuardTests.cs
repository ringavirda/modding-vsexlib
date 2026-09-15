using System.Reflection;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every <c>[Collection("...")]</c> name used in this assembly has a matching
/// <c>[CollectionDefinition(...)]</c>.</summary>
public class CollectionGuardTests {
  [Fact]
  public void Every_collection_name_has_a_definition() =>
    StaticStateCollection.EveryCollectionNameHasADefinition(
      Assembly.GetExecutingAssembly()
    );
}
