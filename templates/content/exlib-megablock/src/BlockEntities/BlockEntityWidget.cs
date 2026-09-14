using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace WidgetNamespace.BlockEntities;

/// <summary>Counts clicks on any cell of its megablock footprint, saved by <c>[Persist]</c>.</summary>
[BlockEntityRegister]
public class BlockEntityWidget : ExBlockEntity {
  [Persist]
  private int _cells;

  /// <summary>Records one more filler-cell click.</summary>
  public void RegisterClick() {
    _cells++;
    MarkDirty();
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.Lang("widgetdomain:widget-cells", _cells);
  }
}
