using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using WasherPunch.BlockEntities;

namespace WasherPunch.Blocks;

/// <summary>A hand screw fly press with two selection boxes: the body opens the station window, the
/// fly makes one stroke.</summary>
[BlockRegister]
public partial class BlockWasherPunch : Block, IExBlockDefProvider {
  /// <summary>Index of the body among the selection boxes.</summary>
  public const int BodyBox = 0;

  /// <summary>Index of the fly among the selection boxes.</summary>
  public const int FlyBox = 1;

  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "press", "press")
        .Class<BlockWasherPunch>()
        .EntityClass<BlockEntityWasherPunch>()
        .Behavior("BlockEntityInteract")
        .Material(EnumBlockMaterial.Metal)
        .MaxStackSize(1)
        .Shape("washerpunch:block/press")
        .CreativeTab("general", "*")
        .CreativeTab("items", "*")
        .CreativeTab(domain, "*")
        .SelectionBox(0.1875f, 0f, 0.1875f, 0.8125f, 0.875f, 0.8125f)
        .SelectionBox(0f, 0.875f, 0.375f, 1f, 1f, 0.625f)
        .CollisionBox(0.1875f, 0f, 0.1875f, 0.8125f, 0.875f, 0.8125f)
        .CollisionBox(0f, 0.375f, 0.375f, 1f, 1f, 0.625f)
        .Sound("place", "game:block/anvil")
        .Sound("break", "game:block/anvil")
        .Sound("hit", "game:block/anvil")
        .Sound("walk", "game:walk/stone"),
    ];

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityWasherPunch press
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    if (blockSel.SelectionBoxIndex == FlyBox) {
      if (world.Side == EnumAppSide.Server)
        press.Stroke();
    } else {
      press.ToggleWindow(byPlayer);
    }
    return true;
  }
}
