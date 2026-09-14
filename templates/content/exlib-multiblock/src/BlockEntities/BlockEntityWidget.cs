using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace WidgetNamespace.BlockEntities;

[BlockEntityRegister]
public class BlockEntityWidget : BlockEntityMultiblockMachine {
  [Persist]
  private int _ticks;

  private int Angle => ExOrientation.AngleFromSide(Block?.Variant?["side"]);

  protected override void UpdateStructureRotation() => SetStructureAngle(Angle);

  protected override string GetIncompleteMessage(int missingCount) =>
    Lang.Get("widgetdomain:multiblock-widget-incomplete", missingCount);

  protected override string GetCompleteMessage() =>
    Lang.Get("widgetdomain:multiblock-widget-complete");

  protected override void OnProductionTick(float dt) => _ticks++;

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.Lang("widgetdomain:widget-ticks", _ticks);
  }
}
