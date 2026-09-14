using System.Collections.Generic;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using WidgetNamespace.BlockEntities;
using Vintagestory.API.Common;

namespace WidgetNamespace.Blocks;

/// <summary>A code-first block on a vanilla shape; a sneak-click resets its entity's counter.</summary>
[BlockRegister]
public class BlockWidget : Block, IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "widget")
        .Class<BlockWidget>()
        .EntityClass<BlockEntityWidget>()
        .Material(EnumBlockMaterial.Stone)
        .Shape("game:block/basic/cube")
        .TextureAll("game:block/stone/rock/granite1")
        .Resistance(3.0f)
        .MiningTier(1)
        .CreativeCommon("*")
        .SideVariant()
        .Behavior<BlockBehaviorExOrientable>(),
    ];

  public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel) {
    if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityWidget widget)
      return base.OnBlockInteractStart(world, byPlayer, blockSel);
    Interaction interaction = ExInteraction.Of(world, byPlayer, blockSel);
    if (!interaction.Sneaking)
      return base.OnBlockInteractStart(world, byPlayer, blockSel);
    if (interaction.IsClient)
      return true;
    widget.Reset();
    return true;
  }
}
