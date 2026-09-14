using System.Collections.Generic;
using ExpandedLib.Migrations;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace WidgetNamespace.Migrations;

/// <summary>Rewrites the widget's old code to its current one for a save from before the rename.</summary>
public class WidgetMigration : IBlockCodeMigration {
  public string Name => "Widget renamed";

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  ) {
    yield return (
      new AssetLocation("widgetdomain:oldwidget"),
      new AssetLocation("widgetdomain:widget")
    );
  }
}
