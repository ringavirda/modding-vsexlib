using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Structures;

/// <summary>
/// Invisible, solid placeholder that fills the grid cells a mega-block visually occupies. Renders
/// nothing but reroutes every player-facing operation to the principal controller block.
/// </summary>
[BlockRegister]
public partial class BlockStructureFiller
  : Block,
    INetworkConnector,
    IMechanicalPowerBlock,
    IExBlockDefProvider {
  #region Code-first definition

  /// <summary>The structure-filler blocktype: invisible, solid, hidden from the handbook, no drops.</summary>
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "structurefiller")
        .Class<BlockStructureFiller>()
        .EntityClass<BlockEntityStructureFiller>()
        .HandbookExclude()
        .Material(EnumBlockMaterial.Metal)
        .Shape("exlib:block/empty")
        .DrawType("json")
        .SideSolid(true)
        .SideOpaque(false)
        .LightAbsorption(0)
        .Replaceable(500)
        .Resistance(45.0f)
        .NoDrops()
        .SingleCollisionBox(0f, 0f, 0f, 1f, 1f, 1f)
        .SingleSelectionBox(0f, 0f, 0f, 1f, 1f, 1f),
    ];

  #endregion

  // A plain filler is inert (type ""); a principal can turn one cell into a fixed port via its BE.
  public string NetworkType => "";

  public bool HasConnectorAt(BlockFacing face) => false;

  public string NetworkTypeAt(IBlockAccessor world, BlockPos pos) =>
    world.GetBlockEntity(pos) is BlockEntityStructureFiller be
      ? be.PortNetworkType ?? ""
      : "";

  public bool HasConnectorAt(
    IBlockAccessor world,
    BlockPos pos,
    BlockFacing face
  ) =>
    world.GetBlockEntity(pos) is BlockEntityStructureFiller be
    && be.PortFace == face.Code[0].ToString();

  // A footprint cell becomes a mechanical-power intake when its fillerOffsets entry hosts an MP behaviour.
  private static BEBehaviorMPBase? MpBehaviorAt(
    IBlockAccessor world,
    BlockPos pos
  ) => world.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>();

  public bool HasMechPowerConnectorAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
#if GAME_GE_1_22
    ,
    BlockMPBase forBlock
#endif
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(pos)
        is not BlockEntityStructureFiller be
      || be.GetBehavior<BEBehaviorMPBase>() == null
      || be.HostedBehaviors == null
    )
      return false;
    // A port declared on one face also accepts its opposite face, unless `through: false`.
    foreach (FillerBehavior b in be.HostedBehaviors)
      if (
        b.ConnectorFace != null
        && (
          b.ConnectorFace == face
          || (
            b.ConnectorFace.Opposite == face
            && (b.Properties?["through"].AsBool(true) ?? true)
          )
        )
      )
        return true;
    return false;
  }

  public void DidConnectAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
  ) { }

  /// <summary>The MP network of the hosted port behaviour at this cell, or null when the cell hosts none.</summary>
  public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
    MpBehaviorAt(world.BlockAccessor, pos)?.Network;

  // Not an attachment surface by default; a cell opts back in via its fillerOffsets "allowAttach" flag.
  public override bool CanAttachBlockAt(
    IBlockAccessor blockAccessor,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi? attachmentArea = null
  ) {
    if (
      blockAccessor.GetBlockEntity(pos) is BlockEntityStructureFiller be
      && be.AllowAttach
    )
      return base.CanAttachBlockAt(
        blockAccessor,
        block,
        pos,
        blockFace,
        attachmentArea
      );
    return false;
  }

  // A cell the mega-block only partially fills carries its own boxes on the BE; a full-cube cell falls
  // back to the definition's box.
  public override Cuboidf[] GetCollisionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    blockAccessor.GetBlockEntity(pos)
      is BlockEntityStructureFiller { CollisionBoxes: { Length: > 0 } boxes }
      ? boxes
      : base.GetCollisionBoxes(blockAccessor, pos);

  public override Cuboidf[] GetSelectionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    blockAccessor.GetBlockEntity(pos)
      is BlockEntityStructureFiller { CollisionBoxes: { Length: > 0 } boxes }
      ? boxes
      : base.GetSelectionBoxes(blockAccessor, pos);

  /// <summary>Inherits the principal's interaction sounds so the invisible footprint is not silent.</summary>
  public override BlockSounds GetSounds(
    IBlockAccessor blockAccessor,
    BlockSelection blockSel,
    ItemStack? stack = null
  ) {
    if (
      blockSel?.Position != null
      && blockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityStructureFiller be
      && be.Principal != null
    ) {
      Block principal = blockAccessor.GetBlock(be.Principal);
      if (principal.Id != 0 && principal != this)
        return principal.GetSounds(blockAccessor, blockSel, stack);
    }
    return base.GetSounds(blockAccessor, blockSel, stack);
  }

  /// <summary>Resolves the principal position and block; false when orphaned.</summary>
  private bool TryGetPrincipal(
    IWorldAccessor world,
    BlockPos pos,
    out BlockPos principalPos,
    out Block principalBlock
  ) {
    principalPos = null!;
    principalBlock = null!;
    if (
      world.BlockAccessor.GetBlockEntity(pos)
        is not BlockEntityStructureFiller be
      || be.Principal == null
    )
      return false;

    principalPos = be.Principal;
    principalBlock = world.BlockAccessor.GetBlock(principalPos);
    return principalBlock.Id != 0;
  }

  private static BlockSelection Repoint(
    BlockSelection sel,
    BlockPos principalPos
  ) {
    BlockSelection clone = sel.Clone();
    clone.Position = principalPos;
    return clone;
  }

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    // A held placeable block skips the forward, except a liquid container, which the principal must see.
    ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
    bool placingBlock =
      held?.Block != null && held.Collectible is not BlockLiquidContainerBase;

    // Cell-aware principals (IFillerInteractionTarget) also get the clicked cell.
    if (
      !placingBlock
      && TryGetPrincipal(world, blockSel.Position, out var pp, out var pb)
    ) {
      BlockSelection psel = Repoint(blockSel, pp);
      bool handled = pb is IFillerInteractionTarget target
        ? target.OnFillerInteractStart(world, byPlayer, psel, blockSel.Position)
        : pb.OnBlockInteractStart(world, byPlayer, psel);
      if (handled)
        return true;
    }

    // Unhandled, allowAttach cell: let the engine do its normal placement.
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityStructureFiller be
      && be.AllowAttach
    )
      return false;

    // Unhandled, non-buildable cell: swallow the click.
    if (placingBlock)
      return true;

    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }

  public override bool OnBlockInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (!TryGetPrincipal(world, blockSel.Position, out var pp, out var pb))
      return base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel);
    BlockSelection psel = Repoint(blockSel, pp);
    return pb is IFillerInteractionTarget target
      ? target.OnFillerInteractStep(
        secondsUsed,
        world,
        byPlayer,
        psel,
        blockSel.Position
      )
      : pb.OnBlockInteractStep(secondsUsed, world, byPlayer, psel);
  }

  public override void OnBlockInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (!TryGetPrincipal(world, blockSel.Position, out var pp, out var pb)) {
      base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
      return;
    }
    BlockSelection psel = Repoint(blockSel, pp);
    if (pb is IFillerInteractionTarget target)
      target.OnFillerInteractStop(
        secondsUsed,
        world,
        byPlayer,
        psel,
        blockSel.Position
      );
    else
      pb.OnBlockInteractStop(secondsUsed, world, byPlayer, psel);
  }

  public override float OnGettingBroken(
    IPlayer player,
    BlockSelection blockSel,
    ItemSlot itemslot,
    float remainingResistance,
    float dt,
    int counter
  ) {
    IWorldAccessor world = player?.Entity?.World ?? api.World;
    if (!TryGetPrincipal(world, blockSel.Position, out var pp, out var pb))
      return base.OnGettingBroken(
        player,
        blockSel,
        itemslot,
        remainingResistance,
        dt,
        counter
      );
    return pb.OnGettingBroken(
      player,
      Repoint(blockSel, pp),
      itemslot,
      remainingResistance,
      dt,
      counter
    );
  }

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    // Breaking any filler breaks the whole structure via the principal's OnBlockBroken.
    if (!TryGetPrincipal(world, pos, out var pp, out var pb)) {
      base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
      return;
    }
    pb.OnBlockBroken(world, pp, byPlayer, dropQuantityMultiplier);

    // Removes this cell in case the principal's break did not clear it.
    if (world.BlockAccessor.GetBlock(pos).Id == BlockId)
      world.BlockAccessor.SetBlock(0, pos);
  }

  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) {
    if (!TryGetPrincipal(world, pos, out var pp, out var pb))
      return base.OnPickBlock(world, pos);
    return pb.OnPickBlock(world, pp);
  }

  // The principal owns all drops.
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  ) => [];

  // The look-at info HUD shows the principal's text instead of the invisible filler's.
  public override string GetPlacedBlockInfo(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer forPlayer
  ) {
    if (!TryGetPrincipal(world, pos, out var pp, out var pb))
      return base.GetPlacedBlockInfo(world, pos, forPlayer);
    return pb.GetPlacedBlockInfo(world, pp, forPlayer);
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) {
    if (!TryGetPrincipal(world, selection.Position, out var pp, out var pb))
      return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
    BlockSelection psel = Repoint(selection, pp);
    return pb is IFillerInteractionTarget target
      ? target.GetFillerInteractionHelp(
        world,
        psel,
        forPlayer,
        selection.Position
      )
      : pb.GetPlacedBlockInteractionHelp(world, psel, forPlayer);
  }
}
