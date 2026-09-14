using ExpandedLib.Testing;
using WidgetNamespace;
using Xunit;

namespace WidgetNamespace.Tests;

/// <summary>The generated accessor loads its coded default when nothing is on disk yet.</summary>
public class WidgetConfigTests {
  [Fact]
  public void Loads_its_coded_default_when_unconfigured() {
    var world = new TestWorld();

    WidgetValues.Load(world.Api);

    Assert.Equal(10, WidgetValues.WidgetCount);
  }
}
