using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using WidgetNamespace.Migrations;
using Xunit;

namespace WidgetNamespace.Tests;

/// <summary>The migration declares the one rename it exists for.</summary>
public class WidgetMigrationTests {
  [Fact]
  public void GetRemaps_declares_the_old_to_new_pair() {
    var migration = new WidgetMigration();

    var pairs = migration.GetRemaps(new TestWorld().Api).ToList();

    Assert.Single(pairs);
    Assert.Equal(new AssetLocation("widgetdomain:oldwidget"), pairs[0].oldCode);
    Assert.Equal(new AssetLocation("widgetdomain:widget"), pairs[0].newCode);
  }
}
