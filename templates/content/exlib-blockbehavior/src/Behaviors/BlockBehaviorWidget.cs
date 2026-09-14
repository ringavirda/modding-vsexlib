using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace WidgetNamespace.Behaviors;

/// <summary>Appends one line to a block's placed-block info, attached with
/// <c>.Behavior&lt;BlockBehaviorWidget&gt;()</c> on any block that wants it.</summary>
[BlockBehaviorRegister]
public class BlockBehaviorWidget : BlockBehavior {
  public BlockBehaviorWidget(Block block)
    : base(block) { }

  public override string GetPlacedBlockInfo(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer forPlayer
  ) => Lang.Get("widgetdomain:widget-info", world.BlockAccessor.GetBlock(pos)?.Code);
}
