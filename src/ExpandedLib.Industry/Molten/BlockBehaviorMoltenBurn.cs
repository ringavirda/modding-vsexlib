using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Molten;

/// <summary>Marks a block as a molten holder: an entity touching its top face while its hottest metal is hot
/// takes a <see cref="MoltenBurn"/>, and land creatures path round it. Attached by
/// <see cref="MoltenHolders.Attach"/> or declared as <c>{ "name": "MoltenBurn" }</c>.</summary>
/// <remarks>The hooks are Harmony patches on <see cref="Block.OnEntityCollide"/> and
/// <see cref="Block.GetTraversalCost"/>, so a block class that overrides either without calling the base
/// escapes them.</remarks>
[BlockBehaviorRegister("MoltenBurn", PrefixModId = false)]
public class BlockBehaviorMoltenBurn(Block block) : BlockBehavior(block) {
  /// <summary>Burns <paramref name="entity"/> when it touches the top face of the block at
  /// <paramref name="pos"/>, on the server, while the block entity's hottest metal is hot.</summary>
  /// <param name="world">The world the collision happens in.</param>
  /// <param name="entity">The colliding entity.</param>
  /// <param name="pos">The block's position.</param>
  /// <param name="facing">The face touched; any but <see cref="BlockFacing.UP"/> does nothing.</param>
  /// <returns>True when the entity took the hit.</returns>
  public bool Collide(
    IWorldAccessor world,
    Entity entity,
    BlockPos pos,
    BlockFacing facing
  ) =>
    world.Side == EnumAppSide.Server
    && facing == BlockFacing.UP
    && MoltenBurn.HottestTemperature(world.BlockAccessor.GetBlockEntity(pos))
      is { } temperature
    && MoltenBurn.Apply(entity, temperature, pos);
}
