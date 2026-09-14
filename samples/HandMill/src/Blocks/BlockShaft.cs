using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using HandMill.BlockEntities;
using Vintagestory.API.Common;

namespace HandMill.Blocks;

/// <summary>
/// A straight wooden shaft: the mpenergy network's transmission run, riding vanilla's own axle mesh.
/// It carries the drive along a line and contributes a little rotational inertia
/// (<see cref="BlockEntities.BlockEntityShaft"/>), so a shaft line buffers slightly.
/// </summary>
[BlockRegister]
public partial class BlockShaft : BlockNetworkNode, IExBlockDefProvider {
  public override string NetworkType => "mpenergy";

  /// <summary>Vanilla's axle shape runs along X, not Z: <c>we</c> is the unrotated mesh and
  /// <c>ns</c> the 90-degree turn.</summary>
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "shaft")
        .Class<BlockShaft>()
        .EntityClass<BlockEntities.BlockEntityShaft>()
        .Material(EnumBlockMaterial.Wood)
        .MaxStackSize(64)
        .VariantGroup("type", "shaft")
        .VariantGroup("orientation", "ns", "we", "ud")
        .NetworkOriented()
        .ShapeByType("*-we", "game:block/wood/mechanics/axle", rotateY: 0)
        .ShapeByType("*-ns", "game:block/wood/mechanics/axle", rotateY: 90)
        .ShapeByType("*-ud", "game:block/wood/mechanics/axle", rotateZ: 90)
        .CreativeCommon("*-ns")
        .SingleCollisionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 1f)
        .SingleSelectionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 1f)
        .SideSolid(false)
        .SideOpaque(false),
    ];
}
