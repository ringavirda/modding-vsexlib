#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>Joins the mechanical behaviours of a placed scene into the lines the scene steps
/// (setups.md, Placement kinds).</summary>
internal static class SetupMechanical {
  /// <summary>Takes every mechanical behaviour out of the lines placement made, then gives each
  /// group of behaviours whose blocks answer <c>HasMechPowerConnectorAt</c> across their shared
  /// faces one new line; a behaviour with no such neighbour joins none.</summary>
  /// <param name="world">The scene's world; its <see cref="MechanicalPowerMod"/> makes the
  /// lines.</param>
  public static void Join(TestWorld world) {
    var power = world.Mods.GetModSystem<MechanicalPowerMod>();
    if (power == null)
      return;
    List<BEBehaviorMPBase> all =
    [
      .. world.BlockEntities.SelectMany(kv =>
        kv.Value.Behaviors.OfType<BEBehaviorMPBase>()
      ),
    ];
    foreach (BEBehaviorMPBase behavior in all)
      behavior.LeaveNetwork();
    ((MechPowerData)ReflectionHelpers.GetField(power, "data")!)
      .networksById.Clear();

    int[] group = [.. Enumerable.Range(0, all.Count)];
    int Root(int i) => group[i] == i ? i : group[i] = Root(group[i]);
    for (int i = 0; i < all.Count; i++)
      for (int j = i + 1; j < all.Count; j++)
        if (Connected(world, all[i].Position, all[j].Position))
          group[Root(j)] = Root(i);

    foreach (var line in Enumerable.Range(0, all.Count).GroupBy(Root)) {
      if (line.Count() < 2)
        continue;
      MechanicalNetwork network = power.CreateNetwork(all[line.First()]);
      foreach (int i in line)
        all[i].JoinNetwork(network);
    }
  }

  private static bool Connected(TestWorld world, BlockPos a, BlockPos b) {
    BlockFacing? face = BlockFacing.ALLFACES.FirstOrDefault(f =>
      a.AddCopy(f).Equals(b)
    );
    if (face == null)
      return false;
    Block blockA = world.GetBlock(a);
    Block blockB = world.GetBlock(b);
    return blockA is IMechanicalPowerBlock powerA
      && blockB is IMechanicalPowerBlock powerB
      && powerA.HasMechPowerConnectorAt(
        world.World,
        a,
        face,
        blockB as BlockMPBase
      )
      && powerB.HasMechPowerConnectorAt(
        world.World,
        b,
        face.Opposite,
        blockA as BlockMPBase
      );
  }
}
#endif
