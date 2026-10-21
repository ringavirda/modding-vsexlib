using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using Vintagestory.API.MathTools;

namespace ExpandedLib.FastForwarding;

/// <summary>One simulated-time group: the members that share a cursor.</summary>
internal sealed class FastForwardGroup {
  internal FastForwardGroup(List<FastForwardMember> members, double cursorHours) {
    Members = members;
    CursorHours = cursorHours;
  }

  internal List<FastForwardMember> Members { get; }

  /// <summary>Calendar time, in game hours, the group is simulated to.</summary>
  internal double CursorHours { get; set; }

  /// <summary>Whether a member is in an unloaded chunk; a frozen group's cursor stands still.</summary>
  internal bool Frozen => Members.Any(m => !m.Present);

  /// <summary>Game hours between <paramref name="nowHours"/> and the cursor.</summary>
  internal double Gap(double nowHours) => nowHours - CursorHours;

  /// <summary>Whether <paramref name="nowHours"/> is at least one machine second past the cursor.</summary>
  internal bool Due(double nowHours) =>
    !Frozen && Gap(nowHours) >= 1.0 / GameTime.MachineSecondsPerHour;
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
}

/// <summary>The registry of members and the groups they form with the block networks, owned by <see cref="BlockNetworkModSystem"/>. Groups are rebuilt on the first <see cref="Advance"/> after the epoch rises.</summary>
internal sealed class FastForwardDriver {
  private const float MaxStepSeconds = 2f;

  private readonly BlockNetworkModSystem _networks;
  private readonly Dictionary<BlockPos, FastForwardMember> _members = [];
  private List<FastForwardGroup> _groups = [];
  private int _builtEpoch = -1;

  internal FastForwardDriver(BlockNetworkModSystem networks) {
    _networks = networks;
  }

  /// <summary>Rises on any change that can alter which members and networks connect.</summary>
  internal int Epoch { get; private set; }

  internal IReadOnlyList<FastForwardGroup> Groups => _groups;

  internal void RaiseEpoch() => Epoch++;

  internal void Record(BlockPos pos) => FastForward.RecordLookup(this, pos);

  /// <summary>Adds the member at <paramref name="pos"/>, or replaces it. A null <paramref name="cursorHours"/> starts it at the present.</summary>
  internal FastForwardMember Register(
    BlockPos pos,
    double? cursorHours = null,
    IEnumerable<BlockPos>? couplings = null
  ) {
    var member = new FastForwardMember(pos.Copy(), cursorHours ?? NowHours());
    if (couplings != null)
      member.Couplings.UnionWith(couplings.Select(c => c.Copy()));
    _members[member.Pos] = member;
    Epoch++;
    return member;
  }

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

  /// <summary>Records that the member at <paramref name="member"/> reads through <paramref name="pos"/>; a position it had not recorded raises the epoch.</summary>
  internal void Couple(BlockPos member, BlockPos pos) {
    if (
      _members.TryGetValue(member, out FastForwardMember? record)
      && record.Couplings.Add(pos.Copy())
    )
      Epoch++;
  }

  /// <summary>Rebuilds the groups when the epoch rose, then moves each unfrozen group's cursor on by <paramref name="dt"/> machine seconds, at most two, and never past the present.</summary>
  /// <param name="dt">Seconds since the previous call.</param>
  internal void Advance(float dt) {
    if (_members.Count == 0 && _groups.Count == 0)
      return;
    if (_builtEpoch != Epoch)
      Rebuild();

    double now = NowHours();
    double share =
      Math.Min(dt, MaxStepSeconds) / GameTime.MachineSecondsPerHour;
    foreach (FastForwardGroup group in _groups) {
      if (group.Frozen)
        continue;
      group.CursorHours = Math.Min(group.CursorHours + share, now);
      foreach (FastForwardMember member in group.Members)
        member.CursorHours = group.CursorHours;
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
    _builtEpoch = Epoch;
  }

  private IEnumerable<object> LinksOf(FastForwardMember member) {
    yield return member.Pos;
    if (_networks.GetNetworkAt(member.Pos) is { } own)
      yield return own.Id;
    foreach (BlockPos pos in member.Couplings) {
      if (_members.ContainsKey(pos))
        yield return pos;
      if (_networks.GetNetworkAt(pos) is { } net)
        yield return net.Id;
    }
  }

  private double NowHours() =>
    _networks.ServerWorld is { } world ? GameTime.NowHours(world) : 0.0;
}
