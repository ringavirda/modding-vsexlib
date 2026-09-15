using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// Passthrough pipe: carries gas straight through a wall. A connector butted against a solid
/// (non-air) block is not treated as a leak. A chimney capping its open top face draws gas out of
/// the run (<see cref="IChimneyVentable"/>).
/// </summary>
[BlockRegister]
public partial class BlockPipePassthrough : BlockPipe, IChimneyVentable {
  /// <summary>Passthroughs never burst.</summary>
  public override float BurstPressure => float.MaxValue;

  /// <summary>The defs this class declares itself, used only to derive
  /// <see cref="BlockNetworkNode.AllowedOrientations"/>; empty for the <c>exlib</c> domain.</summary>
  public static new IEnumerable<ExBlockDef> Definitions(string domain) =>
    domain == "exlib" ? [] : Passthroughs(domain, tier: null);

  /// <summary>The passthrough + passthrough-bend blocktypes of one <paramref name="tier"/> under
  /// <paramref name="domain"/>.</summary>
  /// <param name="sheet">The sheet texture this tier's pipe is made of; defaults to vanilla's
  /// corroded sheet.</param>
  public static IEnumerable<ExBlockDef> Passthroughs(
    string domain,
    string? tier,
    string? sheet = null
  ) =>
    [
      Passthrough(domain, tier, sheet ?? DefaultSheet),
      PassthroughBend(domain, tier, sheet ?? DefaultSheet),
    ];

  /// <summary>The sheet texture used when a tier names none; overrides the <c>normal4</c> key
  /// only.</summary>
  private const string DefaultSheet = "game:block/metal/corroded/normal4";

  /// <summary>The brick shell both passthrough blocktypes are drawn with, in exlib's own asset tree,
  /// shared by every tier.</summary>
  private const string PassthroughShape = "exlib:pipe/passthrough";

  /// <summary>The bend counterpart of <see cref="PassthroughShape"/>, shared the same way.</summary>
  private const string PassthroughBendShape = "exlib:pipe/passthroughbend";

  private static readonly string[] Bricks =
  [
    "fire",
    "black",
    "brown",
    "cream",
    "gray",
    "orange",
    "red",
    "tan",
  ];

  // The ceramic-brick surface shared by both passthrough blocktypes.
  private static ExBlockDef Brick(
    string domain,
    string? tier,
    string sheet,
    string assetName,
    string creative,
    string handbookGroup
  ) {
    ExBlockDef def = ExBlockDef
      .Create(domain, "pipe", assetName)
      // Typed, and safe across assemblies: KeyFor resolves the domain from the TYPE's assembly.
      .Class<BlockPipePassthrough>()
      .EntityClass<BlockEntityPipePassthrough>()
      .Material(EnumBlockMaterial.Ceramic)
      .Sound("walk", "game:walk/stone")
      .Sound("place", "game:block/ceramicplace")
      .SoundByTool(
        EnumTool.Pickaxe,
        "game:block/rock-hit-pickaxe",
        "game:block/rock-break-pickaxe"
      )
      .MaxStackSize(1)
      .CreativeTab("general", creative)
      .CreativeTab(domain, creative)
      // Grouped within a tier: an undomained groupBy selector is qualified with the block's own domain.
      .Handbook(
        tier == null ? $"pipe-{handbookGroup}" : $"pipe-{tier}-{handbookGroup}"
      )
      .Behavior("Lockable")
      .TextureByType(
        "*",
        "front1",
        "game:block/clay/brick/four/running/cream1",
        "game:block/clay/brick/four/running/{brick}1"
      )
      .Texture("normal4", sheet)
      .RenderPass("OpaqueNoCull")
      .FaceCullMode("NeverCull")
      .LightAbsorption(0)
      .SideSolid(true)
      .SideOpaque(false);

    // Declared before `type`; every selector below leads with `*` to absorb it.
    return tier == null ? def : def.VariantGroup("tier", tier);
  }

  private static ExBlockDef Passthrough(
    string domain,
    string? tier,
    string sheet
  ) =>
    Brick(
        domain,
        tier,
        sheet,
        BlockPipe.Asset(tier, "passthrough"),
        "*-passthrough-*-ns",
        "passthrough-*"
      )
      .VariantGroup("type", "passthrough")
      .VariantGroup("brick", Bricks)
      .VariantGroup("orientation", "ns", "we", "ud")
      .NetworkOriented()
      .ShapeByType("*-passthrough-*-ns", PassthroughShape, rotateY: 0)
      .ShapeByType("*-passthrough-*-we", PassthroughShape, rotateY: 90)
      .ShapeByType("*-passthrough-*-ud", PassthroughShape, rotateX: 90);

  private static ExBlockDef PassthroughBend(
    string domain,
    string? tier,
    string sheet
  ) {
    const string s = PassthroughBendShape;
    return Brick(
        domain,
        tier,
        sheet,
        BlockPipe.Asset(tier, "passthroughbend"),
        "*-passthroughbend-*-nw",
        "passthroughbend-*"
      )
      .VariantGroup("type", "passthroughbend")
      .VariantGroup("brick", Bricks)
      .VariantGroup(
        "orientation",
        "nw",
        "se",
        "en",
        "ws",
        "un",
        "us",
        "uw",
        "ue",
        "dn",
        "ds",
        "dw",
        "de"
      )
      .NetworkOriented()
      .ShapeByType("*-passthroughbend-*-nw", s)
      .ShapeByType("*-passthroughbend-*-en", s, rotateY: 270)
      .ShapeByType("*-passthroughbend-*-se", s, rotateY: 180)
      .ShapeByType("*-passthroughbend-*-ws", s, rotateY: 90)
      .ShapeByType("*-passthroughbend-*-dn", s, rotateZ: 90)
      .ShapeByType("*-passthroughbend-*-de", s, rotateY: 270, rotateZ: 90)
      .ShapeByType("*-passthroughbend-*-ds", s, rotateY: 180, rotateZ: 90)
      .ShapeByType("*-passthroughbend-*-dw", s, rotateY: 90, rotateZ: 90)
      .ShapeByType("*-passthroughbend-*-un", s, rotateZ: 270)
      .ShapeByType("*-passthroughbend-*-ue", s, rotateY: 270, rotateZ: 270)
      .ShapeByType("*-passthroughbend-*-us", s, rotateY: 180, rotateZ: 270)
      .ShapeByType("*-passthroughbend-*-uw", s, rotateY: 90, rotateZ: 270);
  }

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => SideSolid[blockFace.Index] || HasConnectorAt(blockFace);

  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  ) { }
}
