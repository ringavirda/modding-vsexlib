using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using WasherPunch.BlockEntities;

namespace WasherPunch.Blocks;

/// <summary>A drilling machine: a cell of the "mpshaft" run with two selection boxes, the body opening its
/// window and the lever feeding the cut while it is held.</summary>
[BlockRegister]
public partial class BlockWasherDrill : BlockNetworkNode, IExBlockDefProvider {
  /// <summary>Index of the body among the selection boxes.</summary>
  public const int BodyBox = 0;

  /// <summary>Index of the lever among the selection boxes.</summary>
  public const int LeverBox = 1;

  public override string NetworkType => "mpshaft";

  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "machine", "drill")
        .Class<BlockWasherDrill>()
        .EntityClass<BlockEntityWasherDrill>()
        .Behavior("BlockEntityInteract")
        .Material(EnumBlockMaterial.Metal)
        .MaxStackSize(1)
        .VariantGroup("type", "drill")
        .VariantGroup("orientation", "ns", "we")
        .NetworkOriented()
        .Attribute("machinekind", $"{domain}:drill")
        .ShapeByType("*-ns", $"{domain}:block/drill", rotateY: 0)
        .ShapeByType("*-we", $"{domain}:block/drill", rotateY: 90)
        .CreativeCommon("*-ns")
        .SelectionBox(0.125f, 0f, 0.125f, 0.875f, 0.875f, 0.9375f)
        .SelectionBox(0.6875f, 0.25f, 0.25f, 0.78125f, 0.5625f, 0.34375f)
        .CollisionBox(0.125f, 0f, 0.125f, 0.875f, 0.875f, 0.9375f)
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
      is not BlockEntityWasherDrill drill
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    if (blockSel.SelectionBoxIndex == LeverBox) {
      if (world.Side == EnumAppSide.Server)
        drill.Hold(byPlayer.PlayerUID);
    } else {
      drill.ToggleWindow(byPlayer);
    }
    return true;
  }

  public override bool OnBlockInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      blockSel.SelectionBoxIndex != LeverBox
      || world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is not BlockEntityWasherDrill drill
    )
      return base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel);

    if (world.Side == EnumAppSide.Server)
      drill.Hold(byPlayer.PlayerUID);
    return true;
  }

  public override void OnBlockInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    Release(world, byPlayer, blockSel);
    base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
  }

  public override bool OnBlockInteractCancel(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    EnumItemUseCancelReason cancelReason
  ) {
    Release(world, byPlayer, blockSel);
    return base.OnBlockInteractCancel(
      secondsUsed,
      world,
      byPlayer,
      blockSel,
      cancelReason
    );
  }

  private static void Release(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      world.Side == EnumAppSide.Server
      && world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityWasherDrill drill
    )
      drill.Release(byPlayer.PlayerUID);
  }
}
