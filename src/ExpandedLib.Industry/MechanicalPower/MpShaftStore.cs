using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// Node store of the "mpshaft" type as parallel arrays grouped by rigid run, with the implicit step that turns
/// each group at one signed reference speed (rad/s); a node turns at <c>ratio * w</c>.
/// </summary>
internal sealed partial class MpShaftStore {
  private readonly Dictionary<NodeKey, int> _index = [];

  private float[] _speed = new float[4];
  private float[] _previous = new float[4];
  private bool[] _rest = new bool[4];
  private float _stepDt;
  private float[] _inertiaSum = new float[4];
  private float[] _active = new float[4];
  private float[] _activeSlope = new float[4];
  private float[] _resist = new float[4];
  private float[] _resistSlope = new float[4];
  private int _groups;

  private int[] _group = new int[16];
  private float[] _inertia = new float[16];
  private float[] _standing = new float[16];
  private float[] _ratio = new float[16];
  private float[] _drive = new float[16];
  private float[] _driveSlope = new float[16];
  private float[] _load = new float[16];
  private float[] _loadSlope = new float[16];
  private float[] _efficiency = new float[16];
  private float[] _carried = new float[16];
  private int _nodes;

  private readonly List<(
    int A,
    int B,
    float Ratio,
    int Sign,
    float Stiffness
  )> _edges = [];

  public int NodeCount => _nodes;

  public int GroupCount => _groups;

  /// <summary>Adds a group at rest or at <paramref name="speed"/> and returns its index.</summary>
  /// <param name="speed">Reference speed in rad/s, signed.</param>
  public int AddGroup(float speed = 0f) {
    if (_groups == _speed.Length) {
      int size = _groups * 2;
      Array.Resize(ref _speed, size);
      Array.Resize(ref _previous, size);
      Array.Resize(ref _rest, size);
      Array.Resize(ref _inertiaSum, size);
      Array.Resize(ref _active, size);
      Array.Resize(ref _activeSlope, size);
      Array.Resize(ref _resist, size);
      Array.Resize(ref _resistSlope, size);
    }
    _speed[_groups] = speed;
    _previous[_groups] = speed;
    return _groups++;
  }

  /// <summary>Adds a node to a group and returns its index.</summary>
  /// <param name="key">The node's identity.</param>
  /// <param name="group">Index from <see cref="AddGroup"/>.</param>
  /// <param name="inertiaKgM2">Rotational inertia at the node's own speed, kg*m^2, zero or more.</param>
  /// <param name="standingNm">Coulomb standing torque at the node's own speed, N*m, zero or more.</param>
  /// <param name="ratio">Node speed over group speed, signed; negative for a node turning against the group's reference.</param>
  /// <exception cref="ArgumentException">The store already holds <paramref name="key"/>.</exception>
  public int AddNode(
    NodeKey key,
    int group,
    float inertiaKgM2,
    float standingNm,
    float ratio = 1f
  ) {
    if (_index.ContainsKey(key))
      throw new ArgumentException(
        "The store already holds this node.",
        nameof(key)
      );
    if (_nodes == _group.Length) {
      int size = _nodes * 2;
      Array.Resize(ref _group, size);
      Array.Resize(ref _inertia, size);
      Array.Resize(ref _standing, size);
      Array.Resize(ref _ratio, size);
      Array.Resize(ref _drive, size);
      Array.Resize(ref _driveSlope, size);
      Array.Resize(ref _load, size);
      Array.Resize(ref _loadSlope, size);
      Array.Resize(ref _efficiency, size);
      Array.Resize(ref _carried, size);
    }
    int node = _nodes++;
    _topology++;
    _index[key] = node;
    _group[node] = group;
    _inertia[node] = MathF.Max(0f, inertiaKgM2);
    _standing[node] = MathF.Max(0f, standingNm);
    _ratio[node] = ratio;
    _efficiency[node] = 1f;
    return node;
  }

  public int IndexOf(NodeKey key) => _index[key];

  /// <summary>Every node by key.</summary>
  public IReadOnlyDictionary<NodeKey, int> Nodes => _index;

  /// <summary>Number of shaft joints added with <see cref="TryAddEdge"/> and kept.</summary>
  public int EdgeCount => _edges.Count;

  /// <summary>Index of the group the node belongs to.</summary>
  public int GroupOf(int node) => _group[node];

  /// <summary>Signed speed of the node over its group's reference speed.</summary>
  public float RatioOf(int node) => _ratio[node];

  /// <summary>Sets the node's rotational inertia at its own speed.</summary>
  /// <param name="inertiaKgM2">kg*m^2; negative values count as 0.</param>
  public void SetInertia(int node, float inertiaKgM2) =>
    _inertia[node] = MathF.Max(0f, inertiaKgM2);

  /// <summary>Sets the node's Coulomb standing torque at its own speed.</summary>
  /// <param name="standingNm">N*m; negative values count as 0.</param>
  public void SetStanding(int node, float standingNm) =>
    _standing[node] = MathF.Max(0f, standingNm);

  /// <summary>Inertia of the group reflected to its reference speed in kg*m^2: the sum of each node's inertia times its squared ratio.</summary>
  public float ReflectedInertia(int group) {
    float inertia = 0f;
    for (int i = 0; i < _nodes; i++)
      if (_group[i] == group)
        inertia += _inertia[i] * _ratio[i] * _ratio[i];
    return inertia;
  }

  /// <summary>
  /// Joins two nodes by a shaft or gear joint. The output node turns at <c>sign / ratio</c> times the input's
  /// ratio. Nodes of one group must already agree with the joint; nodes of two groups bring both into the first
  /// node's group, rescaling the second group and mixing the speeds by momentum.
  /// </summary>
  /// <param name="input">Index of the input node.</param>
  /// <param name="output">Index of the output node, with a ratio other than zero.</param>
  /// <param name="ratio">Input speed over output speed in magnitude, above zero.</param>
  /// <param name="sign">+1 when the output turns the same way as the input, -1 against it.</param>
  /// <param name="stiffness">Torsional stiffness G J / L of the joint in N*m per rad, above zero; only the ratios between the joints of one ring matter.</param>
  /// <returns>False, with the store unchanged, when the joint closes a ring whose ratio or sign product is not 1.</returns>
  public bool TryAddEdge(
    int input,
    int output,
    float ratio,
    int sign,
    float stiffness = 1f
  ) {
    float expected = _ratio[input] * (sign < 0 ? -1f : 1f) / ratio;
    if (_group[input] == _group[output]) {
      if (
        MathF.Abs(_ratio[output] - expected)
        > 1e-4f * MathF.Max(1f, MathF.Abs(expected))
      )
        return false;
    } else {
      if (_ratio[output] == 0f)
        return false;
      JoinGroups(_group[input], _group[output], expected / _ratio[output]);
    }
    _edges.Add((input, output, ratio, sign < 0 ? -1 : 1, stiffness));
    _topology++;
    return true;
  }

  /// <summary>
  /// Moves every node of group <paramref name="from"/> into <paramref name="into"/> with its ratio multiplied by
  /// <paramref name="factor"/>, and sets the joined group's speed so that the momentum of both is conserved.
  /// The emptied group stays and steps nothing.
  /// </summary>
  private void JoinGroups(int into, int from, float factor) {
    float momentum = 0f;
    float reflected = 0f;
    for (int i = 0; i < _nodes; i++) {
      if (_group[i] == into) {
        float n = _ratio[i];
        momentum += _inertia[i] * n * n * _speed[into];
        reflected += _inertia[i] * n * n;
      } else if (_group[i] == from) {
        float n = _ratio[i] * factor;
        momentum += _inertia[i] * n * _ratio[i] * _speed[from];
        reflected += _inertia[i] * n * n;
        _ratio[i] = n;
        _group[i] = into;
      }
    }
    if (reflected > 0f)
      _speed[into] = momentum / reflected;
  }

  /// <summary>Mixes a body of inertia <paramref name="inertiaKgM2"/> at <paramref name="speed"/> into the group by momentum: the group's speed becomes the mean of the two weighted by reflected inertia.</summary>
  /// <param name="group">Index from <see cref="AddGroup"/>.</param>
  /// <param name="inertiaKgM2">Inertia of the other body at the group's reference, kg*m^2, zero or more.</param>
  /// <param name="speed">Reference speed of the other body in rad/s, signed.</param>
  public void Mix(int group, float inertiaKgM2, float speed) {
    float own = ReflectedInertia(group);
    float other = MathF.Max(0f, inertiaKgM2);
    if (own + other > 0f)
      _speed[group] = (own * _speed[group] + other * speed) / (own + other);
  }

  /// <summary>
  /// Takes every group, node and joint of <paramref name="other"/> into this store's first group (made when the
  /// store has none) and sets that group's speed by momentum, <c>w = sum(I n w_node) / sum(I n^2)</c> over both
  /// stores' nodes, each node's speed taken before the join. The energy difference is lost.
  /// </summary>
  /// <exception cref="ArgumentException">Both stores hold the same node.</exception>
  public void Absorb(MpShaftStore other) {
    if (_groups == 0)
      AddGroup();
    var grouped = new Dictionary<int, int>();
    var map = new Dictionary<int, int>();
    foreach (var (key, i) in other._index.OrderBy(e => e.Value)) {
      int g = other._group[i];
      if (!grouped.TryGetValue(g, out int mine))
        grouped[g] = mine = AddGroup(other._speed[g]);
      map[i] = CopyNode(other, key, i, mine);
    }
    CopyEdges(other, map);
    foreach (int g in grouped.Values)
      JoinGroups(0, g, 1f);
  }

  /// <summary>
  /// Copies into this empty store the nodes of <paramref name="source"/> named in <paramref name="keys"/>, with
  /// their ratios, the joints between them and one group per source group they came from, each keeping its
  /// source's speed. Keys the source does not hold are skipped.
  /// </summary>
  public void Take(MpShaftStore source, IReadOnlySet<NodeKey> keys) {
    var grouped = new Dictionary<int, int>();
    var map = new Dictionary<int, int>();
    foreach (var (key, i) in source._index.OrderBy(e => e.Value)) {
      if (!keys.Contains(key))
        continue;
      int g = source._group[i];
      if (!grouped.TryGetValue(g, out int mine))
        grouped[g] = mine = AddGroup(source._speed[g]);
      map[i] = CopyNode(source, key, i, mine);
    }
    CopyEdges(source, map);
  }

  private int CopyNode(MpShaftStore source, NodeKey key, int i, int group) {
    int node = AddNode(
      key,
      group,
      source._inertia[i],
      source._standing[i],
      source._ratio[i]
    );
    _drive[node] = source._drive[i];
    _driveSlope[node] = source._driveSlope[i];
    _load[node] = source._load[i];
    _loadSlope[node] = source._loadSlope[i];
    _efficiency[node] = source._efficiency[i];
    _carried[node] = source._carried[i];
    return node;
  }

  private void CopyEdges(MpShaftStore source, Dictionary<int, int> map) {
    foreach (var (a, b, ratio, sign, stiffness) in source._edges)
      if (map.TryGetValue(a, out int na) && map.TryGetValue(b, out int nb))
        _edges.Add((na, nb, ratio, sign, stiffness));
    _topology++;
  }

  /// <summary>Declares the node's drive for the next step.</summary>
  /// <param name="torqueNm">Drive torque at the node's present speed, N*m; negative values count as 0.</param>
  /// <param name="slope">Change of drive torque per rad/s of node speed, zero or negative.</param>
  public void SetDrive(int node, float torqueNm, float slope = 0f) {
    _drive[node] = MathF.Max(0f, torqueNm);
    _driveSlope[node] = MathF.Min(0f, slope);
  }

  /// <summary>Declares the node's load for the next step.</summary>
  /// <param name="torqueNm">Resisting torque at the node's present speed, N*m; negative values count as 0.</param>
  /// <param name="slope">Change of load torque per rad/s of node speed, zero or positive.</param>
  public void SetLoad(int node, float torqueNm, float slope = 0f) {
    _load[node] = MathF.Max(0f, torqueNm);
    _loadSlope[node] = MathF.Max(0f, slope);
  }

  /// <summary>Declares a geared station or bevel mesh at the node.</summary>
  /// <param name="efficiency">Fraction of the carried torque delivered, 0 to 1.</param>
  /// <param name="carriedNm">Torque the mesh carried last step, N*m, signed by the flow direction; the loss is taken on its magnitude.</param>
  public void SetMesh(int node, float efficiency, float carriedNm) {
    _efficiency[node] = Math.Clamp(efficiency, 0f, 1f);
    _carried[node] = carriedNm;
  }

  /// <summary>Reference speed of the group in rad/s, signed.</summary>
  public float Speed(int group) => _speed[group];

  /// <summary>Speed of the node in rad/s, signed: its ratio times its group's speed.</summary>
  public float NodeSpeed(int node) => _ratio[node] * _speed[_group[node]];

  /// <summary>Kinetic energy of the group in J: half its reflected inertia times the squared speed.</summary>
  public float StoredEnergy(int group) {
    float inertia = 0f;
    for (int i = 0; i < _nodes; i++)
      if (_group[i] == group)
        inertia += _inertia[i] * _ratio[i] * _ratio[i];
    return 0.5f * inertia * _speed[group] * _speed[group];
  }

  /// <summary>
  /// Advances every group by <paramref name="dt"/> with the declared drives, loads and meshes. Loads, standing
  /// torque and mesh loss only oppose motion: a turning group stops at zero and a group at rest stays there while
  /// the drive does not exceed them. A group with no inertia keeps its speed.
  /// </summary>
  /// <param name="dt">Step length in seconds, above zero.</param>
  /// <param name="maxSpeed">Speed limit of every group in rad/s, above zero.</param>
  public void Step(float dt, float maxSpeed) {
    if (dt <= 0f)
      return;
    for (int g = 0; g < _groups; g++) {
      _inertiaSum[g] = 0f;
      _active[g] = 0f;
      _activeSlope[g] = 0f;
      _resist[g] = 0f;
      _resistSlope[g] = 0f;
    }
    for (int i = 0; i < _nodes; i++) {
      int g = _group[i];
      float n = _ratio[i];
      float mesh = (1f - _efficiency[i]) * MathF.Abs(_carried[i]);
      _inertiaSum[g] += _inertia[i] * n * n;
      _active[g] += n * _drive[i];
      _activeSlope[g] += n * n * _driveSlope[i];
      _resist[g] += MathF.Abs(n) * (_load[i] + _standing[i] + mesh);
      _resistSlope[g] += n * n * _loadSlope[i];
    }
    _stepDt = dt;
    for (int g = 0; g < _groups; g++) {
      _previous[g] = _speed[g];
      _rest[g] = false;
      float inertia = _inertiaSum[g];
      if (inertia <= 0f)
        continue;
      float w = _speed[g];
      float resist = _resist[g];
      float atRest = _active[g] - _activeSlope[g] * w;
      float denominator = inertia + dt * (_resistSlope[g] - _activeSlope[g]);
      float next;
      if (w == 0f) {
        next =
          MathF.Abs(atRest) <= resist
            ? 0f
            : dt * (atRest - MathF.Sign(atRest) * resist) / denominator;
      } else {
        next = w + dt * (_active[g] - MathF.Sign(w) * resist) / denominator;
        if (next * w <= 0f && MathF.Abs(atRest) <= resist)
          next = 0f;
      }
      _speed[g] = Math.Clamp(next, -maxSpeed, maxSpeed);
      _rest[g] = _speed[g] == 0f;
    }
  }
}
