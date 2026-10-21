using System;
using ExpandedLib.Networks;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// A machine's handle on one port node of a physical pipe run: it declares heat, a source, links and
/// couplings, which apply from the next step, and reads the port back with <see cref="Take"/>. It holds
/// only the node's key and finds the run on each call, so it stays valid as runs merge and split.
/// </summary>
public sealed class FlowPort {
  private readonly BlockNetworkModSystem _system;

  /// <summary>Creates the handle on the node <paramref name="key"/>.</summary>
  public FlowPort(BlockNetworkModSystem system, NodeKey key) {
    _system = system;
    Key = key;
  }

  /// <summary>The port's node.</summary>
  public NodeKey Key { get; }

  /// <summary>The handle on port <paramref name="index"/> of the machine <paramref name="machine"/>.</summary>
  /// <param name="machine">The machine's block entity.</param>
  /// <param name="index">Port number, 0..127.</param>
  /// <returns>The port, or <c>null</c> on a client or before the network system exists.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside 0..127.</exception>
  public static FlowPort? At(BlockEntity machine, int index) =>
    machine.Api?.ModLoader.GetModSystem<BlockNetworkModSystem>() is { } system
      ? new FlowPort(system, NodeKey.Port(machine.Pos, index))
      : null;

  /// <summary>Whether the port's node is in a physical run's store.</summary>
  public bool IsJoined => Run() is { } run && run.Store.Nodes.ContainsKey(Key);

  /// <summary>Declares heat into the port's content, replacing the earlier declaration.</summary>
  /// <param name="watts">Heat in W; negative takes it out.</param>
  /// <returns>False when the port is in no run.</returns>
  public bool SetHeatWatts(double watts) {
    if (State() is not { } state)
      return false;
    state.HeatWatts = watts;
    return true;
  }

  /// <summary>Declares a mass source into the port, replacing the earlier declaration. A positive source whose medium differs from a run that holds one passes nothing; the first positive source into an ambient run gives it its medium.</summary>
  /// <param name="kgPerSecond">Mass rate in kg/s; negative draws gas at the node's own temperature.</param>
  /// <param name="temperatureC">Temperature in C of the gas a positive source delivers; NaN delivers it at the node's own temperature.</param>
  /// <param name="medium">The medium the source delivers.</param>
  /// <param name="grade">Heating value of a fuel source relative to rich producer gas, 0..1; ignored for any other medium.</param>
  /// <returns>False when the port is in no run.</returns>
  public bool SetSource(
    double kgPerSecond,
    double temperatureC,
    FlowMedium medium,
    double grade = 0
  ) {
    if (State() is not { } state)
      return false;
    state.SourceKgPerSecond = kgPerSecond;
    state.SourceTemperatureC = temperatureC;
    state.SourceMedium = medium;
    state.SourceGrade = Math.Clamp(grade, 0, 1);
    state.SourceDirty = true;
    return true;
  }

  /// <summary>The mass and enthalpy the port moved and its mean pressure and temperature since the last call, with its stores now; the next reading starts here.</summary>
  /// <returns>The reading, or <c>null</c> when the port is in no run or no machine has declared at it.</returns>
  public PortReading? Take() {
    if (
      Run() is not { } run
      || !run.Store.Nodes.TryGetValue(Key, out int node)
      || !run.Ports.TryGetValue(Key, out var state)
    )
      return null;
    return state.Take(run.Store, node);
  }

  /// <summary>The link between this port's node and <paramref name="other"/>'s, which must be laid in the run's store from this port to the other.</summary>
  /// <returns>The link, or <c>null</c> when the two nodes are in different runs or no edge joins them.</returns>
  public Link? LinkTo(FlowPort other) {
    if (
      Run() is not { } run
      || !run.Store.Nodes.TryGetValue(Key, out int a)
      || !run.Store.Nodes.TryGetValue(other.Key, out int b)
      || run.Store.EdgeBetween(a, b) < 0
    )
      return null;
    return new Link(this, other);
  }

  /// <summary>Opens a coupling from this port's node to <paramref name="receiver"/>'s, in whatever run that is. The coupling passes nothing until <see cref="Coupling.SetArea"/> gives it an area. Calling again for the same pair returns the same coupling.</summary>
  /// <param name="receiver">The port that takes the gas.</param>
  /// <param name="gas">The gas passed.</param>
  /// <returns>The coupling, or <c>null</c> when this port is in no run.</returns>
  public Coupling? CoupleTo(FlowPort receiver, FlowGas gas) {
    if (Run() is not { } sender)
      return null;
    if (Find(sender, receiver.Key) == null) {
      var coupling = new FlowCoupling(Key, receiver.Key, gas);
      sender.Couplings.Add(coupling);
    }
    return new Coupling(this, receiver.Key);
  }

  private static FlowCoupling? Find(PipeFlowNetwork sender, NodeKey to) =>
    sender.Couplings.Find(c => c.To == to);

  internal PipeFlowNetwork? Run() =>
    _system.GetNetworkAt(Key) as PipeFlowNetwork;

  private PortState? State() {
    if (Run() is not { } run)
      return null;
    if (!run.Ports.TryGetValue(Key, out var state))
      run.Ports[Key] = state = new PortState();
    return state;
  }

  /// <summary>An edge between two port nodes of one run, directed from the port that made it to the other.</summary>
  public sealed class Link {
    private readonly FlowPort _from;
    private readonly FlowPort _to;

    internal Link(FlowPort from, FlowPort to) {
      _from = from;
      _to = to;
    }

    /// <summary>Sets the open share of the link, 0..1.</summary>
    /// <returns>False when the link no longer exists.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="share"/> is outside 0..1.</exception>
    public bool SetShare(double share) {
      if (Edge(out var store) is not { } edge)
        return false;
      store.SetShare(edge, share);
      return true;
    }

    /// <summary>Makes the link carry <paramref name="m3PerSecond"/> of its upstream content from the sending port to the other every step.</summary>
    /// <param name="m3PerSecond">Swept volume in m3/s, zero or more.</param>
    /// <returns>False when the link no longer exists or was laid from the other port.</returns>
    public bool SetSweptVolume(double m3PerSecond) {
      if (Edge(out var store) is not { } edge)
        return false;
      store.SetSweptVolume(edge, m3PerSecond);
      return true;
    }

    /// <summary>Makes the link a pump from the sending port to the other.</summary>
    /// <param name="freeM3PerSecond">Delivery at zero lift in m3/s, zero or more.</param>
    /// <param name="stallPa">Lift in Pa at which delivery stops.</param>
    /// <returns>False when the link no longer exists or was laid from the other port.</returns>
    public bool SetPumpCurve(double freeM3PerSecond, double stallPa) {
      if (Edge(out var store) is not { } edge)
        return false;
      store.SetPumpCurve(edge, freeM3PerSecond, stallPa);
      return true;
    }

    private int? Edge(out PipeFlowStore store) {
      store = null!;
      if (
        _from.Run() is not { } run
        || !run.Store.Nodes.TryGetValue(_from.Key, out int a)
        || !run.Store.Nodes.TryGetValue(_to.Key, out int b)
      )
        return null;
      store = run.Store;
      int edge = store.EdgeBetween(a, b);
      return edge >= 0 && store.EdgeFrom(edge) == a ? edge : null;
    }
  }

  /// <summary>An orifice from a port to a node of another run.</summary>
  public sealed class Coupling {
    private readonly FlowPort _from;
    private readonly NodeKey _to;

    internal Coupling(FlowPort from, NodeKey to) {
      _from = from;
      _to = to;
    }

    /// <summary>Sets the orifice area, which applies from the next step.</summary>
    /// <param name="areaM2">Area in m2, zero or more; zero closes it.</param>
    /// <returns>False when the sending port is in no run.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="areaM2"/> is negative.</exception>
    public bool SetArea(double areaM2) {
      if (areaM2 < 0)
        throw new ArgumentOutOfRangeException(nameof(areaM2));
      if (_from.Run() is not { } run || Find(run, _to) is not { } coupling)
        return false;
      coupling.AreaM2 = areaM2;
      return true;
    }
  }
}
