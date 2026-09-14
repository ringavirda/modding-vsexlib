using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using WidgetNamespace.BlockEntities;
using Vintagestory.API.Common;

namespace WidgetNamespace.Blocks;

/// <summary>
/// The widget core: a designed multiblock (cobble walls, the front open) whose block entity ticks
/// while the structure is complete.
/// </summary>
[BlockRegister]
public class BlockWidget : Block, IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "widget")
        .Class<BlockWidget>()
        .EntityClass<BlockEntityWidget>()
        .Material(EnumBlockMaterial.Stone)
        .MaxStackSize(1)
        .Shape("game:block/stone/quern/complete")
        .TextureAll("game:block/stone/rock/granite1")
        .Behavior("MultiblockStructure")
        .Behavior("ExOrientable")
        .SideVariant()
        .CreativeCommon("*-n")
        // Cobble walls either side and behind, the front open for the player.
        .MultiblockLayout(l =>
          l.Origin(-1, -1)
            .Legend('#', "game:cobblestone-*")
            .Legend('C', $"{domain}:widget-*")
            .Core('C')
            .Layer(
              0,
              """
              # # #
              # C #
              # . #
              """
            )
        )
        .SolidNonOpaque(),
    ];
}
