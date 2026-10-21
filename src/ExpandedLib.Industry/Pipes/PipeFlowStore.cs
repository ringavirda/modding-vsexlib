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
  private const double ScaleTolerance = 1e-14;
  private const double PicardRatio = 2;
  private const double AmbientK = 293.15;
  private const double RestFlow = 1e-6;
  private const double RestPressureStepPa = 1;
  private const double ThermalTickSeconds = 1;
  private const double ThermalWakePa = 50;
  private const double KelvinOffset = 273.15;
  private const double HeatCapacityPerGasConstant = 3.5;
  private const double DefaultAmbientC = 20;
  private const double ThermalRestC = 1;
  private const double MinContentKg = 1e-12;
  private const double LiquidDensity = 1000;
  private const double LiquidStiffnessPa = 2000 * 101325;
  private const double FullFill = 0.99;
  private const double MinVoidFraction = 1 - FullFill;
  private const double BlendStartFill = 0.98;
  private const double BlendEndFill = 1;
  private const double LiquidHeat = 4186;
  private const double DrainDischarge = 0.8;
  private const double SeaLevelPa = 101325;
  private const double AirHeadPaPerM = 11.8;
  private const int PhaseIterations = 40;
  private const double PhaseLowC = -40;
  private const double PhaseHighC = 370;
  private const double HammerVelocity = 10;
  private const double HammerFill = 0.25;

  private readonly Dictionary<NodeKey, int> _index = [];
  private readonly PipeFlowSolver _solver = new();

  private double[] _volume = new double[16];
  private double[] _height = new double[16];
  private double[] _gas = new double[16];
  private double[] _temperatureC = new double[16];
  private double[] _gasConstant = new double[16];
  private double[] _liquid = new double[16];
  private double[] _air = new double[16];
  private bool[] _water = new bool[16];
  private double[] _pending = new double[16];
  private double[] _active = new double[16];
  private bool[] _frozen = new bool[16];
  private double[] _pendingTemperatureC = new double[16];
  private double[] _activeTemperatureC = new double[16];
  private double[] _wallC = new double[16];
  private double[] _wallCapacity = new double[16];
  private double[] _wallInnerArea = new double[16];
  private double[] _wallOuterUa = new double[16];
  private double[] _ambientC = new double[16];

  private int[] _from = new int[16];
  private int[] _to = new int[16];
  private double[] _bore = new double[16];
  private double[] _length = new double[16];
  private double[] _friction = new double[16];
  private double[] _share = new double[16];
  private FlowPipeClass[] _class = new FlowPipeClass[16];
  private bool[] _hammering = new bool[16];
  private bool[] _cracked = new bool[16];
  private int[] _bangs = new int[16];
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
  private bool[] _openForce = new bool[4];
  private bool[] _openLiquid = new bool[4];
  private bool[] _openLiquidNow = new bool[4];

  private double[] _pubPressure = [];
  private double[] _pubFlow = [];
  private double[] _pubOpenFlow = [];
  private double[] _pubOpenLedger = [];

  private double[] _pressure = [];
  private double[] _density = [];
  private double[] _rhs = [];
  private double[] _scale = [];
  private double[] _scaleLiquid = [];
  private double[] _outgoing = [];
  private double[] _inflow = [];
  private double[] _outgoingLiquid = [];
  private double[] _inflowLiquid = [];
  private double[] _liquidWeight = [];
  private double[] _heat = [];
  private double[] _heatStart = [];
  private double[] _heatIn = [];
  private bool[] _full = [];
  private bool[] _edgeLiquid = [];
  private double[] _conductance = [];
  private double[] _edgeDensity = [];
  private double[] _unit = [];
  private double[] _trial = [];
  private double[] _shift = [];
  private bool _conductanceMoved;
  private bool _asleep;
  private double _restSeconds;

  private enum OpeningMode {
    Linear,
    ChokedOut,
    ChokedIn,
  }

  public int NodeCount { get; private set; }

  public int EdgeCount { get; private set; }

  public int OpeningCount { get; private set; }

  /// <summary>Net mass in kg that openings have passed into the run, gas, air and liquid: negative when the run has vented. Together with <see cref="SourceLedgerKg"/> it closes the balance: the sum of every node's gas, air and liquid equals its starting total plus both.</summary>
  public double OpeningLedgerKg { get; private set; }

  /// <summary>Net mass in kg that sources and sinks have added to the run: the sum of every node's gas, air and liquid equals its starting total plus this and <see cref="OpeningLedgerKg"/>.</summary>
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
  /// <param name="liquidKg">Liquid water held, in kg. The node counts as full from 99 % of its volume, where its pressure follows the liquid branch.</param>
  /// <param name="airKg">Air held in the void beside the gas, in kg; only a water node holds any.</param>
  /// <param name="water">True for a node of a water run: air that enters through an opening or arrives with the gas phase is kept as air beside the steam, not as steam, and the node holds its steam and liquid in equilibrium. Each step condenses steam above the saturation pressure of the node's temperature and boils liquid below it, the latent heat moving the node's temperature, until the node is liquid-full.</param>
  /// <exception cref="ArgumentException"><paramref name="key"/> is already in the store, or <paramref name="airKg"/> is above zero on a node that is not a water node.</exception>
  /// <exception cref="ArgumentOutOfRangeException">The volume or gas constant is not above zero, or the gas, liquid or air is negative.</exception>
  public int AddNode(
    NodeKey key,
    double volumeM3,
    double heightM,
    double gasKg,
    double temperatureC,
    double gasConstant = 461.5,
    double liquidKg = 0,
    double airKg = 0,
    bool water = false
  ) {
    if (volumeM3 <= 0)
      throw new ArgumentOutOfRangeException(nameof(volumeM3));
    if (gasConstant <= 0)
      throw new ArgumentOutOfRangeException(nameof(gasConstant));
    if (gasKg < 0)
      throw new ArgumentOutOfRangeException(nameof(gasKg));
    if (liquidKg < 0)
      throw new ArgumentOutOfRangeException(nameof(liquidKg));
    if (airKg < 0)
      throw new ArgumentOutOfRangeException(nameof(airKg));
    if (airKg > 0 && !water)
      throw new ArgumentException(
        "Only a water node holds air.",
        nameof(airKg)
      );
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
      Array.Resize(ref _liquid, size);
      Array.Resize(ref _air, size);
      Array.Resize(ref _water, size);
      Array.Resize(ref _pending, size);
      Array.Resize(ref _active, size);
      Array.Resize(ref _frozen, size);
      Array.Resize(ref _pendingTemperatureC, size);
      Array.Resize(ref _activeTemperatureC, size);
      Array.Resize(ref _wallC, size);
      Array.Resize(ref _wallCapacity, size);
      Array.Resize(ref _wallInnerArea, size);
      Array.Resize(ref _wallOuterUa, size);
      Array.Resize(ref _ambientC, size);
    }
    _volume[i] = volumeM3;
    _height[i] = heightM;
    _gas[i] = gasKg;
    _temperatureC[i] = temperatureC;
    _gasConstant[i] = gasConstant;
    _liquid[i] = liquidKg;
    _air[i] = airKg;
    _water[i] = water;
    _pendingTemperatureC[i] = double.NaN;
    _activeTemperatureC[i] = double.NaN;
    _wallC[i] = temperatureC;
    _wallCapacity[i] = 0;
    _wallInnerArea[i] = 0;
    _wallOuterUa[i] = 0;
    _ambientC[i] = DefaultAmbientC;
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
      Array.Resize(ref _class, size);
      Array.Resize(ref _hammering, size);
      Array.Resize(ref _cracked, size);
      Array.Resize(ref _bangs, size);
      Array.Resize(ref _flow, size);
      Array.Resize(ref _prevUnit, size);
    }
    _from[e] = a;
    _to[e] = b;
    _bore[e] = boreM;
    _length[e] = lengthM;
    _friction[e] = pipeClass.Friction();
    _share[e] = share;
    _class[e] = pipeClass;
    _hammering[e] = false;
    _cracked[e] = false;
    _bangs[e] = 0;
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
  /// <param name="liquid">True for a drain: while the node holds liquid and is above <paramref name="outsidePa"/> the opening passes liquid at Cd 0.8 and sqrt(2 rho dp); once the liquid is gone it passes gas like any opening.</param>
  /// <exception cref="ArgumentOutOfRangeException">The node does not exist, or the area or outside pressure is negative.</exception>
  public int AddOpening(
    int node,
    double areaM2,
    double outsidePa,
    FlowGas gas,
    bool liquid = false
  ) {
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
      Array.Resize(ref _openForce, size);
      Array.Resize(ref _openLiquid, size);
      Array.Resize(ref _openLiquidNow, size);
    }
    _openNode[o] = node;
    _openArea[o] = areaM2;
    _openOutside[o] = outsidePa;
    _openGas[o] = gas;
    _openLiquid[o] = liquid;
    _openLiquidNow[o] = false;
    _openPrev[o] = 0;
    OpeningCount++;
    Wake();
    return o;
  }

  /// <summary>Absolute pressure in Pa of the atmosphere at <paramref name="heightM"/>: 101,325 Pa at the reference height, 11.8 Pa less for every metre above it.</summary>
  /// <param name="heightM">Height in m.</param>
  /// <param name="referenceHeightM">Height in m at which the atmosphere stands at 101,325 Pa.</param>
  public static double AmbientPressurePa(
    double heightM,
    double referenceHeightM = 0
  ) => SeaLevelPa - AirHeadPaPerM * (heightM - referenceHeightM);

  /// <summary>Opens <paramref name="node"/> to the atmosphere at the node's own height, as <see cref="AddOpening"/> does at <see cref="AmbientPressurePa"/>.</summary>
  /// <param name="node">Index of the node.</param>
  /// <param name="areaM2">Orifice area in m2, zero or more.</param>
  /// <param name="gas">Gas the node holds.</param>
  /// <param name="referenceHeightM">Height in m at which the atmosphere stands at 101,325 Pa.</param>
  /// <param name="liquid">True for a drain; see <see cref="AddOpening"/>.</param>
  /// <exception cref="ArgumentOutOfRangeException">The node does not exist or the area is negative.</exception>
  public int AddAtmosphereOpening(
    int node,
    double areaM2,
    FlowGas gas,
    double referenceHeightM = 0,
    bool liquid = false
  ) {
    CheckNode(node);
    return AddOpening(
      node,
      areaM2,
      AmbientPressurePa(_height[node], referenceHeightM),
      gas,
      liquid
    );
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
  public void Wake() {
    _asleep = false;
    _restSeconds = 0;
  }

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

  /// <summary>The wall temperature of <paramref name="node"/> in C; the content's until <see cref="SetWall"/> gives the node a wall.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The node does not exist.</exception>
  public double WallTemperatureC(int node) {
    CheckNode(node);
    return _wallC[node];
  }

  /// <summary>Gives <paramref name="node"/> a wall that exchanges heat with its content and with the ambient, replacing an earlier wall. The inner coefficient is 30 W/(m2 K) for gas, 5,000 while steam in a water node meets a colder wall and 500 for a liquid-full node. A node without a wall keeps its content temperature except where flows mix it.</summary>
  /// <param name="node">Index of the node.</param>
  /// <param name="wall">The wall's capacity and areas.</param>
  /// <param name="wallTemperatureC">The wall's temperature in C.</param>
  /// <exception cref="ArgumentOutOfRangeException">The node does not exist, or the wall's capacity is not above zero or an area is negative.</exception>
  public void SetWall(int node, PipeWall wall, double wallTemperatureC) {
    CheckNode(node);
    if (wall.CapacityJPerK <= 0)
      throw new ArgumentOutOfRangeException(nameof(wall));
    if (wall.InnerAreaM2 < 0 || wall.OuterAreaM2 < 0)
      throw new ArgumentOutOfRangeException(nameof(wall));
    _wallCapacity[node] = wall.CapacityJPerK;
    _wallInnerArea[node] = wall.InnerAreaM2;
    _wallOuterUa[node] = wall.OuterUaWPerK;
    _wallC[node] = wallTemperatureC;
    Wake();
  }

  /// <summary>Sets the ambient temperature <paramref name="node"/>'s wall loses heat to and its openings draw air in at, in C, from the next step. Nodes start at 20 C.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The node does not exist.</exception>
  public void SetAmbient(int node, double temperatureC) {
    CheckNode(node);
    _ambientC[node] = temperatureC;
    Wake();
  }

  /// <summary>Declares a source of <paramref name="kgPerSecond"/> into <paramref name="node"/> (negative draws from it), replacing its earlier declaration. It applies from the next step.</summary>
  /// <param name="node">Index of the node.</param>
  /// <param name="kgPerSecond">Mass rate in kg/s; negative draws gas at the node's own temperature.</param>
  /// <param name="temperatureC">Temperature in C of the gas a positive source delivers; NaN delivers it at the node's own temperature.</param>
  public void SetSource(
    int node,
    double kgPerSecond,
    double temperatureC = double.NaN
  ) {
    CheckNode(node);
    _pending[node] = kgPerSecond;
    _pendingTemperatureC[node] = temperatureC;
    if (kgPerSecond != 0)
      Wake();
  }

  /// <summary>The source in force at <paramref name="node"/>, in kg/s: the declaration the last step started with.</summary>
  public double ActiveSource(int node) {
    CheckNode(node);
    return _active[node];
  }

  /// <summary>Gas held by <paramref name="node"/>, in kg; the air beside it is <see cref="AirKg"/>.</summary>
  public double GasKg(int node) {
    CheckNode(node);
    return _gas[node];
  }

  /// <summary>Air held in the void of <paramref name="node"/>, in kg; zero unless it is a water node.</summary>
  public double AirKg(int node) {
    CheckNode(node);
    return _air[node];
  }

  /// <summary>Liquid held by <paramref name="node"/>, in kg.</summary>
  public double LiquidKg(int node) {
    CheckNode(node);
    return _liquid[node];
  }

  /// <summary>Whether <paramref name="node"/> is liquid-full: at least 99 % of its volume is liquid.</summary>
  public bool IsLiquidFull(int node) {
    CheckNode(node);
    return LiquidFull(node);
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

  /// <summary>Whether water hammer has cracked the cast piece on <paramref name="edge"/>. A crack stays; only a cast edge takes one.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The edge does not exist.</exception>
  public bool IsCracked(int edge) {
    if ((uint)edge >= (uint)EdgeCount)
      throw new ArgumentOutOfRangeException(nameof(edge));
    return _cracked[edge];
  }

  /// <summary>How many times water hammer has banged on <paramref name="edge"/>: gas entering at more than 10 m/s a water node over 25 % full of liquid whose wall is below the content's saturation temperature. Each onset counts once, whatever the pipe class.</summary>
  /// <exception cref="ArgumentOutOfRangeException">The edge does not exist.</exception>
  public int HammerBangs(int edge) {
    if ((uint)edge >= (uint)EdgeCount)
      throw new ArgumentOutOfRangeException(nameof(edge));
    return _bangs[edge];
  }

  /// <summary>Moves every node, edge and opening of <paramref name="other"/> into this store, with their gas, air, liquid, temperature, sources, freeze flags, shares and opening ledgers, and adds its ledger totals. <paramref name="other"/> is left as it was and must be discarded.</summary>
  /// <param name="other">The store to take from.</param>
  /// <exception cref="ArgumentException">A node of <paramref name="other"/> is already in this store.</exception>
  public void Absorb(PipeFlowStore other) {
    CopyFrom(other, other._index.Keys.ToHashSet(), true);
    OpeningLedgerKg += other.OpeningLedgerKg;
    SourceLedgerKg += other.SourceLedgerKg;
  }

  /// <summary>Copies the nodes keyed in <paramref name="keys"/> from <paramref name="source"/> into this store, with the edges whose two ends are both among them and the openings on them. Edges to other nodes are dropped, with the gas those nodes held. The ledger totals stay with <paramref name="source"/>, and the copied openings start with empty ledgers.</summary>
  /// <param name="source">The store to take from; left as it was.</param>
  /// <param name="keys">The nodes to take; keys <paramref name="source"/> does not hold are ignored.</param>
  /// <exception cref="ArgumentException">A node taken is already in this store.</exception>
  public void Take(PipeFlowStore source, IReadOnlySet<NodeKey> keys) =>
    CopyFrom(source, keys, false);

  /// <summary>Sum of the gas and air in every node, in kg.</summary>
  public double TotalGasKg() {
    double sum = 0;
    for (int i = 0; i < NodeCount; i++)
      sum += _gas[i] + _air[i];
    return sum;
  }

  /// <summary>Sum of the liquid in every node, in kg. Drains take it out of the run: together with <see cref="TotalGasKg"/> it equals the starting total plus <see cref="SourceLedgerKg"/> and <see cref="OpeningLedgerKg"/>.</summary>
  public double TotalLiquidKg() {
    double sum = 0;
    for (int i = 0; i < NodeCount; i++)
      sum += _liquid[i];
    return sum;
  }

  /// <summary>Advances the run by <paramref name="dt"/> seconds with one linearised backward-Euler solve, repeated once when a conductance moved by more than a factor 2. Stable at any step; the step only sets accuracy. A node cannot be emptied below zero: its outflows and draws are scaled to what it holds plus what flows into it in the same step. An opening that leaves its choked range during the solve is solved again as a linear nozzle. A resting run solves no flow; the step that finds the run at rest puts it to sleep. While it sleeps, a walled node more than 1 C from ambient is cooled, condensed and settled in a thermal tick once per second of stepped time, and the run wakes when the tick moves a node's pressure by more than 50 Pa or changes a node's phase.</summary>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="dt"/> is not above zero.</exception>
  public void Step(double dt) {
    if (dt <= 0)
      throw new ArgumentOutOfRangeException(nameof(dt));
    if (_asleep) {
      ThermalTick(dt);
      return;
    }
    int n = NodeCount;
    int m = EdgeCount;
    int k = OpeningCount;
    Array.Copy(_pending, _active, n);
    Array.Copy(_pendingTemperatureC, _activeTemperatureC, n);
    Array.Clear(_openForce, 0, k);
    for (int i = 0; i < n; i++)
      if (_frozen[i])
        _active[i] = 0;

    for (int i = 0; i < n; i++) {
      _pressure[i] = Pressure(i);
      _full[i] = LiquidFull(i);
      _density[i] = _full[i] ? LiquidDensity : (_gas[i] + _air[i]) / Void(i);
      _heat[i] = Heat(i);
      _heatStart[i] = HeatContent(i);
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
    if (CrossedChoke()) {
      Array.Copy(_rhs, _trial, n);
      Assemble(dt, _trial);
      _solver.Solve(_rhs);
      LastStepPasses++;
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
    Array.Fill(_scaleLiquid, 1d, 0, n);
    for (int pass = 0; pass < n && Rescale(dt); pass++) { }

    Array.Clear(_shift, 0, n);
    Array.Clear(_heatIn, 0, n);
    for (int e = 0; e < m; e++) {
      int a = _from[e];
      int b = _to[e];
      bool liquid = _edgeLiquid[e];
      var scale = liquid ? _scaleLiquid : _scale;
      double moved = _flow[e] * dt * (_flow[e] > 0 ? scale[a] : scale[b]);
      int to = moved > 0 ? b : a;
      int from = moved > 0 ? a : b;
      if (liquid) {
        _heatIn[to] += Math.Abs(moved) * LiquidHeat;
        _shift[to] +=
          Math.Abs(moved)
          * LiquidHeat
          * (_temperatureC[from] - _temperatureC[to]);
        _liquid[a] -= moved;
        _liquid[b] += moved;
      } else {
        _heatIn[to] += Math.Abs(moved) * _heat[from];
        _shift[to] +=
          Math.Abs(moved)
          * _heat[from]
          * (_temperatureC[from] - _temperatureC[to]);
        MoveGas(from, to, Math.Abs(moved));
      }
      _flow[e] = moved / dt;
    }
    for (int o = 0; o < k; o++) {
      int node = _openNode[o];
      bool liquid = _openLiquidNow[o];
      var scale = liquid ? _scaleLiquid : _scale;
      double moved = _openFlow[o] * dt * (_openFlow[o] > 0 ? scale[node] : 1);
      if (liquid)
        _liquid[node] -= moved;
      else {
        if (moved < 0) {
          _heatIn[node] -= moved * _heat[node];
          _shift[node] -=
            moved
            * _heat[node]
            * (_ambientC[node] + KelvinOffset - Kelvin(node));
        }
        DrawGas(node, moved);
      }
      _openLedger[o] -= moved;
      OpeningLedgerKg -= moved;
      _openFlow[o] = moved / dt;
    }
    for (int i = 0; i < n; i++) {
      double applied = _active[i] * dt * (_active[i] < 0 ? _scale[i] : 1);
      if (applied > 0) {
        _heatIn[i] += applied * _heat[i];
        if (!double.IsNaN(_activeTemperatureC[i]))
          _shift[i] +=
            applied * _heat[i] * (_activeTemperatureC[i] - _temperatureC[i]);
      }
      _gas[i] += applied;
      SourceLedgerKg += applied;
      if (_gas[i] < 0)
        _gas[i] = 0;
      if (_air[i] < 0)
        _air[i] = 0;
      if (_liquid[i] < 0)
        _liquid[i] = 0;
    }
    for (int i = 0; i < n; i++)
      if (
        _gas[i] + _air[i] + _liquid[i] > MinContentKg
        && _heatStart[i] + _heatIn[i] > 0
      )
        _temperatureC[i] += _shift[i] / (_heatStart[i] + _heatIn[i]);
    DetectHammer();
    ExchangeWithWalls(dt);

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

  private void DetectHammer() {
    for (int e = 0; e < EdgeCount; e++) {
      int into = _flow[e] > 0 ? _to[e] : _from[e];
      bool hammer =
        !_edgeLiquid[e]
        && _water[into]
        && _wallCapacity[into] > 0
        && _wallC[into] < _temperatureC[into]
        && Fill(into) > HammerFill
        && Math.Abs(_flow[e])
          / (_edgeDensity[e] * Math.PI * _bore[e] * _bore[e] / 4)
          > HammerVelocity;
      if (hammer && !_hammering[e]) {
        _bangs[e]++;
        if (_class[e] == FlowPipeClass.Cast)
          _cracked[e] = true;
      }
      _hammering[e] = hammer;
    }
  }

  private void ExchangeWithWalls(double dt) {
    for (int i = 0; i < NodeCount; i++) {
      if (_frozen[i])
        continue;
      if (_water[i] && !LiquidFull(i) && SettlePhases(i, dt))
        continue;
      double wallCapacity = _wallCapacity[i];
      if (wallCapacity <= 0)
        continue;
      double content = Math.Max(HeatContent(i), _heatStart[i] + _heatIn[i]);
      double inner = _wallInnerArea[i] * InnerCoefficient(i) * dt;
      double coupled =
        content > MinContentKg ? inner * content / (content + inner) : 0;
      double outer = _wallOuterUa[i] * dt;
      double wall = _wallC[i];
      double wallNew =
        (
          wallCapacity * wall
          + coupled * _temperatureC[i]
          + outer * _ambientC[i]
        ) / (wallCapacity + coupled + outer);
      if (content > MinContentKg)
        _temperatureC[i] =
          (content * _temperatureC[i] + inner * wallNew) / (content + inner);
      _wallC[i] = wallNew;
    }
  }

  private double InnerCoefficient(int i) {
    if (!_water[i])
      return PipeWall.InnerGasCoefficient;
    if (LiquidFull(i))
      return PipeWall.InnerWaterCoefficient;
    return _gas[i] > MinContentKg && _temperatureC[i] > _wallC[i]
      ? PipeWall.InnerCondensingCoefficient
      : PipeWall.InnerGasCoefficient;
  }

  private double SaturatedGas(int i, double temperatureC, double voidM3) =>
    PipeSteam.SaturationPressurePa(temperatureC)
    * voidM3
    / (_gasConstant[i] * (temperatureC + KelvinOffset));

  private bool SettlePhases(int i, double dt) {
    double gas = _gas[i];
    double liquid = _liquid[i];
    double air = _air[i];
    double total = gas + liquid;
    if (total + air <= MinContentKg)
      return false;
    double tc = _temperatureC[i];
    double voidM3 = Void(i);
    bool walled = _wallCapacity[i] > 0;
    if (!walled && liquid <= MinContentKg && gas <= SaturatedGas(i, tc, voidM3))
      return true;

    double steamHeat = HeatCapacityPerGasConstant * _gasConstant[i];
    double airHeat = air * HeatCapacityPerGasConstant * GasOrifice.AirConstant;
    double start =
      (gas * steamHeat + liquid * LiquidHeat + airHeat) * tc
      + gas * PipeSteam.LatentZeroJPerKg;
    double inner = walled ? _wallInnerArea[i] * InnerCoefficient(i) * dt : 0;
    double outer = walled ? _wallOuterUa[i] * dt : 0;
    double wallCapacity = _wallCapacity[i];
    double wall = _wallC[i];
    double ambient = _ambientC[i];
    double denominator = wallCapacity + inner + outer;

    double low = PhaseLowC;
    double high = PhaseHighC;
    for (int iteration = 0; iteration < PhaseIterations; iteration++) {
      double t = 0.5 * (low + high);
      double steam = Math.Min(SaturatedGas(i, t, voidM3), total);
      double wallNew = walled
        ? (wallCapacity * wall + inner * t + outer * ambient) / denominator
        : 0;
      double residual =
        (steam * steamHeat + (total - steam) * LiquidHeat + airHeat) * t
        + steam * PipeSteam.LatentZeroJPerKg
        - start
        + inner * (t - wallNew);
      if (residual > 0)
        high = t;
      else
        low = t;
    }
    double settled = 0.5 * (low + high);
    double steamNew = Math.Min(SaturatedGas(i, settled, voidM3), total);
    _gas[i] = steamNew;
    _liquid[i] = total - steamNew;
    _temperatureC[i] = settled;
    if (walled)
      _wallC[i] =
        (wallCapacity * wall + inner * settled + outer * ambient) / denominator;
    return true;
  }

  private bool CrossedChoke() {
    bool crossed = false;
    for (int o = 0; o < OpeningCount; o++) {
      double p = _rhs[_openNode[o]];
      double outside = _openOutside[o];
      if (
        _openMode[o] == OpeningMode.ChokedOut
          && !GasOrifice.IsChoked(p, outside)
        || _openMode[o] == OpeningMode.ChokedIn
          && !GasOrifice.IsChoked(outside, p)
      ) {
        _openForce[o] = true;
        crossed = true;
      }
    }
    return crossed;
  }

  private bool Rescale(double dt) {
    int n = NodeCount;
    var outgoing = _outgoing;
    var inflow = _inflow;
    var outgoingLiquid = _outgoingLiquid;
    var inflowLiquid = _inflowLiquid;
    Array.Clear(outgoing, 0, n);
    Array.Clear(inflow, 0, n);
    Array.Clear(outgoingLiquid, 0, n);
    Array.Clear(inflowLiquid, 0, n);
    for (int e = 0; e < EdgeCount; e++) {
      int a = _from[e];
      int b = _to[e];
      double f = _flow[e] * dt;
      var leaving = _edgeLiquid[e] ? outgoingLiquid : outgoing;
      var arriving = _edgeLiquid[e] ? inflowLiquid : inflow;
      var scale = _edgeLiquid[e] ? _scaleLiquid : _scale;
      if (f > 0) {
        leaving[a] += f;
        arriving[b] += f * scale[a];
      } else {
        leaving[b] -= f;
        arriving[a] -= f * scale[b];
      }
    }
    for (int o = 0; o < OpeningCount; o++) {
      double f = _openFlow[o] * dt;
      if (f > 0)
        (_openLiquidNow[o] ? outgoingLiquid : outgoing)[_openNode[o]] += f;
      else
        inflow[_openNode[o]] -= f;
    }
    bool changed = false;
    for (int i = 0; i < n; i++) {
      if (_active[i] < 0)
        outgoing[i] -= _active[i] * dt;
      else
        inflow[i] += _active[i] * dt;
      double held = _gas[i] + _air[i];
      double scale =
        outgoing[i] > held + inflow[i] ? (held + inflow[i]) / outgoing[i] : 1;
      if (Math.Abs(scale - _scale[i]) > ScaleTolerance)
        changed = true;
      _scale[i] = scale;
      double scaleLiquid =
        outgoingLiquid[i] > _liquid[i] + inflowLiquid[i]
          ? (_liquid[i] + inflowLiquid[i]) / outgoingLiquid[i]
          : 1;
      if (Math.Abs(scaleLiquid - _scaleLiquid[i]) > ScaleTolerance)
        changed = true;
      _scaleLiquid[i] = scaleLiquid;
    }
    return changed;
  }

  private void Assemble(double dt, double[] pg) {
    int n = NodeCount;
    int m = EdgeCount;
    _conductanceMoved = false;
    _solver.Begin();
    for (int i = 0; i < n; i++) {
      double capacity = Capacity(i, out double liquidWeight);
      _liquidWeight[i] = liquidWeight;
      _solver.AddDiagonal(i, capacity / dt);
      _rhs[i] = capacity / dt * _pressure[i] + _active[i];
    }
    for (int e = 0; e < m; e++) {
      int a = _from[e];
      int b = _to[e];
      double dp = pg[a] - pg[b];
      double lift = _density[a] * Gravity * (_height[a] - _height[b]);
      bool fromA = dp + lift >= 0;
      double rho = fromA ? _density[a] : _density[b];
      bool liquid = fromA ? _full[a] : _full[b];
      _edgeLiquid[e] = liquid;
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
      double weightA = liquid ? _liquidWeight[a] : 1;
      double weightB = liquid ? _liquidWeight[b] : 1;
      _solver.AddDiagonal(a, weightA * g);
      _solver.AddDiagonal(b, weightB * g);
      _solver.AddEdge(e, -weightA * g, -weightB * g);
      double liftRhs = g * rho * Gravity * (_height[b] - _height[a]);
      _rhs[a] += weightA * liftRhs;
      _rhs[b] -= weightB * liftRhs;
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
    double gasConstant = leaving ? GasConstantOf(node) : GasOrifice.AirConstant;
    _openLiquidNow[o] = false;

    if (_openLiquid[o] && leaving && area > 0 && _liquid[node] > MinContentKg) {
      double drop = Math.Max(p - outside, MinDropPa);
      double drain = Limit(
        DrainDischarge * area * Math.Sqrt(2 * LiquidDensity / drop),
        _openPrev[o]
      );
      _openMode[o] = OpeningMode.Linear;
      _openCoeff[o] = drain;
      _openLimited[o] = drain;
      _openLiquidNow[o] = true;
      _solver.AddDiagonal(node, _liquidWeight[node] * drain);
      _rhs[node] += _liquidWeight[node] * drain * outside;
      return;
    }

    if (area > 0 && !_openForce[o] && GasOrifice.IsChoked(upstream, downstream)) {
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
    double g = 0;
    if (area > 0)
      g = _openForce[o] ? flow / span : Limit(flow / span, _openPrev[o]);
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

  private void ThermalTick(double dt) {
    if (!HasThermalDrift())
      return;
    _restSeconds += dt;
    if (_restSeconds < ThermalTickSeconds)
      return;
    int n = NodeCount;
    var liquid = new bool[n];
    var gas = new bool[n];
    for (int i = 0; i < n; i++) {
      liquid[i] = _liquid[i] > MinContentKg;
      gas[i] = _gas[i] > MinContentKg;
      _heatStart[i] = HeatContent(i);
      _heatIn[i] = 0;
    }
    double elapsed = _restSeconds;
    _restSeconds = 0;
    ExchangeWithWalls(elapsed);
    for (int i = 0; i < n; i++)
      if (
        Math.Abs(Pressure(i) - _pressure[i]) > ThermalWakePa
        || (_liquid[i] > MinContentKg) != liquid[i]
        || (_gas[i] > MinContentKg) != gas[i]
      ) {
        Wake();
        return;
      }
  }

  private bool HasThermalDrift() {
    for (int i = 0; i < NodeCount; i++)
      if (
        _wallCapacity[i] > 0
        && !_frozen[i]
        && (
          Math.Abs(_temperatureC[i] - _ambientC[i]) > ThermalRestC
          || Math.Abs(_wallC[i] - _ambientC[i]) > ThermalRestC
        )
      )
        return true;
    return false;
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

  private void CopyFrom(
    PipeFlowStore source,
    IReadOnlySet<NodeKey> keys,
    bool carryLedgers
  ) {
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
        source._gasConstant[from],
        source._liquid[from],
        source._air[from],
        source._water[from]
      );
      _pending[to] = source._pending[from];
      _active[to] = source._active[from];
      _pendingTemperatureC[to] = source._pendingTemperatureC[from];
      _activeTemperatureC[to] = source._activeTemperatureC[from];
      _wallC[to] = source._wallC[from];
      _wallCapacity[to] = source._wallCapacity[from];
      _wallInnerArea[to] = source._wallInnerArea[from];
      _wallOuterUa[to] = source._wallOuterUa[from];
      _ambientC[to] = source._ambientC[from];
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
        source._class[e]
      );
      _friction[edge] = source._friction[e];
      _hammering[edge] = source._hammering[e];
      _cracked[edge] = source._cracked[e];
      _bangs[edge] = source._bangs[e];
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
        source._openGas[o],
        source._openLiquid[o]
      );
      _openLiquidNow[opening] = source._openLiquidNow[o];
      _openPrev[opening] = source._openPrev[o];
      _openLedger[opening] = carryLedgers ? source._openLedger[o] : 0;
      _openFlow[opening] = source._openFlow[o];
      _openMode[opening] = source._openMode[o];
      _openCoeff[opening] = source._openCoeff[o];
      _openLimited[opening] = source._openLimited[o];
    }
    Wake();
  }

  private double GasConstantOf(int i) =>
    _air[i] > 0
      ? (_gas[i] * _gasConstant[i] + _air[i] * GasOrifice.AirConstant)
        / (_gas[i] + _air[i])
      : _gasConstant[i];

  private double Heat(int i) => HeatCapacityPerGasConstant * GasConstantOf(i);

  private double HeatContent(int i) =>
    (_gas[i] + _air[i]) * Heat(i) + _liquid[i] * LiquidHeat;

  private double Kelvin(int i) => _temperatureC[i] + KelvinOffset;

  private double Fill(int i) => _liquid[i] / (LiquidDensity * _volume[i]);

  private bool LiquidFull(int i) => Fill(i) >= FullFill;

  private double Void(int i) =>
    Math.Max(
      _volume[i] - _liquid[i] / LiquidDensity,
      MinVoidFraction * _volume[i]
    );

  private double Pressure(int i) {
    double partial =
      (_gas[i] * _gasConstant[i] + _air[i] * GasOrifice.AirConstant)
      * Kelvin(i);
    double fill = Fill(i);
    if (fill < FullFill)
      return partial / (_volume[i] - _liquid[i] / LiquidDensity);
    return partial / (MinVoidFraction * _volume[i])
      + LiquidStiffnessPa * (fill - FullFill);
  }

  private double Capacity(int i, out double liquidWeight) {
    double kelvin = Kelvin(i);
    double constant = GasConstantOf(i);
    double voidM3 = Void(i);
    double blend = Math.Clamp(
      (Fill(i) - BlendStartFill) / (BlendEndFill - BlendStartFill),
      0,
      1
    );
    double flash = FlashCapacity(i);
    if (blend <= 0) {
      liquidWeight = _pressure[i] / (LiquidDensity * constant * kelvin);
      return voidM3 / (constant * kelvin) + flash;
    }
    double gasSlope =
      (1 - blend) * constant * kelvin / voidM3
      + blend * constant * kelvin / (MinVoidFraction * _volume[i]);
    double liquidSlope =
      (1 - blend) * _pressure[i] / (LiquidDensity * voidM3)
      + blend * LiquidStiffnessPa / (LiquidDensity * _volume[i]);
    liquidWeight = liquidSlope / gasSlope;
    return 1 / gasSlope + flash;
  }

  private double FlashCapacity(int i) {
    if (!_water[i] || _liquid[i] <= MinContentKg)
      return 0;
    double t = _temperatureC[i];
    double latent =
      PipeSteam.LatentZeroJPerKg
      + (HeatCapacityPerGasConstant * _gasConstant[i] - LiquidHeat) * t;
    double open = Math.Clamp(
      (FullFill - Fill(i)) / (FullFill - BlendStartFill),
      0,
      1
    );
    return open
      * _liquid[i]
      * LiquidHeat
      / (latent * PipeSteam.SaturationSlopePaPerK(t));
  }

  private void MoveGas(int from, int to, double kg) {
    double held = _gas[from] + _air[from];
    double air = _air[from] > 0 && held > 0 ? kg * _air[from] / held : 0;
    _air[from] -= air;
    _gas[from] -= kg - air;
    if (_water[to]) {
      _air[to] += air;
      _gas[to] += kg - air;
    } else
      _gas[to] += kg;
  }

  private void DrawGas(int node, double kg) {
    if (kg >= 0) {
      double held = _gas[node] + _air[node];
      double air = _air[node] > 0 && held > 0 ? kg * _air[node] / held : 0;
      _air[node] -= air;
      _gas[node] -= kg - air;
    } else if (_water[node])
      _air[node] -= kg;
    else
      _gas[node] -= kg;
  }

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
      _scaleLiquid = new double[size];
      _outgoing = new double[size];
      _inflow = new double[size];
      _outgoingLiquid = new double[size];
      _inflowLiquid = new double[size];
      _liquidWeight = new double[size];
      _heat = new double[size];
      _heatStart = new double[size];
      _heatIn = new double[size];
      _full = new bool[size];
      _trial = new double[size];
      _shift = new double[size];
    }
    if (_conductance.Length < m) {
      int size = _from.Length;
      _conductance = new double[size];
      _edgeLiquid = new bool[size];
      _edgeDensity = new double[size];
      _unit = new double[size];
    }
    _solver.SetTopology(n, _from, _to, m);
  }
}
