using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.FastForwarding;

/// <summary>What a member saves of its place in a group: the positions it couples through (<c>ff_ports</c>) and the other members of its group (<c>ff_peers</c>), each a flat list of x, y, z triples with y the dimension-aware <c>InternalY</c>.</summary>
internal sealed class FastForwardRecord {
  internal const string PortsKey = "ff_ports";
  internal const string PeersKey = "ff_peers";

  internal List<BlockPos> Ports { get; private set; } = [];
  internal List<BlockPos> Peers { get; private set; } = [];

  /// <summary>Takes the positions saved in <paramref name="tree"/>; absent keys leave the lists empty.</summary>
  internal void Read(ITreeAttribute tree) {
    Ports = ReadList(tree, PortsKey);
    Peers = ReadList(tree, PeersKey);
  }

  /// <summary>Takes the positions <paramref name="driver"/> holds for <paramref name="member"/>.</summary>
  internal void Capture(FastForwardDriver driver, FastForwardMember member) =>
    (Ports, Peers) = driver.RecordOf(member);

  internal void Write(ITreeAttribute tree) {
    tree[PortsKey] = Pack(Ports);
    tree[PeersKey] = Pack(Peers);
  }

  private static IntArrayAttribute Pack(List<BlockPos> positions) =>
    new([.. positions.SelectMany(p => new[] { p.X, p.InternalY, p.Z })]);

  private static List<BlockPos> ReadList(ITreeAttribute tree, string key) {
    int[] flat = (tree[key] as IntArrayAttribute)?.value ?? [];
    var positions = new List<BlockPos>(flat.Length / 3);
    for (int i = 0; i + 2 < flat.Length; i += 3)
      positions.Add(new BlockPos(flat[i], flat[i + 1], flat[i + 2]));
    return positions;
  }
}
