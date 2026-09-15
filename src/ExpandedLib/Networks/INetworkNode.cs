using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>
/// Base interface for block entities that participate in a block network (gas pipes, molten canals).
/// </summary>
public interface INetworkNode {
  /// <summary>The first letter of each direction with a network connector, e.g. "ns" for north plus south. Null while the block is loading.</summary>
  string? Orientation { get; }

  /// <summary>All orientation strings valid at this position, used for wrench cycling.</summary>
  string[] PossibleOrientations { get; }

  /// <summary>Network type identifier, e.g. "gas" or "molten".</summary>
  string NetworkType { get; }

  /// <summary>Returns <c>true</c> when this block has a connector on <paramref name="face"/>.</summary>
  bool HasConnectorAt(BlockFacing face);

  /// <summary>Whether this node currently severs the network at its position (e.g. a closed valve).</summary>
  bool IsConnectionBroken() => false;

  /// <summary>Called by the network tick with the connector faces that have no valid neighbour (open ends).</summary>
  void OnOpenConnectorsChanged(BlockFacing[] openFaces);

  /// <summary>Called for a node on the open-ended boundary of a pressurised or flooded run.</summary>
  /// <param name="isLiquid">Distinguishes water from gas.</param>
  /// <param name="intensity">Density scale from 0 upwards.</param>
  void OnLeak(BlockFacing[] leakingFaces, bool isLiquid, float intensity);

  /// <summary>Receives the latest network state, for updating the client display.</summary>
  void OnNetworkUpdate(object? state);
}
