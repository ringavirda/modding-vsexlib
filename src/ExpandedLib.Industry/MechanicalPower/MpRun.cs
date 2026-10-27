using ExpandedLib.Networks;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>What a machine on an "mpshaft" run reads of the run at its own node.</summary>
/// <param name="SpeedRadS">Speed of the node in rad/s, signed: positive and negative are the two senses the run can turn, 0 at rest.</param>
/// <param name="AvailableTorqueNm">Torque in N*m, zero or more, at the node's own speed that the rest of the run can still deliver to it: the run's drive less the opposing torque of every other node, the node's own load excluded.</param>
public readonly record struct MpRunReading(
  float SpeedRadS,
  float AvailableTorqueNm
) {
  /// <summary>The reading of a node on no run: speed 0 and no torque available.</summary>
  public static MpRunReading None => default;
}

/// <summary>
/// Reads an "mpshaft" run from a machine's own node. Needs no setup: a machine already holding its
/// <see cref="BlockEntityNetworkNode"/> or <see cref="BEBehaviorNetworkMember"/> reads with one call, and the
/// reading is that of the last network tick.
/// </summary>
public static class MpRun {
  /// <summary>Reads the run at the node <paramref name="key"/>.</summary>
  /// <param name="system">The network system; null reads as no run.</param>
  /// <param name="key">The machine's node on the run.</param>
  /// <returns><see cref="MpRunReading.None"/> when <paramref name="system"/> is null or the node is on no "mpshaft" run; it never throws.</returns>
  public static MpRunReading Read(BlockNetworkModSystem? system, NodeKey key) =>
    (system?.GetNetworkAt(key) as MpShaftNetwork)?.Read(key)
    ?? MpRunReading.None;

  /// <summary>Reads the run at the centre node of <paramref name="node"/>'s cell.</summary>
  /// <returns><see cref="MpRunReading.None"/> while the block entity is not yet on a run.</returns>
  public static MpRunReading ReadRun(this BlockEntityNetworkNode node) =>
    Read(node.NetworkSystem, NodeKey.Centre(node.Pos));

  /// <summary>Reads the run at the centre node of <paramref name="member"/>'s cell.</summary>
  /// <returns><see cref="MpRunReading.None"/> while the behavior is not yet on a run.</returns>
  public static MpRunReading ReadRun(this BEBehaviorNetworkMember member) =>
    Read(member.NetworkSystem, NodeKey.Centre(member.Blockentity.Pos));
}
