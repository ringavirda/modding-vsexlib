using System;
using System.Collections.Generic;
using ExpandedLib.Networks;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// Node store and edges of a physical gas run as parallel arrays, and the implicit backward-Euler step that
/// moves mass along them. Stores are kg; a machine's source is a kg/s declaration that takes effect at the
/// next step, so the order machines declare in does not matter.
/// </summary>
internal sealed class PipeFlowStore {
  private const double Gravity = 9.80665;
  private const double MinDropPa = 1;
  private const double KelvinOffset = 273.15;

  private readonly Dictionary<NodeKey, int> _index = [];
  private readonly PipeFlowSolver _solver = new();

  private double[] _volume = new double[16];
  private double[] _height = new double[16];
  private double[] _gas = new double[16];
  private double[] _temperatureC = new double[16];
  private double[] _gasConstant = new double[16];
  private double[] _pending = new double[16];
  private double[] _active = new double[16];

  private int[] _from = new int[16];
  private int[] _to = new int[16];
  private double[] _bore = new double[16];
  private double[] _length = new double[16];
  private double[] _friction = new double[16];
  private double[] _share = new double[16];
  private double[] _flow = new double[16];

  private double[] _pressure = [];
  private double[] _density = [];
  private double[] _rhs = [];
  private double[] _scale = [];
  private double[] _outgoing = [];
  private double[] _conductance = [];
  private double[] _edgeDensity = [];

  public int NodeCount { get; private set; }

  public int EdgeCount { get; private set; }

  /// <summary>Net mass in kg that sources and sinks have added to the run: the sum of every node's gas equals its starting total plus this.</summary>
  public double SourceLedgerKg { get; private set; }

  /// <summary>How many times the solver computed its elimination order.</summary>
  public int OrderBuilds => _solver.OrderBuilds;

  /// <summary>Adds a node and returns its index.</summary>
  /// <param name="key">The node's identity; unique in the store.</param>
  /// <param name="volumeM3">Void volume in m3, above zero.</param>
  /// <param name="heightM">Height of the node in m.</param>
  /// <param name="gasKg">Gas held, in kg.</param>
  /// <param name="temperatureC">Gas temperature in C.</param>
  /// <param name="gasConstant">Specific gas constant in J/(kg K).</param>
  /// <exception cref="ArgumentException"><paramref name="key"/> is already in the store.</exception>
  /// <exception cref="ArgumentOutOfRangeException">The volume or gas constant is not above zero, or the gas is negative.</exception>
  public int AddNode(
    NodeKey key,
    double volumeM3,
    double heightM,
    double gasKg,
    double temperatureC,
    double gasConstant = 461.5
  ) {
    if (volumeM3 <= 0)
      throw new ArgumentOutOfRangeException(nameof(volumeM3));
    if (gasConstant <= 0)
      throw new ArgumentOutOfRangeException(nameof(gasConstant));
    if (gasKg < 0)
      throw new ArgumentOutOfRangeException(nameof(gasKg));
    int i = NodeCount;
    if (!_index.TryAdd(key, i))
      throw new ArgumentException("Node already in the store.", nameof(key));
    if (i == _volume.Length) {
      int size = i * 2;
      Array.Resize(ref _volume, size);
      Array.Resize(ref _height, size);
      Array.Resize(ref _gas, size);
      Array.Resize(ref _temperatureC, size);
      Array.Resize(ref _gasConstant, size);
      Array.Resize(ref _pending, size);
      Array.Resize(ref _active, size);
    }
    _volume[i] = volumeM3;
    _height[i] = heightM;
    _gas[i] = gasKg;
    _temperatureC[i] = temperatureC;
    _gasConstant[i] = gasConstant;
    NodeCount++;
    UpdateTopology();
    return i;
  }

  /// <summary>Joins nodes <paramref name="a"/> and <paramref name="b"/> and returns the edge index; flow is positive from <paramref name="a"/> to <paramref name="b"/>.</summary>
  /// <param name="a">Index of the first node.</param>
  /// <param name="b">Index of the second node, different from <paramref name="a"/>.</param>
  /// <param name="boreM">Smaller bore of the two ends in m, above zero.</param>
  /// <param name="lengthM">Length in m, above zero.</param>
  /// <param name="pipeClass">Sets the friction factor.</param>
  /// <param name="share">Open share of the edge, 0..1.</param>
  /// <exception cref="ArgumentOutOfRangeException">A node index, the bore, the length or the share is out of range, or the nodes are the same.</exception>
  public int AddEdge(
    int a,
    int b,
    double boreM,
    double lengthM,
    FlowPipeClass pipeClass,
    double share = 1
  ) {
    CheckNode(a);
    CheckNode(b);
    if (a == b)
      throw new ArgumentOutOfRangeException(nameof(b));
    if (boreM <= 0)
      throw new ArgumentOutOfRangeException(nameof(boreM));
    if (lengthM <= 0)
      throw new ArgumentOutOfRangeException(nameof(lengthM));
    CheckShare(share);
    int e = EdgeCount;
    if (e == _from.Length) {
      int size = e * 2;
      Array.Resize(ref _from, size);
      Array.Resize(ref _to, size);
      Array.Resize(ref _bore, size);
      Array.Resize(ref _length, size);
      Array.Resize(ref _friction, size);
      Array.Resize(ref _share, size);
      Array.Resize(ref _flow, size);
    }
    _from[e] = a;
    _to[e] = b;
    _bore[e] = boreM;
    _length[e] = lengthM;
    _friction[e] = pipeClass.Friction();
    _share[e] = share;
    EdgeCount++;
    UpdateTopology();
    return e;
  }

  /// <summary>The index of the node keyed <paramref name="key"/>.</summary>
  /// <exception cref="KeyNotFoundException">No such node.</exception>
  public int IndexOf(NodeKey key) => _index[key];

  /// <summary>Sets the open share of <paramref name="edge"/> from the next step; 0 closes it.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The edge does not exist or <paramref name="share"/> is outside 0..1.</exception>
  public void SetShare(int edge, double share) {
    if ((uint)edge >= (uint)EdgeCount)
      throw new ArgumentOutOfRangeException(nameof(edge));
    CheckShare(share);
    _share[edge] = share;
  }

  /// <summary>Declares a source of <paramref name="kgPerSecond"/> into <paramref name="node"/> (negative draws from it), replacing its earlier declaration. It applies from the next step.</summary>
  public void SetSource(int node, double kgPerSecond) {
    CheckNode(node);
    _pending[node] = kgPerSecond;
  }

  /// <summary>The source in force at <paramref name="node"/>, in kg/s: the declaration the last step started with.</summary>
  public double ActiveSource(int node) {
    CheckNode(node);
    return _active[node];
  }

  /// <summary>Gas held by <paramref name="node"/>, in kg.</summary>
  public double GasKg(int node) {
    CheckNode(node);
    return _gas[node];
  }

  /// <summary>Absolute pressure of <paramref name="node"/>, in Pa.</summary>
  public double PressurePa(int node) {
    CheckNode(node);
    return Pressure(node);
  }

  /// <summary>Mean mass flow along <paramref name="edge"/> over the last step in kg/s, positive from its first node to its second.</summary>
  public double FlowKgPerSecond(int edge) {
    if ((uint)edge >= (uint)EdgeCount)
      throw new ArgumentOutOfRangeException(nameof(edge));
    return _flow[edge];
  }

  /// <summary>Sum of the gas in every node, in kg.</summary>
  public double TotalGasKg() {
    double sum = 0;
    for (int i = 0; i < NodeCount; i++)
      sum += _gas[i];
    return sum;
  }

  /// <summary>Advances the run by <paramref name="dt"/> seconds with one linearised backward-Euler solve. Stable at any step; the step only sets accuracy. A node cannot be emptied below zero: its outflows and draws are scaled for the step.</summary>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="dt"/> is not above zero.</exception>
  public void Step(double dt) {
    if (dt <= 0)
      throw new ArgumentOutOfRangeException(nameof(dt));
    int n = NodeCount;
    int m = EdgeCount;
    Array.Copy(_pending, _active, n);

    for (int i = 0; i < n; i++) {
      _pressure[i] = Pressure(i);
      _density[i] = _gas[i] / _volume[i];
    }

    _solver.Begin();
    for (int i = 0; i < n; i++) {
      double capacity = _volume[i] / (_gasConstant[i] * Kelvin(i));
      _solver.AddDiagonal(i, capacity / dt);
      _rhs[i] = capacity / dt * _pressure[i] + _active[i];
    }
    for (int e = 0; e < m; e++) {
      int a = _from[e];
      int b = _to[e];
      double dp = _pressure[a] - _pressure[b];
      double rho = dp >= 0 ? _density[a] : _density[b];
      double drop = dp + rho * Gravity * (_height[a] - _height[b]);
      double k =
        _share[e]
        * Math.PI
        * _bore[e]
        * _bore[e]
        / 4
        * Math.Sqrt(2 * rho * _bore[e] / (_friction[e] * _length[e]));
      double g = k / Math.Sqrt(Math.Max(Math.Abs(drop), MinDropPa));
      _conductance[e] = g;
      _edgeDensity[e] = rho;
      _solver.AddDiagonal(a, g);
      _solver.AddDiagonal(b, g);
      _solver.AddEdge(e, -g, -g);
      double lift = g * rho * Gravity * (_height[b] - _height[a]);
      _rhs[a] += lift;
      _rhs[b] -= lift;
    }
    _solver.Solve(_rhs);

    for (int e = 0; e < m; e++) {
      int a = _from[e];
      int b = _to[e];
      _flow[e] =
        _conductance[e]
        * (
          _rhs[a]
          - _rhs[b]
          + _edgeDensity[e] * Gravity * (_height[a] - _height[b])
        );
    }

    Array.Fill(_scale, 1d, 0, n);
    var outgoing = _outgoing;
    Array.Clear(outgoing, 0, n);
    for (int e = 0; e < m; e++) {
      if (_flow[e] > 0)
        outgoing[_from[e]] += _flow[e] * dt;
      else
        outgoing[_to[e]] -= _flow[e] * dt;
    }
    for (int i = 0; i < n; i++) {
      if (_active[i] < 0)
        outgoing[i] -= _active[i] * dt;
      if (outgoing[i] > _gas[i])
        _scale[i] = _gas[i] / outgoing[i];
    }

    for (int e = 0; e < m; e++) {
      int a = _from[e];
      int b = _to[e];
      double moved = _flow[e] * dt * (_flow[e] > 0 ? _scale[a] : _scale[b]);
      _gas[a] -= moved;
      _gas[b] += moved;
      _flow[e] = moved / dt;
    }
    for (int i = 0; i < n; i++) {
      double applied = _active[i] * dt * (_active[i] < 0 ? _scale[i] : 1);
      _gas[i] += applied;
      SourceLedgerKg += applied;
    }
  }

  private double Kelvin(int i) => _temperatureC[i] + KelvinOffset;

  private double Pressure(int i) =>
    _gas[i] * _gasConstant[i] * Kelvin(i) / _volume[i];

  private void CheckNode(int node) {
    if ((uint)node >= (uint)NodeCount)
      throw new ArgumentOutOfRangeException(nameof(node));
  }

  private static void CheckShare(double share) {
    if (share < 0 || share > 1)
      throw new ArgumentOutOfRangeException(nameof(share));
  }

  private void UpdateTopology() {
    int n = NodeCount;
    int m = EdgeCount;
    if (_pressure.Length < n) {
      int size = _volume.Length;
      _pressure = new double[size];
      _density = new double[size];
      _rhs = new double[size];
      _scale = new double[size];
      _outgoing = new double[size];
    }
    if (_conductance.Length < m) {
      int size = _from.Length;
      _conductance = new double[size];
      _edgeDensity = new double[size];
    }
    _solver.SetTopology(n, _from, _to, m);
  }
}
