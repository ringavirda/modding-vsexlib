using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Machines;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace WidgetNamespace.BlockEntities;

/// <summary>A counter ticking on the production lifecycle, saved by <c>[Persist]</c>.</summary>
[BlockEntityRegister]
public class BlockEntityWidget : BlockEntityProductionMachine {
  [Persist]
  private int _ticks;

  protected override int ProductionTickMs => 1000;

  protected override bool CanRunProduction => true;

  protected override void OnProductionTick(float dt) => _ticks++;

  /// <summary>Puts the counter back to zero.</summary>
  public void Reset() => _ticks = 0;

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.Lang("widgetdomain:widget-ticks", _ticks);
  }
}
