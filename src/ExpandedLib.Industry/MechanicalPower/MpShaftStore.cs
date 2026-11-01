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
  private double[] _turn = new double[4];
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
  private float[] _rating = new float[16];
  private float[] _ratio = new float[16];
  private float[] _drive = new float[16];
  private float[] _driveSlope = new float[16];
  private float[] _driveRest = new float[16];
  private float[] _load = new float[16];
  private float[] _loadSlope = new float[16];
  private float[] _efficiency = new float[16];
  private float[] _carried = new float[16];
  private float[] _slip = new float[16];
  private int _nodes;

  private readonly List<(
    int A,
    int B,
    float Ratio,
    int Sign,
    float Stiffness
  )> _edges = [];

  private readonly Dictionary<(int, int), int> _edgeIndex = [];

  public int NodeCount => _nodes;

  public int GroupCount => _groups;

  /// <summary>Number of times the node set, a node's group or a joint has changed; a reader that keeps what it derived from the layout compares it.</summary>
  public int Topology => _topology;

  /// <summary>Adds a group at rest or at <paramref name="speed"/> and returns its index.</summary>
  /// <param name="speed">Reference speed in rad/s, signed.</param>
  /// <param name="angle">Reference angle in rad, unwrapped; NaN leaves it unset, so the group takes the angle of the group it joins and starts at 0 when it joins none.</param>
  public int AddGroup(float speed = 0f, double angle = double.NaN) {
    if (_groups == _speed.Length) {
      int size = _groups * 2;
      Array.Resize(ref _speed, size);
      Array.Resize(ref _turn, size);
      Array.Resize(ref _previous, size);
      Array.Resize(ref _rest, size);
      Array.Resize(ref _inertiaSum, size);
      Array.Resize(ref _active, size);
      Array.Resize(ref _activeSlope, size);
      Array.Resize(ref _resist, size);
      Array.Resize(ref _resistSlope, size);
    }
    _speed[_groups] = speed;
    _turn[_groups] = angle;
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
      Array.Resize(ref _rating, size);
      Array.Resize(ref _ratio, size);
      Array.Resize(ref _drive, size);
      Array.Resize(ref _driveSlope, size);
      Array.Resize(ref _driveRest, size);
      Array.Resize(ref _load, size);
      Array.Resize(ref _loadSlope, size);
      Array.Resize(ref _efficiency, size);
      Array.Resize(ref _carried, size);
      Array.Resize(ref _slip, size);
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

  /// <summary>Index of the joint that joins the two nodes in either direction, or -1 when none does.</summary>
  public int EdgeIndex(int a, int b) =>
    _edgeIndex.TryGetValue(a < b ? (a, b) : (b, a), out int edge) ? edge : -1;

  private void AddEdge(int a, int b, float ratio, int sign, float stiffness) {
    _edgeIndex.TryAdd(a < b ? (a, b) : (b, a), _edges.Count);
    _edges.Add((a, b, ratio, sign, stiffness));
  }

  /// <summary>A copy holding the same nodes, groups with their speeds, and joints in their order, sharing nothing with this store.</summary>
  public MpShaftStore Clone() {
    var copy = new MpShaftStore();
    copy.Take(this, _index.Keys.ToHashSet());
    return copy;
  }

  /// <summary>Index of the group the node belongs to.</summary>
  public int GroupOf(int node) => _group[node];

  /// <summary>Signed speed of the node over its group's reference speed.</summary>
  public float RatioOf(int node) => _ratio[node];

  /// <summary>The node's rotational inertia at its own speed, kg*m^2.</summary>
  public float InertiaOf(int node) => _inertia[node];

  /// <summary>Sets the node's rotational inertia at its own speed.</summary>
  /// <param name="inertiaKgM2">kg*m^2; negative values count as 0.</param>
  public void SetInertia(int node, float inertiaKgM2) =>
    _inertia[node] = MathF.Max(0f, inertiaKgM2);

  /// <summary>Sets the node's working torque at its own speed.</summary>
  /// <param name="ratingNm">N*m; zero or less marks the node unrated, so it never counts as loaded.</param>
  public void SetRating(int node, float ratingNm) =>
    _rating[node] = MathF.Max(0f, ratingNm);

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
    AddEdge(input, output, ratio, sign < 0 ? -1 : 1, stiffness);
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
    if (double.IsNaN(_turn[into]))
      _turn[into] = _turn[from];
  }

  /// <summary>Moves every node of group <paramref name="from"/> into group <paramref name="into"/> at the ratios it had, mixing the two speeds by momentum.</summary>
  /// <param name="from">Index of the group to empty.</param>
  /// <param name="into">Index of the group that takes the nodes.</param>
  public void Fold(int from, int into) {
    JoinGroups(into, from, 1f);
    _topology++;
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
    float reflected = ReflectedInertia(0);
    float momentum = reflected * _speed[0];
    var map = new Dictionary<int, int>();
    foreach (var (key, i) in other._index.OrderBy(e => e.Value)) {
      map[i] = CopyNode(other, key, i, 0);
      if (double.IsNaN(_turn[0]))
        _turn[0] = other._turn[other._group[i]];
      float n = other._ratio[i];
      float share = other._inertia[i] * n * n;
      reflected += share;
      momentum += share * other._speed[other._group[i]];
    }
    CopyEdges(other, map);
    if (reflected > 0f)
      _speed[0] = momentum / reflected;
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
        grouped[g] = mine = AddGroup(source._speed[g], source._turn[g]);
      map[i] = CopyNode(source, key, i, mine);
    }
    CopyEdges(source, map);
  }

  /// <summary>
  /// Copies into this empty store the nodes of <paramref name="source"/> named in <paramref name="keys"/>, each in
  /// a group of its own at ratio 1 turning at the speed it had, and no joints, so joints of any ratio can join
  /// them again. Keys the source does not hold are skipped.
  /// </summary>
  public void TakeLoose(MpShaftStore source, IReadOnlySet<NodeKey> keys) {
    foreach (var (key, i) in source._index.OrderBy(e => e.Value)) {
      if (!keys.Contains(key))
        continue;
      int node = CopyNode(
        source,
        key,
        i,
        AddGroup(
          source.NodeSpeed(i),
          source._ratio[i] * source._turn[source._group[i]]
        )
      );
      _ratio[node] = 1f;
    }
    _topology++;
  }

  /// <summary>
  /// Folds every node that is alone in its group into group 0 by momentum, then renumbers the groups in the order
  /// of their first node, dropping empty ones, so group 0 is the group of the first node.
  /// </summary>
  /// <param name="apart">Nodes that keep a group of their own even when alone, the ends of links that may slip or be open.</param>
  public void Settle(IReadOnlySet<NodeKey>? apart = null) {
    Compact();
    var size = new int[_groups];
    for (int i = 0; i < _nodes; i++)
      size[_group[i]]++;
    var kept = new HashSet<int>();
    foreach (var (key, i) in _index)
      if (apart?.Contains(key) == true)
        kept.Add(i);
    for (int i = 0; i < _nodes; i++)
      if (_group[i] != 0 && size[_group[i]] == 1 && !kept.Contains(i))
        Fold(_group[i], 0);
    Compact();
  }

  private void Compact() {
    var map = new Dictionary<int, int>();
    for (int i = 0; i < _nodes; i++)
      map.TryAdd(_group[i], map.Count);
    var speed = new float[_speed.Length];
    var angle = new double[_speed.Length];
    var previous = new float[_speed.Length];
    var rest = new bool[_speed.Length];
    foreach (var (old, renumbered) in map) {
      speed[renumbered] = _speed[old];
      angle[renumbered] = _turn[old];
      previous[renumbered] = _previous[old];
      rest[renumbered] = _rest[old];
    }
    _speed = speed;
    _turn = angle;
    _previous = previous;
    _rest = rest;
    _groups = map.Count;
    for (int i = 0; i < _nodes; i++)
      _group[i] = map[_group[i]];
    _topology++;
  }

  private int CopyNode(MpShaftStore source, NodeKey key, int i, int group) {
    int node = AddNode(
      key,
      group,
      source._inertia[i],
      source._standing[i],
      source._ratio[i]
    );
    _rating[node] = source._rating[i];
    _drive[node] = source._drive[i];
    _driveSlope[node] = source._driveSlope[i];
    _driveRest[node] = source._driveRest[i];
    _load[node] = source._load[i];
    _loadSlope[node] = source._loadSlope[i];
    _efficiency[node] = source._efficiency[i];
    _carried[node] = source._carried[i];
    _slip[node] = source._slip[i];
    return node;
  }

  private void CopyEdges(MpShaftStore source, Dictionary<int, int> map) {
    foreach (var (a, b, ratio, sign, stiffness) in source._edges)
      if (map.TryGetValue(a, out int na) && map.TryGetValue(b, out int nb))
        AddEdge(na, nb, ratio, sign, stiffness);
    _topology++;
  }

  /// <summary>Declares the node's drive for the next step.</summary>
  /// <param name="torqueNm">Drive torque at the node's present speed, N*m; negative values count as 0.</param>
  /// <param name="slope">Change of drive torque per rad/s of node speed, zero or negative.</param>
  public void SetDrive(int node, float torqueNm, float slope = 0f) {
    _drive[node] = MathF.Max(0f, torqueNm);
    _driveSlope[node] = MathF.Min(0f, slope);
    _driveRest[node] = _drive[node] - _driveSlope[node] * NodeSpeed(node);
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

  /// <summary>Sets the torque a slipping link puts on the node for the next step.</summary>
  /// <param name="torqueNm">N*m at the node's own speed, signed along the node's own sense of turning.</param>
  public void SetSlip(int node, float torqueNm) => _slip[node] = torqueNm;

  /// <summary>Adds to the torque a slipping link puts on the node for the next step.</summary>
  /// <param name="torqueNm">N*m at the node's own speed, signed along the node's own sense of turning.</param>
  public void AddSlip(int node, float torqueNm) => _slip[node] += torqueNm;

  /// <summary>Reference speed of the group in rad/s, signed.</summary>
  public float Speed(int group) => _speed[group];

  /// <summary>Speed of the node in rad/s, signed: its ratio times its group's speed.</summary>
  public float NodeSpeed(int node) => _ratio[node] * _speed[_group[node]];

  /// <summary>Reference angle of the group in rad, signed and unwrapped, 0 when none has been set.</summary>
  public double Angle(int group) =>
    double.IsNaN(_turn[group]) ? 0d : _turn[group];

  /// <summary>Angle of the node in rad, signed and unwrapped: its ratio times its group's angle.</summary>
  public double NodeAngle(int node) => _ratio[node] * Angle(_group[node]);

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
  /// the drive does not exceed them. A group with no inertia keeps its speed; every group turns by its mean speed.
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
      _active[g] += n * (_drive[i] + _slip[i]);
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
    for (int g = 0; g < _groups; g++) {
      if (double.IsNaN(_turn[g]))
        _turn[g] = 0d;
      _turn[g] += 0.5 * (_previous[g] + _speed[g]) * dt;
    }
  }
}
