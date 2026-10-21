using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>One shaft joint the graph asks of an "mpshaft" run, as <see cref="MpShaftStore.TryAddEdge"/> takes it.</summary>
internal readonly record struct MpShaftJoint(
  NodeKey Input,
  NodeKey Output,
  float Ratio,
  int Sign,
  float Stiffness,
  IMpEnergyLink? Link
);

/// <summary>Reads the joints of an "mpshaft" run off the world: a unit joint across every face crossing two members share, and the geared joint of each engaged link.</summary>
internal static class MpShaftJoints {
  /// <summary>The joints among <paramref name="keys"/>, in key order, each pair once.</summary>
  /// <param name="world">The accessor members and their block entities are read through.</param>
  /// <param name="keys">Every node of the run, plus any node a placement would add.</param>
  /// <param name="memberAt">How the cell at a position takes part in the run, or null when it does not.</param>
  /// <param name="linksAt">The links held at a position.</param>
  public static List<MpShaftJoint> Gather(
    IBlockAccessor world,
    IEnumerable<NodeKey> keys,
    System.Func<BlockPos, INetworkMember?> memberAt,
    System.Func<BlockPos, IEnumerable<IMpEnergyLink>> linksAt
  ) {
    var ordered = keys.OrderBy(k => k.Pos.X)
      .ThenBy(k => k.Pos.Y)
      .ThenBy(k => k.Pos.Z)
      .ThenBy(k => k.Slot)
      .ToList();
    var present = new HashSet<NodeKey>(ordered);
    var byCell = ordered.ToLookup(k => k.Pos);
    var joints = new List<MpShaftJoint>();
    var seen = new HashSet<(NodeKey, NodeKey)>();
    foreach (NodeKey key in ordered) {
      INetworkMember? member = memberAt(key.Pos);
      if (member == null)
        continue;
      foreach (NodeCrossing crossing in member.Crossings(world, key)) {
        BlockPos beside = key.Pos.AddCopy(crossing.Face);
        INetworkMember? other = memberAt(beside);
        if (other == null)
          continue;
        foreach (NodeKey near in byCell[beside]) {
          var back = new NodeCrossing(crossing.Face.Opposite, crossing.Edge);
          if (!other.Crossings(world, near).Contains(back))
            continue;
          var pair = Order(key, near);
          if (seen.Add(pair))
            joints.Add(
              new MpShaftJoint(
                pair.Item1,
                pair.Item2,
                1f,
                1,
                Stiffness(world, key.Pos, beside),
                null
              )
            );
        }
      }
    }
    var cells = new HashSet<BlockPos>(ordered.Select(k => k.Pos));
    foreach (
      BlockPos cell in cells.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z)
    )
      foreach (IMpEnergyLink link in linksAt(cell))
        if (
          link.Engaged
          && present.Contains(link.From)
          && present.Contains(link.To)
        )
          joints.Add(
            new MpShaftJoint(link.From, link.To, link.Ratio, link.Sign, 1f, link)
          );
    return joints;
  }

  /// <summary>The links a placed cell holds: its block entity and the behaviours on it.</summary>
  public static IEnumerable<IMpEnergyLink> LinksOf(
    IBlockAccessor world,
    BlockPos pos
  ) {
    BlockEntity? be = world.GetBlockEntity(pos);
    if (be is IMpEnergyLink own)
      yield return own;
    foreach (IMpEnergyLink link in be?.Behaviors.OfType<IMpEnergyLink>() ?? [])
      yield return link;
  }

  /// <summary>Adds <paramref name="joints"/> to <paramref name="store"/> in order, skipping those it already holds.</summary>
  /// <param name="store">The store to extend; every key of a joint must be a node of it.</param>
  /// <param name="joints">The joints to add.</param>
  /// <param name="fresh">Nodes that joined the store this step and have no joint yet; each is moved to a group of its own before its first joint, so a joint of any ratio can take it, and the run is folded back into group 0 when the isolation emptied it.</param>
  /// <returns>The joints the store refused as closing a jamming ring; none of them was added.</returns>
  public static List<MpShaftJoint> Apply(
    MpShaftStore store,
    IEnumerable<MpShaftJoint> joints,
    IReadOnlySet<NodeKey> fresh
  ) {
    var refused = new List<MpShaftJoint>();
    var isolated = new HashSet<NodeKey>();
    foreach (MpShaftJoint joint in joints) {
      int input = store.IndexOf(joint.Input);
      int output = store.IndexOf(joint.Output);
      if (store.EdgeIndex(input, output) >= 0)
        continue;
      foreach (NodeKey key in new[] { joint.Input, joint.Output })
        if (fresh.Contains(key) && isolated.Add(key))
          store.Isolate(store.IndexOf(key));
      if (
        !store.TryAddEdge(
          input,
          output,
          joint.Ratio,
          joint.Sign,
          joint.Stiffness
        )
      )
        refused.Add(joint);
    }
    if (
      isolated.Count > 0
      && !store.Nodes.Values.Any(i => store.GroupOf(i) == 0)
    )
      foreach (
        int group in isolated
          .Select(k => store.GroupOf(store.IndexOf(k)))
          .Distinct()
      )
        if (group != 0)
          store.Fold(group, 0);
    return refused;
  }

  private static (NodeKey, NodeKey) Order(NodeKey a, NodeKey b) =>
    (a.Pos.X, a.Pos.Y, a.Pos.Z, a.Slot).CompareTo(
      (b.Pos.X, b.Pos.Y, b.Pos.Z, b.Slot)
    ) <= 0
      ? (a, b)
      : (b, a);

  // The two cells' segments in series, or the only one that has a stiffness; ring solves read only the ratios between joints.
  private static float Stiffness(IBlockAccessor world, BlockPos a, BlockPos b) {
    float ka =
      (world.GetBlockEntity(a) as IMpEnergySegment)?.StiffnessNmPerRad ?? 0f;
    float kb =
      (world.GetBlockEntity(b) as IMpEnergySegment)?.StiffnessNmPerRad ?? 0f;
    if (ka > 0f && kb > 0f)
      return ka * kb / (ka + kb);
    return ka > 0f ? ka
      : kb > 0f ? kb
      : 1f;
  }
}
