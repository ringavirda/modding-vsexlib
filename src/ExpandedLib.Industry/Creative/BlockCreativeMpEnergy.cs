using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Creative;

/// <summary>
/// The creative mpenergy source (<c>exlib:creativempenergy</c>): a connector on every face that joins the
/// "mpshaft" run of any member whose crossing meets it, driving the run through its
/// <see cref="BlockEntityCreativeMpEnergy"/>. In no creative tab and no recipe: the scenario <c>power</c> step
/// places it.
/// </summary>
[BlockRegister]
[ExDefDomain("exlib")]
public partial class BlockCreativeMpEnergy
  : Block,
    INetworkConnector,
    IExBlockDefProvider {
  /// <summary>The code of the block, in the <c>exlib</c> domain.</summary>
  public const string Code = "exlib:creativempenergy";

  /// <summary>The creative mpenergy source blocktype: one block, a connector on every face.</summary>
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "creativempenergy")
        .Class<BlockCreativeMpEnergy>()
        .EntityClass<BlockEntityCreativeMpEnergy>()
        .Material(EnumBlockMaterial.Metal)
        .TextureAll("game:block/metal/corroded/normal4")
        .Shape("game:block/basic/cube"),
    ];

  /// <summary>The network type of the run this block drives.</summary>
  public string NetworkType => "mpshaft";

  /// <summary>True on every face.</summary>
  public bool HasConnectorAt(BlockFacing face) => true;
}
