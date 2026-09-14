using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using WidgetNamespace.BlockEntities;
using Vintagestory.API.Common;

namespace WidgetNamespace.Blocks;

/// <summary>
/// A straight network node riding vanilla's own axle mesh: the widgetnet network's transmission
/// run, carrying it along a line.
/// </summary>
[BlockRegister]
public class BlockWidget : BlockNetworkNode, IExBlockDefProvider {
  public override string NetworkType => "widgetnet";

  /// <summary>Vanilla's axle shape runs along X, not Z: <c>we</c> is the unrotated mesh and
  /// <c>ns</c> the 90-degree turn.</summary>
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "widget")
        .Class<BlockWidget>()
        .EntityClass<BlockEntityWidget>()
        .Material(EnumBlockMaterial.Wood)
        .MaxStackSize(64)
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
