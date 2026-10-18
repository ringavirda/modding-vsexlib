using System;
using System.Collections.Generic;
using System.Linq;
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
  private const double MaxConductanceStep = 4;
  private const double PicardRatio = 2;
  private const double AmbientK = 293.15;
  private const double RestFlow = 1e-6;
  private const double RestPressureStepPa = 1;
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
  private bool[] _frozen = new bool[16];

  private int[] _from = new int[16];
  private int[] _to = new int[16];
  private double[] _bore = new double[16];
  private double[] _length = new double[16];
  private double[] _friction = new double[16];
  private double[] _share = new double[16];
  private double[] _flow = new double[16];
  private double[] _prevUnit = new double[16];

  private int[] _openNode = new int[4];
  private double[] _openArea = new double[4];
  private double[] _openOutside = new double[4];
  private FlowGas[] _openGas = new FlowGas[4];
  private double[] _openPrev = new double[4];
  private double[] _openLedger = new double[4];
  private double[] _openFlow = new double[4];
  private OpeningMode[] _openMode = new OpeningMode[4];
  private double[] _openCoeff = new double[4];
  private double[] _openLimited = new double[4];

  private double[] _pubPressure = [];
  private double[] _pubFlow = [];
  private double[] _pubOpenFlow = [];
  private double[] _pubOpenLedger = [];

  private double[] _pressure = [];
  private double[] _density = [];
  private double[] _rhs = [];
  private double[] _scale = [];
  private double[] _outgoing = [];
  private double[] _conductance = [];
  private double[] _edgeDensity = [];
  private double[] _unit = [];
  private double[] _trial = [];
  private bool _conductanceMoved;
  private bool _asleep;

  private enum OpeningMode {
    Linear,
    ChokedOut,
    ChokedIn,
  }

  public int NodeCount { get; private set; }

  public int EdgeCount { get; private set; }

  public int OpeningCount { get; private set; }

  /// <summary>Net mass in kg that openings have passed into the run: negative when the run has vented. Together with <see cref="SourceLedgerKg"/> it closes the balance: the sum of every node's gas equals its starting total plus both.</summary>
  public double OpeningLedgerKg { get; private set; }

  /// <summary>Net mass in kg that sources and sinks have added to the run: the sum of every node's gas equals its starting total plus this and <see cref="OpeningLedgerKg"/>.</summary>
  public double SourceLedgerKg { get; private set; }

  /// <summary>True while the run rests: <see cref="Step"/> does nothing until something wakes it.</summary>
  public bool Asleep => _asleep;

  /// <summary>How many steps have been solved; a step skipped asleep is not counted.</summary>
  public int StepsSolved { get; private set; }

  /// <summary>Linearisations the last solved step used: 1, or 2 when a conductance moved by more than a factor 2.</summary>
  public int LastStepPasses { get; private set; }

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
      Array.Resize(ref _frozen, size);
    }
    _volume[i] = volumeM3;
    _height[i] = heightM;
    _gas[i] = gasKg;
    _temperatureC[i] = temperatureC;
    _gasConstant[i] = gasConstant;
    NodeCount++;
    UpdateTopology();
    Wake();
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
      Array.Resize(ref _prevUnit, size);
    }
    _from[e] = a;
    _to[e] = b;
    _bore[e] = boreM;
    _length[e] = lengthM;
    _friction[e] = pipeClass.Friction();
    _share[e] = share;
    _prevUnit[e] = 0;
    EdgeCount++;
    UpdateTopology();
    Wake();
    return e;
  }

  /// <summary>Opens <paramref name="node"/> to a fixed outside pressure through an orifice and returns the opening index. Gas leaves while the node is above <paramref name="outsidePa"/> and air enters while it is below.</summary>
  /// <param name="node">Index of the node.</param>
  /// <param name="areaM2">Orifice area in m2, zero or more.</param>
  /// <param name="outsidePa">Absolute outside pressure in Pa, zero or more.</param>
  /// <param name="gas">Gas the node holds; picks the choked coefficient and exponent of the outflow.</param>
  /// <exception cref="ArgumentOutOfRangeException">The node does not exist, or the area or outside pressure is negative.</exception>
  public int AddOpening(int node, double areaM2, double outsidePa, FlowGas gas) {
    CheckNode(node);
    if (areaM2 < 0)
      throw new ArgumentOutOfRangeException(nameof(areaM2));
    if (outsidePa < 0)
      throw new ArgumentOutOfRangeException(nameof(outsidePa));
    int o = OpeningCount;
    if (o == _openNode.Length) {
      int size = o * 2;
      Array.Resize(ref _openNode, size);
      Array.Resize(ref _openArea, size);
      Array.Resize(ref _openOutside, size);
      Array.Resize(ref _openGas, size);
      Array.Resize(ref _openPrev, size);
      Array.Resize(ref _openLedger, size);
      Array.Resize(ref _openFlow, size);
      Array.Resize(ref _openMode, size);
      Array.Resize(ref _openCoeff, size);
      Array.Resize(ref _openLimited, size);
    }
    _openNode[o] = node;
    _openArea[o] = areaM2;
    _openOutside[o] = outsidePa;
    _openGas[o] = gas;
    _openPrev[o] = 0;
    OpeningCount++;
    Wake();
    return o;
  }

  /// <summary>Sets the area of <paramref name="opening"/> in m2 from the next step; 0 closes it.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The opening does not exist or <paramref name="areaM2"/> is negative.</exception>
  public void SetOpeningArea(int opening, double areaM2) {
    CheckOpening(opening);
    if (areaM2 < 0)
      throw new ArgumentOutOfRangeException(nameof(areaM2));
    _openArea[opening] = areaM2;
    Wake();
  }

  /// <summary>Wakes a resting run so the next <see cref="Step"/> solves.</summary>
  public void Wake() => _asleep = false;

  /// <summary>Mean mass flow out of the run through <paramref name="opening"/> over the last step in kg/s; negative while air enters.</summary>
  public double OpeningFlowKgPerSecond(int opening) {
    CheckOpening(opening);
    return _openFlow[opening];
  }

  /// <summary>Net mass in kg that <paramref name="opening"/> has passed into the run: negative when it has vented.</summary>
  public double OpeningLedger(int opening) {
    CheckOpening(opening);
    return _openLedger[opening];
  }

  /// <summary>The last step's conductance of <paramref name="edge"/> with its share left out, in kg/(s Pa): the secant of the edge law at the drop the step started with, limited to a factor 4 of the step before.</summary>
  public double UnitConductance(int edge) {
    if ((uint)edge >= (uint)EdgeCount)
      throw new ArgumentOutOfRangeException(nameof(edge));
    return _prevUnit[edge];
  }

  /// <summary>Every node's key with its index.</summary>
  public IReadOnlyDictionary<NodeKey, int> Nodes => _index;

  /// <summary>Whether <paramref name="node"/> is frozen.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The node does not exist.</exception>
  public bool IsFrozen(int node) {
    CheckNode(node);
    return _frozen[node];
  }

  /// <summary>Freezes or thaws <paramref name="node"/>. A frozen node keeps its gas and temperature: its edges carry nothing, its openings pass nothing and its declared source is held back until it thaws.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The node does not exist.</exception>
  public void SetFrozen(int node, bool frozen) {
    CheckNode(node);
    if (_frozen[node] == frozen)
      return;
    _frozen[node] = frozen;
    Wake();
  }

  /// <summary>The temperature of <paramref name="node"/> in C.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The node does not exist.</exception>
  public double TemperatureC(int node) {
    CheckNode(node);
    return _temperatureC[node];
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
    Wake();
  }

  /// <summary>Declares a source of <paramref name="kgPerSecond"/> into <paramref name="node"/> (negative draws from it), replacing its earlier declaration. It applies from the next step.</summary>
  public void SetSource(int node, double kgPerSecond) {
    CheckNode(node);
    _pending[node] = kgPerSecond;
    if (kgPerSecond != 0)
      Wake();
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

  /// <summary>Moves every node, edge and opening of <paramref name="other"/> into this store, with their gas, temperature, sources, freeze flags, shares and opening ledgers, and adds its ledger totals. <paramref name="other"/> is left as it was and must be discarded.</summary>
  /// <param name="other">The store to take from.</param>
  /// <exception cref="ArgumentException">A node of <paramref name="other"/> is already in this store.</exception>
  public void Absorb(PipeFlowStore other) {
    CopyFrom(other, other._index.Keys.ToHashSet());
    OpeningLedgerKg += other.OpeningLedgerKg;
    SourceLedgerKg += other.SourceLedgerKg;
  }

  /// <summary>Copies the nodes keyed in <paramref name="keys"/> from <paramref name="source"/> into this store, with the edges whose two ends are both among them and the openings on them. Edges to other nodes are dropped, with the gas those nodes held. The ledger totals stay with <paramref name="source"/>.</summary>
  /// <param name="source">The store to take from; left as it was.</param>
  /// <param name="keys">The nodes to take; keys <paramref name="source"/> does not hold are ignored.</param>
  /// <exception cref="ArgumentException">A node taken is already in this store.</exception>
  public void Take(PipeFlowStore source, IReadOnlySet<NodeKey> keys) =>
    CopyFrom(source, keys);

  /// <summary>Sum of the gas in every node, in kg.</summary>
  public double TotalGasKg() {
    double sum = 0;
    for (int i = 0; i < NodeCount; i++)
      sum += _gas[i];
    return sum;
  }

  /// <summary>Advances the run by <paramref name="dt"/> seconds with one linearised backward-Euler solve, repeated once when a conductance moved by more than a factor 2. Stable at any step; the step only sets accuracy. A node cannot be emptied below zero: its outflows and draws are scaled for the step. A resting run does nothing; the step that finds the run at rest puts it to sleep.</summary>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="dt"/> is not above zero.</exception>
  public void Step(double dt) {
    if (dt <= 0)
      throw new ArgumentOutOfRangeException(nameof(dt));
    if (_asleep)
      return;
    int n = NodeCount;
    int m = EdgeCount;
    int k = OpeningCount;
    Array.Copy(_pending, _active, n);
    for (int i = 0; i < n; i++)
      if (_frozen[i])
        _active[i] = 0;

    for (int i = 0; i < n; i++) {
      _pressure[i] = Pressure(i);
      _density[i] = _gas[i] / _volume[i];
    }

    Assemble(dt, _pressure);
    _solver.Solve(_rhs);
    LastStepPasses = 1;
    if (_conductanceMoved) {
      Array.Copy(_rhs, _trial, n);
      Assemble(dt, _trial);
      _solver.Solve(_rhs);
      LastStepPasses = 2;
    }
    StepsSolved++;
    Array.Copy(_unit, _prevUnit, m);

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
    for (int o = 0; o < k; o++) {
      _openFlow[o] = _openMode[o] switch {
        OpeningMode.ChokedOut => _openCoeff[o]
          * Math.Max(_rhs[_openNode[o]], 0),
        OpeningMode.ChokedIn => -_openCoeff[o],
        _ => _openCoeff[o] * (_rhs[_openNode[o]] - _openOutside[o]),
      };
      if (_openMode[o] == OpeningMode.Linear)
        _openPrev[o] = _openLimited[o];
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
    for (int o = 0; o < k; o++)
      if (_openFlow[o] > 0)
        outgoing[_openNode[o]] += _openFlow[o] * dt;
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
    for (int o = 0; o < k; o++) {
      int node = _openNode[o];
      double moved = _openFlow[o] * dt * (_openFlow[o] > 0 ? _scale[node] : 1);
      _gas[node] -= moved;
      _openLedger[o] -= moved;
      OpeningLedgerKg -= moved;
      _openFlow[o] = moved / dt;
    }
    for (int i = 0; i < n; i++) {
      double applied = _active[i] * dt * (_active[i] < 0 ? _scale[i] : 1);
      _gas[i] += applied;
      SourceLedgerKg += applied;
    }

    _asleep = AtRest();
  }

  /// <summary>Copies the settled pressures, flows and opening ledgers of the last step to the values <see cref="PublishedPressurePa"/>, <see cref="PublishedFlowKgPerSecond"/>, <see cref="PublishedOpeningFlowKgPerSecond"/> and <see cref="PublishedOpeningLedger"/> read, so readers see one step's values together whatever order they run in.</summary>
  public void Publish() {
    int n = NodeCount;
    if (_pubPressure.Length != n)
      _pubPressure = new double[n];
    if (_pubFlow.Length != EdgeCount)
      _pubFlow = new double[EdgeCount];
    if (_pubOpenFlow.Length != OpeningCount) {
      _pubOpenFlow = new double[OpeningCount];
      _pubOpenLedger = new double[OpeningCount];
    }
    for (int i = 0; i < n; i++)
      _pubPressure[i] = Pressure(i);
    Array.Copy(_flow, _pubFlow, EdgeCount);
    Array.Copy(_openFlow, _pubOpenFlow, OpeningCount);
    Array.Copy(_openLedger, _pubOpenLedger, OpeningCount);
  }

  /// <summary>Absolute pressure of <paramref name="node"/> in Pa as of the last <see cref="Publish"/>; zero before the first.</summary>
  public double PublishedPressurePa(int node) {
    CheckNode(node);
    return node < _pubPressure.Length ? _pubPressure[node] : 0;
  }

  /// <summary>Mean flow along <paramref name="edge"/> in kg/s as of the last <see cref="Publish"/>; zero before the first.</summary>
  public double PublishedFlowKgPerSecond(int edge) {
    if ((uint)edge >= (uint)EdgeCount)
      throw new ArgumentOutOfRangeException(nameof(edge));
    return edge < _pubFlow.Length ? _pubFlow[edge] : 0;
  }

  /// <summary>Mean flow out through <paramref name="opening"/> in kg/s as of the last <see cref="Publish"/>; zero before the first.</summary>
  public double PublishedOpeningFlowKgPerSecond(int opening) {
    CheckOpening(opening);
    return opening < _pubOpenFlow.Length ? _pubOpenFlow[opening] : 0;
  }

  /// <summary>Net mass in kg that <paramref name="opening"/> had passed into the run as of the last <see cref="Publish"/>; zero before the first.</summary>
  public double PublishedOpeningLedger(int opening) {
    CheckOpening(opening);
    return opening < _pubOpenLedger.Length ? _pubOpenLedger[opening] : 0;
  }

  private void Assemble(double dt, double[] pg) {
    int n = NodeCount;
    int m = EdgeCount;
    _conductanceMoved = false;
    _solver.Begin();
    for (int i = 0; i < n; i++) {
      double capacity = _volume[i] / (_gasConstant[i] * Kelvin(i));
      _solver.AddDiagonal(i, capacity / dt);
      _rhs[i] = capacity / dt * _pressure[i] + _active[i];
    }
    for (int e = 0; e < m; e++) {
      int a = _from[e];
      int b = _to[e];
      double dp = pg[a] - pg[b];
      double lift = _density[a] * Gravity * (_height[a] - _height[b]);
      double rho = dp + lift >= 0 ? _density[a] : _density[b];
      double drop = dp + rho * Gravity * (_height[a] - _height[b]);
      double k =
        Math.PI
        * _bore[e]
        * _bore[e]
        / 4
        * Math.Sqrt(2 * rho * _bore[e] / (_friction[e] * _length[e]));
      double unit = Limit(
        k / Math.Sqrt(Math.Max(Math.Abs(drop), MinDropPa)),
        _prevUnit[e]
      );
      _unit[e] = unit;
      double g = _frozen[a] || _frozen[b] ? 0 : _share[e] * unit;
      _conductance[e] = g;
      _edgeDensity[e] = rho;
      _solver.AddDiagonal(a, g);
      _solver.AddDiagonal(b, g);
      _solver.AddEdge(e, -g, -g);
      double liftRhs = g * rho * Gravity * (_height[b] - _height[a]);
      _rhs[a] += liftRhs;
      _rhs[b] -= liftRhs;
    }
    for (int o = 0; o < OpeningCount; o++)
      AssembleOpening(o, pg[_openNode[o]]);
  }

  private void AssembleOpening(int o, double p) {
    int node = _openNode[o];
    double area = _frozen[node] ? 0 : _openArea[o];
    double outside = _openOutside[o];
    bool leaving = p >= outside;
    FlowGas gas = leaving ? _openGas[o] : FlowGas.Air;
    double upstream = leaving ? p : outside;
    double downstream = leaving ? outside : p;
    double kelvin = leaving ? Kelvin(node) : AmbientK;
    double gasConstant = leaving ? _gasConstant[node] : GasOrifice.AirConstant;

    if (area > 0 && GasOrifice.IsChoked(upstream, downstream)) {
      double choked = GasOrifice.ChokedCoefficient(gas, kelvin) * area;
      if (leaving) {
        _openMode[o] = OpeningMode.ChokedOut;
        _openCoeff[o] = choked;
        _solver.AddDiagonal(node, choked);
      } else {
        _openMode[o] = OpeningMode.ChokedIn;
        _openCoeff[o] = choked * outside;
        _rhs[node] += _openCoeff[o];
      }
      return;
    }

    double span = Math.Max(Math.Abs(p - outside), MinDropPa);
    double top = Math.Max(p, outside);
    double flow = GasOrifice.Flow(
      top,
      Math.Max(top - span, 0),
      kelvin,
      gasConstant,
      gas,
      area
    );
    double g = area > 0 ? Limit(flow / span, _openPrev[o]) : 0;
    _openMode[o] = OpeningMode.Linear;
    _openCoeff[o] = g;
    _openLimited[o] = g;
    _solver.AddDiagonal(node, g);
    _rhs[node] += g * outside;
  }

  private double Limit(double value, double previous) {
    if (previous <= 0)
      return value;
    double limited = Math.Clamp(
      value,
      previous / MaxConductanceStep,
      previous * MaxConductanceStep
    );
    if (limited > previous * PicardRatio || limited < previous / PicardRatio)
      _conductanceMoved = true;
    return limited;
  }

  private bool AtRest() {
    int n = NodeCount;
    for (int i = 0; i < n; i++)
      if (
        (_pending[i] != 0 && !_frozen[i])
        || Math.Abs(Pressure(i) - _pressure[i]) > RestPressureStepPa
      )
        return false;
    for (int e = 0; e < EdgeCount; e++)
      if (Math.Abs(_flow[e]) >= RestFlow)
        return false;
    for (int o = 0; o < OpeningCount; o++)
      if (Math.Abs(_openFlow[o]) >= RestFlow)
        return false;
    return true;
  }

  private void CopyFrom(PipeFlowStore source, IReadOnlySet<NodeKey> keys) {
    var map = new Dictionary<int, int>();
    foreach (var (key, from) in source._index) {
      if (!keys.Contains(key))
        continue;
      int to = AddNode(
        key,
        source._volume[from],
        source._height[from],
        source._gas[from],
        source._temperatureC[from],
        source._gasConstant[from]
      );
      _pending[to] = source._pending[from];
      _active[to] = source._active[from];
      _frozen[to] = source._frozen[from];
      map[from] = to;
    }
    for (int e = 0; e < source.EdgeCount; e++) {
      if (
        !map.TryGetValue(source._from[e], out int a)
        || !map.TryGetValue(source._to[e], out int b)
      )
        continue;
      int edge = AddEdge(
        a,
        b,
        source._bore[e],
        source._length[e],
        FlowPipeClass.Cast
      );
      _friction[edge] = source._friction[e];
      _share[edge] = source._share[e];
      _flow[edge] = source._flow[e];
      _prevUnit[edge] = source._prevUnit[e];
    }
    for (int o = 0; o < source.OpeningCount; o++) {
      if (!map.TryGetValue(source._openNode[o], out int node))
        continue;
      int opening = AddOpening(
        node,
        source._openArea[o],
        source._openOutside[o],
        source._openGas[o]
      );
      _openPrev[opening] = source._openPrev[o];
      _openLedger[opening] = source._openLedger[o];
      _openFlow[opening] = source._openFlow[o];
      _openMode[opening] = source._openMode[o];
      _openCoeff[opening] = source._openCoeff[o];
      _openLimited[opening] = source._openLimited[o];
    }
    Wake();
  }

  private double Kelvin(int i) => _temperatureC[i] + KelvinOffset;

  private double Pressure(int i) =>
    _gas[i] * _gasConstant[i] * Kelvin(i) / _volume[i];

  private void CheckNode(int node) {
    if ((uint)node >= (uint)NodeCount)
      throw new ArgumentOutOfRangeException(nameof(node));
  }

  private void CheckOpening(int opening) {
    if ((uint)opening >= (uint)OpeningCount)
      throw new ArgumentOutOfRangeException(nameof(opening));
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
      _trial = new double[size];
    }
    if (_conductance.Length < m) {
      int size = _from.Length;
      _conductance = new double[size];
      _edgeDensity = new double[size];
      _unit = new double[size];
    }
    _solver.SetTopology(n, _from, _to, m);
  }
}
