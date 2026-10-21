using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.FastForwarding;

/// <summary>One simulated-time group: the members that share a cursor.</summary>
internal sealed class FastForwardGroup {
  internal FastForwardGroup(List<FastForwardMember> members, double cursorHours) {
    Members = members;
    CursorHours = cursorHours;
  }

  internal List<FastForwardMember> Members { get; }

  /// <summary>Ids of the networks the group's members sit on or read through.</summary>
  internal HashSet<Guid> NetworkIds { get; } = [];

  /// <summary>Real milliseconds of a network's interval accrued by the replay steps so far.</summary>
  internal Dictionary<Guid, int> NetworkMs { get; } = [];

  /// <summary>Base steps the current replay has run; a gap closing resets it.</summary>
  internal int ReplayedSteps { get; set; }

  /// <summary>Calendar time, in game hours, the group is simulated to.</summary>
  internal double CursorHours { get; set; }

  /// <summary>Whether a member is in an unloaded chunk; a frozen group's cursor stands still.</summary>
  internal bool Frozen => Members.Any(m => !m.Present);

  /// <summary>Game hours between <paramref name="nowHours"/> and the cursor.</summary>
  internal double Gap(double nowHours) => nowHours - CursorHours;

  /// <summary>Whether <paramref name="nowHours"/> is at least one machine second past the cursor.</summary>
  internal bool Due(double nowHours) =>
    !Frozen && Gap(nowHours) >= StepHours - 1e-9;

  internal const double StepHours = 1.0 / GameTime.MachineSecondsPerHour;
}

/// <summary>A registered member: its position, the positions it reads or writes through, and its saved cursor.</summary>
internal sealed class FastForwardMember {
  internal FastForwardMember(BlockPos pos, double cursorHours) {
    Pos = pos;
    CursorHours = cursorHours;
  }

  internal BlockPos Pos { get; }
  internal bool Present { get; set; } = true;
  internal double CursorHours { get; set; }
  internal HashSet<BlockPos> Couplings { get; } = [];

  /// <summary>Positions of the members that shared this member's group when it was last saved; a peer with no member registered there holds the group frozen until its chunk loads.</summary>
  internal HashSet<BlockPos> Peers { get; } = [];

  /// <summary>Positions the member declares on each rebuild, beside the recorded <see cref="Couplings"/>.</summary>
  internal Func<IEnumerable<BlockPos>>? Declared { get; init; }

  /// <summary>Whether the entry stands for a saved peer no member has registered at yet.</summary>
  internal bool Ghost { get; init; }

  internal FastForwardGroup? Group { get; set; }

  /// <summary>The recorded and declared coupling positions.</summary>
  internal IEnumerable<BlockPos> AllCouplings =>
    Declared == null ? Couplings : Couplings.Concat(Declared()).Distinct();

  /// <summary>Runs one live tick of the given seconds; null for a member the replay does not step.</summary>
  internal Action<float>? Step { get; init; }

  /// <summary>The live tick's interval, in milliseconds.</summary>
  internal int IntervalMs { get; init; } = 1000;

  /// <summary>Whether the live tick is registered; a member whose tick is stopped is skipped by a replay.</summary>
  internal Func<bool>? Ticking { get; init; }

  /// <summary>Replay milliseconds not yet spent on a whole interval.</summary>
  internal int ReplayMs { get; set; }
}

/// <summary>The registry of members and the groups they form with the block networks, owned by <see cref="BlockNetworkModSystem"/>. Groups are rebuilt on the first <see cref="Advance"/> after the epoch rises.</summary>
internal sealed class FastForwardDriver {
  private const float MaxStepSeconds = 2f;

  private readonly BlockNetworkModSystem _networks;
  private readonly Dictionary<BlockPos, FastForwardMember> _members = [];
  private readonly Dictionary<Guid, FastForwardGroup> _networkGroups = [];
  private List<FastForwardGroup> _groups = [];
  private int _builtEpoch = -1;

  internal FastForwardDriver(BlockNetworkModSystem networks) {
    _networks = networks;
  }

  /// <summary>Rises on any change that can alter which members and networks connect.</summary>
  internal int Epoch { get; private set; }

  internal IReadOnlyList<FastForwardGroup> Groups => _groups;

  /// <summary>Real milliseconds a call may spend replaying; null reads <c>ExlibValues.FastForwardBudgetMs</c>, 0 is unlimited.</summary>
  internal int? BudgetMs { get; set; }

  /// <summary>Whether <see cref="Advance"/> replays a due group's gap; false leaves the gap standing.</summary>
  internal bool Replays { get; set; } = true;

  /// <summary>Most base steps one call runs once every due group has had its first; unlimited by default.</summary>
  internal int MaxStepsPerCall { get; set; } = int.MaxValue;

  internal void RaiseEpoch() => Epoch++;

  internal void Record(BlockPos pos) => FastForward.RecordLookup(this, pos);

  /// <summary>Adds the member at <paramref name="pos"/>, or replaces it. A null <paramref name="cursorHours"/> starts it at the present.</summary>
  internal FastForwardMember Register(
    BlockPos pos,
    double? cursorHours = null,
    IEnumerable<BlockPos>? couplings = null,
    Action<float>? step = null,
    int intervalMs = 1000,
    Func<bool>? ticking = null,
    IEnumerable<BlockPos>? peers = null,
    Func<IEnumerable<BlockPos>>? declared = null
  ) {
    var member = new FastForwardMember(pos.Copy(), cursorHours ?? NowHours()) {
      Step = step,
      IntervalMs = Math.Max(1, intervalMs),
      Ticking = ticking,
      Declared = declared,
    };
    if (couplings != null)
      member.Couplings.UnionWith(couplings.Select(c => c.Copy()));
    _members[member.Pos] = member;
    if (peers != null)
      foreach (BlockPos peer in peers) {
        member.Peers.Add(peer.Copy());
        if (!_members.ContainsKey(peer))
          _members[peer.Copy()] = new FastForwardMember(
            peer.Copy(),
            member.CursorHours
          ) {
            Present = false,
            Ghost = true,
          };
      }
    Epoch++;
    return member;
  }

  /// <summary>The positions to save for <paramref name="member"/>: the coupling positions, and the other members of its group as of the last rebuild, each in X, Y, Z order.</summary>
  internal (List<BlockPos> Ports, List<BlockPos> Peers) RecordOf(
    FastForwardMember member
  ) {
    IEnumerable<BlockPos> peers =
      member
        .Group?.Members.Where(m => !ReferenceEquals(m, member))
        .Select(m => m.Pos)
      ?? member.Peers;
    return (Ordered(member.AllCouplings), Ordered(peers));
  }

  private static List<BlockPos> Ordered(IEnumerable<BlockPos> positions) =>
    [
      .. positions
        .Select(p => p.Copy())
        .OrderBy(p => p.X)
        .ThenBy(p => p.InternalY)
        .ThenBy(p => p.Z),
    ];

  internal void SetPresent(BlockPos pos, bool present) {
    if (!_members.TryGetValue(pos, out FastForwardMember? member))
      return;
    member.Present = present;
    Epoch++;
  }

  internal void Remove(BlockPos pos) {
    if (_members.Remove(pos))
      Epoch++;
  }

  /// <summary>The cursor, in game hours, of the member at <paramref name="pos"/>, or null when none is registered.</summary>
  internal double? CursorOf(BlockPos pos) =>
    _members.TryGetValue(pos, out FastForwardMember? member)
      ? member.CursorHours
      : null;

  /// <summary>Whether the member at <paramref name="pos"/> sits in a group that is frozen or whose gap is more than the one machine second its live tick covers, so its live tick stands aside.</summary>
  internal bool IsHeld(BlockPos pos) =>
    _members.TryGetValue(pos, out FastForwardMember? member)
    && member.Group is { } group
    && Holds(group);

  /// <summary>Whether the member at <paramref name="pos"/> sits in a group with a member away.</summary>
  internal bool IsFrozen(BlockPos pos) => GroupOf(pos)?.Frozen == true;

  /// <summary>The group holding the member at <paramref name="pos"/> as of the last rebuild, or null when none is registered there or it is in none.</summary>
  internal FastForwardGroup? GroupOf(BlockPos pos) =>
    _members.TryGetValue(pos, out FastForwardMember? member)
      ? member.Group
      : null;

  /// <summary>Whether the network <paramref name="id"/> sits in a group that is held or frozen, so its live step stands aside.</summary>
  internal bool IsNetworkHeld(Guid id) =>
    _networkGroups.TryGetValue(id, out FastForwardGroup? group) && Holds(group);

  private bool Holds(FastForwardGroup group) =>
    group.Frozen || group.Gap(NowHours()) > FastForwardGroup.StepHours + 1e-9;

  /// <summary>Records that the member at <paramref name="member"/> reads through <paramref name="pos"/>; a position it had not recorded raises the epoch.</summary>
  internal void Couple(BlockPos member, BlockPos pos) {
    if (
      _members.TryGetValue(member, out FastForwardMember? record)
      && record.Couplings.Add(pos.Copy())
    )
      Epoch++;
  }

  /// <summary>Builds the groups when the epoch rose since they were last built.</summary>
  internal void EnsureBuilt() {
    if (_builtEpoch != Epoch)
      Rebuild();
  }

  /// <summary>Rebuilds the groups when the epoch rose, moves each unfrozen group's cursor on by <paramref name="dt"/> machine seconds, at most two, and never past the present, then replays what is left of each group's gap within the call's budget.</summary>
  /// <param name="accessor">The accessor the networks step through.</param>
  /// <param name="dt">Seconds since the previous call.</param>
  internal void Advance(IBlockAccessor accessor, float dt) {
    if (_members.Count == 0 && _groups.Count == 0)
      return;
    DropEmptyPeers(accessor);
    EnsureBuilt();

    double now = NowHours();
    double share =
      Math.Min(dt, MaxStepSeconds) / GameTime.MachineSecondsPerHour;
    foreach (FastForwardGroup group in _groups) {
      if (group.Frozen)
        continue;
      SetCursor(group, Math.Min(group.CursorHours + share, now));
    }

    if (Replays)
      Replay(accessor, now);
  }

  private void DropEmptyPeers(IBlockAccessor accessor) {
    List<BlockPos>? empty = null;
    foreach (FastForwardMember member in _members.Values) {
      if (!member.Ghost || accessor.GetChunkAtBlockPos(member.Pos) == null)
        continue;
      BlockEntity? entity = accessor.GetBlockEntity(member.Pos);
      if (entity == null || entity.Api != null)
        (empty ??= []).Add(member.Pos);
    }
    if (empty == null)
      return;
    foreach (BlockPos pos in empty) {
      _members.Remove(pos);
      foreach (FastForwardMember member in _members.Values)
        member.Peers.Remove(pos);
    }
    Epoch++;
  }

  private static void SetCursor(FastForwardGroup group, double hours) {
    group.CursorHours = hours;
    foreach (FastForwardMember member in group.Members)
      member.CursorHours = hours;
  }

  private void Replay(IBlockAccessor accessor, double now) {
    List<FastForwardGroup> due = _groups
      .Where(g => g.Due(now))
      .OrderBy(g => g.CursorHours)
      .ToList();
    if (due.Count == 0)
      return;

    int budgetMs = BudgetMs ?? ExlibValues.FastForwardBudgetMs;
    int exactSteps = ExlibValues.FastForwardExactSeconds;
    var clock = Stopwatch.StartNew();
    int steps = 0;
    foreach (FastForwardGroup group in due) {
      if (_builtEpoch != Epoch)
        break;
      int epoch = Epoch;
      bool first = true;
      while (group.Due(now)) {
        if (group.ReplayedSteps >= exactSteps) {
          SetCursor(group, now);
          break;
        }
        if (
          !first
          && (
            steps >= MaxStepsPerCall
            || (budgetMs > 0 && clock.ElapsedMilliseconds >= budgetMs)
          )
        )
          break;
        first = false;
        StepGroup(accessor, group);
        group.ReplayedSteps++;
        steps++;
        if (Epoch != epoch)
          break;
      }
      if (!group.Due(now))
        group.ReplayedSteps = 0;
    }
  }

  private void StepGroup(IBlockAccessor accessor, FastForwardGroup group) {
    SetCursor(group, group.CursorHours + FastForwardGroup.StepHours);
    using (FastForward.Replay(group.CursorHours)) {
      foreach (
        FastForwardMember member in group
          .Members.Where(m => m.Present && m.Step != null)
          .OrderBy(m => m.Pos.X)
          .ThenBy(m => m.Pos.Y)
          .ThenBy(m => m.Pos.Z)
      ) {
        if (member.Ticking?.Invoke() == false)
          continue;
        member.ReplayMs += 1000;
        while (member.ReplayMs >= member.IntervalMs) {
          member.ReplayMs -= member.IntervalMs;
          using (FastForward.Step(this, member.Pos))
            member.Step!(member.IntervalMs / 1000f);
        }
      }

      var stepped = new List<BlockNetwork>();
      foreach (Guid id in group.NetworkIds) {
        if (_networks.NetworkById(id) is not { } network)
          continue;
        int interval = Math.Max(1, network.StepIntervalMs);
        int accrued = group.NetworkMs.GetValueOrDefault(id) + 1000;
        while (accrued >= interval) {
          accrued -= interval;
          network.OnTick(accessor, interval / 1000f, _networks);
          if (!stepped.Contains(network))
            stepped.Add(network);
        }
        group.NetworkMs[id] = accrued;
      }
      foreach (BlockNetwork network in stepped)
        if (_networks.NetworkById(network.Id) != null)
          network.OnTickPublished(accessor, _networks);
    }
  }

  private void Rebuild() {
    var all = _members.Values.ToList();
    _groups =
    [
      .. FastForwardGroups
        .Partition(all, LinksOf)
        .Select(members => new FastForwardGroup(
          members,
          members
            .Where(m => m.Present)
            .DefaultIfEmpty(members[0])
            .Min(m => m.CursorHours)
        )),
    ];
    _networkGroups.Clear();
    foreach (FastForwardGroup group in _groups)
      foreach (FastForwardMember member in group.Members) {
        member.Group = group;
        foreach (Guid id in NetworkIdsOf(member)) {
          group.NetworkIds.Add(id);
          _networkGroups[id] = group;
        }
      }
    _builtEpoch = Epoch;
  }

  private IEnumerable<Guid> NetworkIdsOf(FastForwardMember member) {
    if (_networks.GetNetworkAt(member.Pos) is { } own)
      yield return own.Id;
    foreach (BlockPos pos in member.AllCouplings)
      if (_networks.GetNetworkAt(pos) is { } net)
        yield return net.Id;
  }

  private IEnumerable<object> LinksOf(FastForwardMember member) {
    yield return member.Pos;
    foreach (Guid id in NetworkIdsOf(member))
      yield return id;
    foreach (BlockPos pos in member.AllCouplings.Concat(member.Peers))
      if (_members.ContainsKey(pos))
        yield return pos;
  }

  private double NowHours() =>
    _networks.ServerWorld is { } world ? GameTime.NowHours(world) : 0.0;
}
