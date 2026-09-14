using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace WidgetNamespace.Behaviors;

/// <summary>A block-entity behaviour advancing a persisted counter on its own server tick.</summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorWidget(BlockEntity blockentity)
  : ExBlockEntityBehavior(blockentity) {
  [Persist]
  private int _count;

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    if (api.Side == EnumAppSide.Server)
      Blockentity.RegisterGameTickListener(OnServerTick, 1000);
  }

  private void OnServerTick(float dt) => _count++;

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.Lang("widgetdomain:widget-count", _count);
  }
}
