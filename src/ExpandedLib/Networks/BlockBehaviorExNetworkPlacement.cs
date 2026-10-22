using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Networks;

/// <summary>Refuses, on the server, a placement the block's network type would not take, such as a shaft that closes a jamming ring, and tells the placing player why. Declared on the block as <c>{ "name": "ExNetworkPlacement" }</c>; <c>networkType</c> names the type to ask and is the block's own <see cref="INetworkMember.NetworkType"/> when left out. A <see cref="BlockNetworkNode"/> asks its own type without declaring this.</summary>
[BlockBehaviorRegister("ExNetworkPlacement", PrefixModId = false)]
public class BlockBehaviorExNetworkPlacement : BlockBehavior {
  /// <summary>Creates the behaviour for <paramref name="block"/>; <see cref="Initialize"/> reads the declared network type.</summary>
  /// <param name="block">The block the behaviour is declared on.</param>
  public BlockBehaviorExNetworkPlacement(Block block)
    : base(block) { }

  /// <summary>The declared network type whose live networks are asked, or null when the block's own type is asked.</summary>
  public string? NetworkType { get; private set; }

  /// <inheritdoc/>
  public override void Initialize(JsonObject properties) {
    base.Initialize(properties);
    NetworkType = properties?["networkType"].AsString();
  }

  /// <summary>Asks the networks of the block's type bordering <paramref name="pos"/> whether they take this block there, and sends the placing player the refusal.</summary>
  /// <param name="world">The world placing; a client world accepts without asking.</param>
  /// <param name="byPlayer">The player placing; receives the refusal as an in-game error when it is a server player, and may be null.</param>
  /// <param name="pos">The cell the block would fill.</param>
  /// <param name="failureCode">Set to the refusing network's code on a refusal.</param>
  /// <returns>False when a network refuses the member. True when the block declares no type and is no <see cref="INetworkMember"/>.</returns>
  public bool Accepts(
    IWorldAccessor world,
    IPlayer? byPlayer,
    BlockPos pos,
    ref string failureCode
  ) {
    string? type = NetworkType ?? (block as INetworkMember)?.NetworkType;
    if (type == null || world.Side != EnumAppSide.Server)
      return true;
    var system = world.Api?.ModLoader.GetModSystem<BlockNetworkModSystem>();
    if (
      system == null
      || system.CanPlaceMember(
        world.BlockAccessor,
        pos,
        block,
        type,
        ref failureCode
      )
    )
      return true;
    SendRefusal(byPlayer, failureCode);
    return false;
  }

  /// <summary>Sends <paramref name="byPlayer"/> the <c>game:placefailure-</c> line of <paramref name="failureCode"/> as an in-game error; does nothing for a null or client-side player.</summary>
  /// <param name="byPlayer">The player placing.</param>
  /// <param name="failureCode">The refusal's code; its lang line must ship in every locale.</param>
  internal static void SendRefusal(IPlayer? byPlayer, string failureCode) =>
    (byPlayer as IServerPlayer)?.SendIngameError(
      failureCode,
      Lang.Get("game:placefailure-" + failureCode)
    );

  /// <inheritdoc/>
  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref EnumHandling handling,
    ref string failureCode
  ) {
    if (world.Side != EnumAppSide.Server)
      return true;
    handling = EnumHandling.Handled;
    return Accepts(world, byPlayer, blockSel.Position, ref failureCode);
  }
}
