using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Molten;

/// <summary>Finds the blocks that hold molten metal and gives each a <see cref="BlockBehaviorMoltenBurn"/>.</summary>
/// <remarks>A block holds when its block entity class implements <see cref="IMoltenCell"/> or
/// <see cref="IMoltenSurface"/>, when it declares a block entity behaviour that does, when it is a structure
/// filler (any filler cell may host a <see cref="BEBehaviorMoltenCell"/>), or when its filler footprint hosts
/// one.</remarks>
public static class MoltenHolders {
  /// <summary>Whether <paramref name="block"/> holds molten metal by the rules of this type.</summary>
  /// <param name="block">The block to test.</param>
  /// <param name="classes">The class registry its entity and behaviour names resolve in.</param>
  public static bool IsHolder(Block block, IClassRegistryAPI classes) {
    if (
      block.EntityClass is { } key
      && classes.GetBlockEntity(key) is { } entityType
      && (
        HoldsMetal(entityType)
        || typeof(BlockEntityStructureFiller).IsAssignableFrom(entityType)
      )
    )
      return true;
    if ((block.BlockEntityBehaviors ?? []).Any(b => HostsCell(classes, b.Name)))
      return true;
    return block is IFillerHost host
      && StructureFillers
        .ReadOffsets(host.FillerOffsets)
        .Any(o => (o.Behaviors ?? []).Any(b => HostsCell(classes, b.Code)));
  }

  /// <summary>Attaches a <see cref="BlockBehaviorMoltenBurn"/> to every loaded block that
  /// <see cref="IsHolder"/> accepts and does not carry one. Run once the blocks are registered, from
  /// <c>AssetsFinalize</c>.</summary>
  /// <returns>How many blocks gained the behaviour.</returns>
  public static int Attach(ICoreAPI api) {
    int attached = 0;
    foreach (Block block in api.World.Blocks)
      if (
        block is { Code: not null }
        && block.GetBehavior<BlockBehaviorMoltenBurn>() == null
        && IsHolder(block, api.ClassRegistry)
      ) {
        var burn = new BlockBehaviorMoltenBurn(block);
        burn.Initialize(new JsonObject(new JObject()));
        block.BlockBehaviors = [.. block.BlockBehaviors, burn];
        block.CollectibleBehaviors = [.. block.CollectibleBehaviors, burn];
        attached++;
      }
    return attached;
  }

  private static bool HoldsMetal(Type type) =>
    typeof(IMoltenCell).IsAssignableFrom(type)
    || typeof(IMoltenSurface).IsAssignableFrom(type);

  private static bool HostsCell(IClassRegistryAPI classes, string name) =>
    classes.GetBlockEntityBehaviorClass(name) is { } type && HoldsMetal(type);
}
