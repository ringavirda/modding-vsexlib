using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Blocks;

/// <summary>Vanilla's <c>BlockStairs</c> (the <c>verticalorientation</c>,
/// <c>horizontalorientation</c> and <c>cover</c> groups, the <c>noDownVariant</c> attribute) for a
/// block whose code has a dash; every code it places, drops, picks, rotates or flips to is built
/// from the whole code rather than its first dash-segment.</summary>
[BlockRegister("ExStairs", PrefixModId = false)]
public class BlockExStairs : BlockStairs {
  private static readonly string[] Orientation =
  [
    "verticalorientation",
    "horizontalorientation",
  ];

  private static readonly string[] Canonical =
  [
    "verticalorientation",
    "horizontalorientation",
    "cover",
  ];

  private bool _hasDownVariant = true;

  /// <summary>Reads <c>noDownVariant</c>: when true, a placement never lands upside down and a
  /// vertical flip keeps <c>up</c>.</summary>
  /// <param name="api">The side's API.</param>
  public override void OnLoaded(ICoreAPI api) {
    base.OnLoaded(api);
    _hasDownVariant = Attributes?.IsTrue("noDownVariant") != true;
  }

  /// <summary>Places the variant facing the player's suggested horizontal orientation, upside
  /// down when a side face is hit in its upper half and the block has a down variant, or wearing
  /// the clicked face when it is the top or bottom.</summary>
  /// <returns>False when <c>CanPlaceBlock</c> refuses, with <paramref name="failureCode"/> set,
  /// or when the variant is not registered.</returns>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    BlockFacing[] horVer = SuggestedHVOrientation(byPlayer, blockSel);
    horVer[1] =
      blockSel.Face.IsVertical ? blockSel.Face
      : blockSel.HitPosition.Y < 0.5 || !_hasDownVariant ? BlockFacing.UP
      : BlockFacing.DOWN;

    Block? placed = world.BlockAccessor.GetBlock(
      this.WithVariants(Orientation, [horVer[1].Code, horVer[0].Code])
    );
    if (placed == null)
      return false;
    world.BlockAccessor.SetBlock(placed.BlockId, blockSel.Position);
    return true;
  }

  /// <summary>The <c>up</c>, <c>north</c>, <c>free</c> variant, whatever variant was broken.</summary>
  /// <exception cref="System.NullReferenceException">That variant is not registered.</exception>
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  ) => [CanonicalStack(world)];

  /// <summary>The <c>up</c>, <c>north</c>, <c>free</c> variant, as <see cref="GetDrops"/>.</summary>
  /// <exception cref="System.NullReferenceException">That variant is not registered.</exception>
  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
    CanonicalStack(world);

  private ItemStack CanonicalStack(IWorldAccessor world) =>
    new(
      world.BlockAccessor.GetBlock(
        this.WithVariants(Canonical, ["up", "north", "free"])
      )
    );

  /// <summary>The variant whose <c>horizontalorientation</c> is turned by
  /// <paramref name="angle"/> degrees about the vertical axis.</summary>
  /// <param name="angle">Degrees, a multiple of 90 in 0..270.</param>
  /// <exception cref="System.NullReferenceException">The block's
  /// <c>horizontalorientation</c> is no facing.</exception>
  public override AssetLocation GetRotatedBlockCode(int angle) {
    BlockFacing facing = BlockFacing.FromCode(Variant["horizontalorientation"]);
    BlockFacing turned = BlockFacing.HORIZONTALS_ANGLEORDER[
      ((360 - angle) / 90 + facing.HorizontalAngleIndex) % 4
    ];
    return this.WithVariant("horizontalorientation", turned.Code);
  }

  /// <summary>The variant with <c>verticalorientation</c> <c>down</c> when it is <c>up</c> and
  /// the block has a down variant, else <c>up</c>.</summary>
  public override AssetLocation GetVerticallyFlippedBlockCode() =>
    this.WithVariant(
      "verticalorientation",
      Variant["verticalorientation"] == "up" && _hasDownVariant ? "down" : "up"
    );

  /// <summary>The variant mirrored across <paramref name="axis"/>: <c>horizontalorientation</c>
  /// reversed when it lies on that axis.</summary>
  /// <param name="axis">The axis mirrored across.</param>
  /// <returns>The block's own code when the orientation lies on another axis.</returns>
  /// <exception cref="System.NullReferenceException">The block's
  /// <c>horizontalorientation</c> is no facing.</exception>
  public override AssetLocation GetHorizontallyFlippedBlockCode(EnumAxis axis) {
    BlockFacing facing = BlockFacing.FromCode(Variant["horizontalorientation"]);
    return facing.Axis == axis
      ? this.WithVariant("horizontalorientation", facing.Opposite.Code)
      : Code;
  }
}
