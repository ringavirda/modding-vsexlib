using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using WidgetNamespace.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace WidgetNamespace.Blocks;

/// <summary>
/// A 3x3 megablock reserved with invisible fillers; a click on any rim cell reaches the principal
/// here and is counted by <see cref="BlockEntityWidget"/>.
/// </summary>
[BlockRegister]
public class BlockWidget
  : BlockFilledMegastructure,
    IFillerInteractionTarget,
    IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "widget")
        .Class<BlockWidget>()
        .EntityClass<BlockEntityWidget>()
        .Material(EnumBlockMaterial.Wood)
        .MaxStackSize(1)
        .Shape("game:block/wood/mechanics/largegear3")
        .Behavior("ExOrientable")
        .SideVariant()
        .CreativeCommon("*-n")
        // A vertical 3x3 face: the principal is the hub, the eight fillers the rim.
        .FillerOffsets(
          StructureFootprint.Layout(f =>
            f.Origin(-1, 1)
              .Face(
                0,
                """
                ###
                #0#
                ###
                """
              )
          )
        )
        .SolidNonOpaque(),
    ];

  public override int StructureAngle =>
    ExOrientation.AngleFromSide(Variant["side"]);

  /// <summary>Every cell of the footprint answers the same way: a click counts and chats a line.</summary>
  public bool OnFillerInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) {
    if (world.Side != EnumAppSide.Server)
      return true;
    if (
      world.BlockAccessor.GetBlockEntity(principalSel.Position)
      is BlockEntityWidget widget
    )
      widget.RegisterClick();
    (byPlayer as IServerPlayer)?.SendMessage(
      GlobalConstants.GeneralChatGroup,
      Lang.Get("widgetdomain:widget-clicked"),
      EnumChatType.Notification
    );
    return true;
  }

  public bool OnFillerInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) => false;

  public void OnFillerInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) { }

  public WorldInteraction[] GetFillerInteractionHelp(
    IWorldAccessor world,
    BlockSelection principalSel,
    IPlayer forPlayer,
    BlockPos clickedCell
  ) => [];

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) =>
    OnFillerInteractStart(world, byPlayer, blockSel, blockSel.Position)
    || base.OnBlockInteractStart(world, byPlayer, blockSel);
}
