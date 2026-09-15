using Vintagestory.API.MathTools;

namespace ExpandedLib.Definitions;

/// <summary>
/// The catalogue of vanilla block codes the mod family's multiblock layouts are drawn from.
/// <see cref="ExCodes"/> holds exlib's own blocks; vanilla items named by recipes live in
/// <see cref="ExIngredients"/>.
/// </summary>
public static class VanillaCodes {
  #region Air

  /// <summary>Air: <c>game:air</c>; vanilla ships no <c>air-*</c> variant.</summary>
  public const string Air = "game:air";

  #endregion

  #region Refractory and brick

  // Strictest first: RefractoryTier(n) c Refractory c RefractoryOrFire c AnyBricks.

  /// <summary>Refractory brick pinned to one tier: <c>game:refractorybricks-good-tier{tier}</c>.</summary>
  public static string RefractoryTier(int tier) =>
    $"game:refractorybricks-good-tier{tier}";

  /// <summary>Any refractory brick, any tier: <c>game:refractorybricks-good-tier*</c>; <c>-good-</c>
  /// excludes the damaged state.</summary>
  public const string Refractory = "game:refractorybricks-good-tier*";

  /// <summary>Fire brick: <c>game:claybricks-good-fire</c>.</summary>
  public const string FireBricks = "game:claybricks-good-fire";

  /// <summary>Any refractory brick or fire brick: <c>@(refractorybricks-good-tier.*|claybricks-good-fire)</c>;
  /// the two masonries vanilla marks <c>cokeOvenViable</c>.</summary>
  public const string RefractoryOrFire =
    "@(refractorybricks-good-tier.*|claybricks-good-fire)";

  /// <summary>Any coloured brick course, in any bond and colour: <c>game:brickcourse-*</c>.</summary>
  public const string ColouredBricks = "game:brickcourse-*";

  /// <summary>Any brick a wall can be built from - fire, clinker, refractory of any tier, or a coloured
  /// course.</summary>
  public const string AnyBricks =
    "@(claybricks-(good-fire|clinkerrough)|refractorybricks-good-.*|brickcourse-.*)";

  /// <summary>The refractory grate: <c>game:refractorybrickgrating-good-tier*</c>, not the shorter
  /// <c>refractorygrating</c>.</summary>
  public const string RefractoryGrating =
    "game:refractorybrickgrating-good-tier*";

  #endregion

  #region Slabs

  // Every slab code takes a facing and pins `-free`; a `-snow` slab is a different block.

  /// <summary>A free-standing fire-brick slab facing <paramref name="facing"/>:
  /// <c>game:brickslabs-fire-{facing}-free</c>.</summary>
  public static string FireSlab(BlockFacing facing) =>
    $"game:brickslabs-fire-{facing.Code}-free";

  /// <summary>A free-standing slab of any brick facing <paramref name="facing"/>:
  /// <c>game:brickslabs-*-{facing}-free</c>.</summary>
  public static string AnySlab(BlockFacing facing) =>
    $"game:brickslabs-*-{facing.Code}-free";

  #endregion

  #region Stairs

  // Only the cardinal rotates with the structure; `up`/`down` is invariant under a Y turn.

  /// <summary>A free-standing fire-brick stair: <c>game:brickstairs-fire-{half}-{facing}-free</c>.</summary>
  public static string FireStairs(BlockFacing half, BlockFacing facing) =>
    $"game:brickstairs-fire-{Half(half)}-{Cardinal(facing)}-free";

  /// <summary>A free-standing stair of any brick: <c>game:brickstairs-*-{half}-{facing}-free</c>.</summary>
  public static string AnyStairs(BlockFacing half, BlockFacing facing) =>
    $"game:brickstairs-*-{Half(half)}-{Cardinal(facing)}-free";

  /// <summary>Guards the vertical half of a stair code against the two orientation groups
  /// swapped.</summary>
  private static string Half(BlockFacing half) =>
    half == BlockFacing.UP || half == BlockFacing.DOWN
      ? half.Code
      : throw new System.ArgumentException(
        $"A stair's half must be UP (inverted) or DOWN (normal), not '{half.Code}'. The cardinal the "
          + "step faces is the second argument.",
        nameof(half)
      );

  /// <summary>Guards the horizontal half of a stair code - see <see cref="Half"/>.</summary>
  private static string Cardinal(BlockFacing facing) =>
    facing != BlockFacing.UP && facing != BlockFacing.DOWN
      ? facing.Code
      : throw new System.ArgumentException(
        $"A stair's step must face a cardinal, not '{facing.Code}'. Whether the stair is inverted is "
          + "the first argument.",
        nameof(facing)
      );

  #endregion

  #region Doors and openings

  /// <summary>A coke-oven door facing <paramref name="facing"/>, in any state:
  /// <c>game:cokeovendoor-*-{facing}</c>; facing is the wall face's opposite.</summary>
  public static string CokeOvenDoor(BlockFacing facing) =>
    $"game:cokeovendoor-*-{facing.Code}";

  /// <summary>The coke-oven door that closes the <paramref name="wall"/> face of its cell.</summary>
  public static string Sealing(BlockFacing wall) => CokeOvenDoor(wall.Opposite);

  #endregion

  #region Fuel beds

  /// <summary>A plain fuel bed - vanilla coal pile or nothing: <c>@(air|coalpile)</c>; air is an
  /// accepted occupant.</summary>
  public const string CoalBed = "@(air|coalpile)";

  #endregion
}
