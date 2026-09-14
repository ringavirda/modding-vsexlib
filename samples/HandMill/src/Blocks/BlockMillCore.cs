using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using Grains;
using HandMill.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace HandMill.Blocks;

/// <summary>
/// The mill core: a designed multiblock (cobble walls, the shaft cell, the front open) whose block
/// entity grinds grain into flour while the structure is complete and the shaft turns.
/// </summary>
[BlockRegister]
public partial class BlockMillCore : Block, IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "millcore")
        .Class<BlockMillCore>()
        .EntityClass<BlockEntityMillCore>()
        .Material(EnumBlockMaterial.Stone)
        .MaxStackSize(1)
        .Shape("game:block/stone/quern/complete")
        .Texture("grindstoneup", "game:block/stone/rock/granite1")
        .Texture("baseside", "game:block/stone/rock/granite1")
        .Texture("baseup", "game:block/stone/rock/granite1")
        .Texture("wood", "game:block/stone/rock/granite1")
        .Behavior("MultiblockStructure")
        .Behavior("ExOrientable")
        .Behavior<BlockBehaviorGrainInfo>()
        .SideVariant()
        .CreativeCommon("*-n")
        // Cobble walls either side, the shaft cell behind, the front open for the player.
        .MultiblockLayout(l =>
          l.Origin(-1, -1)
            .Legend('#', "game:cobblestone-*")
            .Legend('A', $"{domain}:drive-shaft-*")
            .Legend('C', $"{domain}:millcore-*")
            .Connector('A', BlockFacing.SOUTH)
            .Role('A', MillCellRoles.Axle)
            .Core('C')
            .Layer(
              0,
              """
              # A #
              # C #
              # . #
              """
            )
        )
        .SolidNonOpaque(),
    ];

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityMillCore mill
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);
    if (
      BlockBehaviorMultiblockStructure.TryToggleProjection(
        world,
        byPlayer,
        blockSel.Position
      )
    )
      return true;
    if (world.Side != EnumAppSide.Server)
      return true;
    ItemSlot? active = byPlayer.InventoryManager?.ActiveHotbarSlot;
    if (active == null || active.Empty) {
      mill.TakeFlour(byPlayer);
      return true;
    }
    if (!mill.TryLoad(active))
      (byPlayer as IServerPlayer)?.SendIngameError("handmill-notgrain");
    return true;
  }
}
