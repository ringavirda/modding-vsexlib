using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>Refuses, on the server, a placement the block's network type would not take, such as a shaft that closes a jamming ring. Declared on the block as <c>{ "name": "ExNetworkPlacement" }</c>; <c>networkType</c> names the type to ask and is <c>mpshaft</c> when left out.</summary>
[BlockBehaviorRegister("ExNetworkPlacement", PrefixModId = false)]
public class BlockBehaviorExNetworkPlacement : BlockBehavior {
  /// <summary>The network type asked when the block declares none.</summary>
  public const string DefaultNetworkType = "mpshaft";

  public BlockBehaviorExNetworkPlacement(Block block)
    : base(block) { }

  /// <summary>The network type whose live networks are asked.</summary>
  public string NetworkType { get; private set; } = DefaultNetworkType;

  /// <inheritdoc/>
  public override void Initialize(JsonObject properties) {
    base.Initialize(properties);
    NetworkType =
      properties?["networkType"].AsString(DefaultNetworkType)
      ?? DefaultNetworkType;
  }

  /// <summary>Asks the networks of <see cref="NetworkType"/> bordering <paramref name="pos"/> whether they take this block there.</summary>
  /// <param name="world">The world placing; a client world accepts without asking.</param>
  /// <param name="pos">The cell the block would fill.</param>
  /// <param name="failureCode">Set to the refusing network's code on a refusal.</param>
  /// <returns>False when a network refuses the member.</returns>
  public bool Accepts(
    IWorldAccessor world,
    BlockPos pos,
    ref string failureCode
  ) {
    if (world.Side != EnumAppSide.Server)
      return true;
    var system = world.Api?.ModLoader.GetModSystem<BlockNetworkModSystem>();
    return system == null
      || system.CanPlaceMember(
        world.BlockAccessor,
        pos,
        block,
        NetworkType,
        ref failureCode
      );
  }

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
    return Accepts(world, blockSel.Position, ref failureCode);
  }
}
