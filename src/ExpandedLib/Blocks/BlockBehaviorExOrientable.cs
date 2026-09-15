using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks;

/// <summary>Places a block wearing the <c>side</c> variant the player's look implies; the family's
/// replacement for vanilla's <c>HorizontalOrientable</c>.</summary>
[BlockBehaviorRegister("ExOrientable", PrefixModId = false)]
public class BlockBehaviorExOrientable : BlockBehavior {
  /// <summary>The variant group a player-oriented block writes its facing to.</summary>
  public const string SideVariant = "side";

  /// <summary>The variant group a network-oriented block writes its token to.</summary>
  public const string OrientationVariant = "orientation";

  private ExOrientationScheme _scheme = ExOrientations.Face;

  public BlockBehaviorExOrientable(Block block)
    : base(block) { }

  /// <summary>Whether this block also accepts the vertical faces.</summary>
  public bool IsOmni { get; private set; }

  /// <summary>Whether the network decides this block's orientation rather than the player.</summary>
  public bool IsNetworkOriented { get; private set; }

  /// <summary>The variant group this block's orientation lives in, by mode.</summary>
  public string VariantKey =>
    IsNetworkOriented ? OrientationVariant : SideVariant;

  /// <summary>The token vocabulary this block declares.</summary>
  public ExOrientationScheme Scheme => _scheme;

  /// <summary>The <c>scheme</c> name this block declared that names no scheme, or null when it
  /// named one or named none.</summary>
  public string? UnresolvedScheme { get; private set; }

  /// <inheritdoc/>
  public override void Initialize(JsonObject properties) {
    base.Initialize(properties);

    string mode = properties?["mode"].AsString("horizontal") ?? "horizontal";
    IsOmni = mode == "omni";
    IsNetworkOriented = mode == "network";

    if (IsNetworkOriented) {
      // The scheme is named explicitly; not derivable from the mode alone.
      string? schemeName = properties?["scheme"].AsString();
      ExOrientationScheme? named = ExOrientations.All.FirstOrDefault(s =>
        s.Name == schemeName
      );
      // Falls back to Axis rather than throwing on an unresolved name.
      UnresolvedScheme = named == null ? schemeName : null;
      _scheme = named ?? ExOrientations.Axis;
    } else
      _scheme = IsOmni ? ExOrientations.FaceAll : ExOrientations.Face;
  }

  /// <summary>Swaps the block at <paramref name="pos"/> to the variant wearing
  /// <paramref name="token"/>.</summary>
  /// <returns>False when the token is outside the block's scheme or resolves to no other block.</returns>
  public bool ApplyOrientation(IWorldAccessor world, BlockPos pos, string token) {
    if (!_scheme.Contains(token))
      return false;

    Block? oriented = world.BlockAccessor.GetBlock(
      block.CodeWithVariant(VariantKey, token)
    );
    if (oriented == null || oriented.BlockId == block.BlockId)
      return false;

    world.BlockAccessor.ExchangeBlock(oriented.BlockId, pos);
    world.BlockAccessor.MarkBlockDirty(pos);
    return true;
  }

  /// <inheritdoc/>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref EnumHandling handling,
    ref string failureCode
  ) {
    // A network block goes down in whatever variant the stack carries.
    if (IsNetworkOriented)
      return true;

    handling = EnumHandling.PreventDefault;

    string token = TokenFor(byPlayer, blockSel);
    Block? oriented = world.BlockAccessor.GetBlock(
      block.CodeWithVariant(VariantKey, token)
    );

    // A missing state is an authoring mistake: refuse the placement rather than dereference null.
    if (oriented == null) {
      world.Logger.Error(
        "[exlib] {0} declares ExOrientable but has no '{1}' state '{2}' - "
          + "placement refused. Declare the variant group from ExOrientations.{3}.",
        block.Code,
        VariantKey,
        token,
        _scheme.Name
      );
      failureCode = "cantplace";
      return false;
    }

    world.BlockAccessor.SetBlock(
      oriented.BlockId,
      blockSel.Position,
      itemstack
    );
    return true;
  }

  /// <summary>The token the placement should wear.</summary>
  private string TokenFor(IPlayer byPlayer, BlockSelection blockSel) {
    if (IsOmni && blockSel.Face is { IsVertical: true } face)
      return face == BlockFacing.UP ? "u" : "d";

    BlockFacing horizontal = Block.SuggestedHVOrientation(byPlayer, blockSel)[
      0
    ];
    return ExOrientation.TokenOf(horizontal, asLetter: true);
  }

  /// <inheritdoc/>
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    ref float dropChanceMultiplier,
    ref EnumHandling handling
  ) {
    // One item for every facing, so drops stack in the inventory.
    handling = EnumHandling.PreventDefault;
    return [CanonicalStack(world)];
  }

  /// <inheritdoc/>
  public override ItemStack OnPickBlock(
    IWorldAccessor world,
    BlockPos pos,
    ref EnumHandling handling
  ) {
    // The same canonical stack GetDrops returns.
    handling = EnumHandling.PreventDefault;
    return CanonicalStack(world);
  }

  /// <summary>The base-token stack for this block, returned by both the drop and the
  /// middle-click.</summary>
  public ItemStack CanonicalStack(IWorldAccessor world) {
    Block canonical =
      world.BlockAccessor.GetBlock(
        block.CodeWithVariant(VariantKey, _scheme.Tokens[0])
      ) ?? block;
    return new ItemStack(canonical);
  }
}
