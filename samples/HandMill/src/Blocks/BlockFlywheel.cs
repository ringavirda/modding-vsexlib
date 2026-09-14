using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using HandMill.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HandMill.Blocks;

/// <summary>
/// The flywheel: a 3x3 vertical wheel reserved with invisible fillers, whose block entity hosts a
/// mechanical-energy membership (large storage). A sneak-click brakes the run it is on.
/// </summary>
[BlockRegister]
public partial class BlockFlywheel
  : BlockFilledMegastructure,
    IFillerInteractionTarget,
    IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "flywheel")
        .Class<BlockFlywheel>()
        .EntityClass<BlockEntityFlywheel>()
        .Material(EnumBlockMaterial.Wood)
        .MaxStackSize(1)
        .Behavior("ExOrientable")
        .SideVariant()
        .ShapeByTypePerOrientation("game:block/wood/mechanics/largegear3", 0)
        .CreativeCommon("*-n")
        // A vertical 3x3 wheel: the principal is the hub, the eight fillers the rim, drawn as one
        // front elevation at the wheel's own Z.
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

  /// <summary>Every cell of the wheel answers the same way: a sneak-click brakes it.</summary>
  public bool OnFillerInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) {
    if (!ExInteraction.Of(world, byPlayer, principalSel).Sneaking)
      return false;
    if (
      world.Side == EnumAppSide.Server
      && world.BlockAccessor.GetBlockEntity(principalSel.Position)
        is BlockEntityFlywheel wheel
    )
      wheel.Brake();
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
