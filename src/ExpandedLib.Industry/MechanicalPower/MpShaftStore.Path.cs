using System;
using ExpandedLib.Industry.Pipes;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// The torque path of the "mpshaft" store: after a step, the net external torque of every node and the torque each
/// joint carries, by subtree sums over a rooted walk for a tree and by stiffness for a ring.
/// </summary>
internal sealed partial class MpShaftStore {
  private readonly PipeFlowSolver _solver = new();

  private int _topology;
  private int _pathTopology = -1;
  private int[] _component = [];
  private int[] _componentNodes = [];
  private int[] _componentEdges = [];
  private int[] _adjacencyStart = [];
  private int[] _adjacencyEdge = [];
  private int[] _loopEdge = [];
  private int[] _loopFrom = [];
  private int[] _loopTo = [];
  private int[] _loopGround = [];
  private int[] _roots = [];
  private int[] _order = [];
  private int[] _parentEdge = [];
  private int[] _parentNode = [];
  private int _orderLength;
  private double[] _injection = [];
  private double[] _angle = [];
  private float[] _net = new float[16];
  private float[] _netActive = new float[4];
  private float[] _netResist = new float[4];
  private float[] _rootWeight = [];
  private int[] _rootBest = [];
  private float[] _edgeTorque = [];
  private float[] _nodeTorque = new float[16];
  private float[] _nodeLoad = new float[16];

  /// <summary>How many times the ring solver's elimination order has been computed; it changes only with the joints.</summary>
  public int PathOrderBuilds => _solver.OrderBuilds;

  /// <summary>Net external torque at the node's own speed after the last <see cref="SolveTorquePath"/>, N*m: drive, load, standing, mesh loss and inertial reaction.</summary>
  public float NetTorque(int node) => _net[node];

  /// <summary>Torque joint <paramref name="edge"/> carried from its input node to its output node, in N*m at the group's reference speed; negative when it flows the other way. Joints count in the order <see cref="TryAddEdge"/> kept them.</summary>
  public float EdgeTorque(int edge) => _edgeTorque[edge];

  /// <summary>The largest torque any joint of the node carried, N*m at the node's own speed.</summary>
  public float NodeTorque(int node) => _nodeTorque[node];

  /// <summary>How far the node's joints load it after the last <see cref="SolveTorquePath"/>: the highest torque over rating among them, each joint's torque taken at the node's own speed. Zero for an unrated node.</summary>
  public float NodeLoad(int node) => _nodeLoad[node];

  /// <summary>How far joint <paramref name="edge"/> loads its weakest end after the last <see cref="SolveTorquePath"/>: the higher of its two ends' torque over rating, each end's torque taken at that end's own speed, so a geared end overloads at its own speed. Unrated ends do not count; above 1 the joint is past its rating.</summary>
  public float EdgeLoad(int edge) {
    var (a, b, _, _, _) = _edges[edge];
    return MathF.Max(EndLoad(edge, a), EndLoad(edge, b));
  }

  private float EndLoad(int edge, int node) =>
    _rating[node] <= 0f || _ratio[node] == 0f
      ? 0f
      : MathF.Abs(_edgeTorque[edge] / _ratio[node]) / _rating[node];

  /// <summary>Works out where the torque of the last <see cref="Step"/> flowed: subtree sums for a run with no ring, <c>K phi = e</c> by stiffness for a ring, both at the group's reference speed.</summary>
  public void SolveTorquePath() {
    if (_pathTopology != _topology)
      BuildPath();
    if (_net.Length < _nodes) {
      Array.Resize(ref _net, _nodes);
      Array.Resize(ref _nodeTorque, _nodes);
      Array.Resize(ref _nodeLoad, _nodes);
    }
    for (int i = 0; i < _nodes; i++)
      _injection[i] = 0d;
    NetTorques();
    Array.Clear(_nodeTorque, 0, _nodes);
    SolveTrees();
    SolveRings();
    for (int e = 0; e < _edges.Count; e++) {
      float t = MathF.Abs(_edgeTorque[e]);
      var (a, b, _, _, _) = _edges[e];
      _nodeTorque[a] = MathF.Max(_nodeTorque[a], t);
      _nodeTorque[b] = MathF.Max(_nodeTorque[b], t);
    }
    Array.Clear(_nodeLoad, 0, _nodes);
    for (int e = 0; e < _edges.Count; e++) {
      var (a, b, _, _, _) = _edges[e];
      _nodeLoad[a] = MathF.Max(_nodeLoad[a], EndLoad(e, a));
      _nodeLoad[b] = MathF.Max(_nodeLoad[b], EndLoad(e, b));
    }
    for (int i = 0; i < _nodes; i++)
      _nodeTorque[i] =
        _ratio[i] == 0f ? 0f : _nodeTorque[i] / MathF.Abs(_ratio[i]);
  }

  private void NetTorques() {
    if (_stepDt <= 0f) {
      Array.Clear(_net, 0, _nodes);
      return;
    }
    if (_netActive.Length < _groups) {
      _netActive = new float[_groups];
      _netResist = new float[_groups];
    }
    Array.Clear(_netActive, 0, _groups);
    Array.Clear(_netResist, 0, _groups);
    for (int i = 0; i < _nodes; i++) {
      int g = _group[i];
      float n = _ratio[i];
      _netActive[g] += n * Effective(i, g);
      _netResist[g] += MathF.Abs(n) * Opposing(i);
    }
    for (int i = 0; i < _nodes; i++) {
      int g = _group[i];
      float n = _ratio[i];
      float active = _netActive[g];
      float resist = _netResist[g];
      float share =
        resist > 0f ? MathF.Min(1f, MathF.Abs(active) / resist) : 0f;
      float heading = _previous[g] != 0f ? _previous[g] : _speed[g];
      float sign = _rest[g]
        ? MathF.Sign(n) * MathF.Sign(active) * share
        : MathF.Sign(n * heading);
      _net[i] = Effective(i, g) - sign * Opposing(i);
      _injection[i] = n * _net[i];
    }
  }

  private float Effective(int node, int group) {
    float n = _ratio[node];
    float change = n * (_speed[group] - _previous[group]);
    return _drive[node]
      + (_driveSlope[node] - _loadSlope[node]) * change
      - _inertia[node] * change / _stepDt;
  }

  private float Opposing(int node) =>
    _load[node]
    + _standing[node]
    + (1f - _efficiency[node]) * MathF.Abs(_carried[node]);

  private void SolveTrees() {
    if (_edgeTorque.Length < _edges.Count)
      _edgeTorque = new float[_edges.Count];
    int comps = _component.Length;
    bool changed = false;
    for (int c = 0; c < comps; c++) {
      _rootWeight[c] = -1f;
      _rootBest[c] = c;
    }
    for (int i = 0; i < _nodes; i++) {
      int c = _component[i];
      if (_componentEdges[c] >= _componentNodes[c])
        continue;
      float w = MathF.Abs(_ratio[i] * _drive[i]);
      if (w > _rootWeight[c]) {
        _rootWeight[c] = w;
        _rootBest[c] = i;
      }
    }
    for (int c = 0; c < comps; c++) {
      if (_component[c] != c || _componentEdges[c] >= _componentNodes[c])
        continue;
      if (_roots[c] != _rootBest[c]) {
        _roots[c] = _rootBest[c];
        changed = true;
      }
    }
    if (changed || _orderLength < 0)
      BuildOrder();
    for (int k = _orderLength - 1; k >= 0; k--) {
      int v = _order[k];
      int e = _parentEdge[v];
      if (e < 0)
        continue;
      double sub = _injection[v];
      _injection[_parentNode[v]] += sub;
      _edgeTorque[e] = (float)(_edges[e].B == v ? -sub : sub);
    }
  }

  private void BuildOrder() {
    _orderLength = 0;
    var seen = new bool[_nodes];
    var queue = new int[_nodes];
    for (int c = 0; c < _component.Length; c++) {
      if (_component[c] != c || _componentEdges[c] >= _componentNodes[c])
        continue;
      int head = _orderLength;
      queue[_orderLength++] = _roots[c];
      seen[_roots[c]] = true;
      _parentEdge[_roots[c]] = -1;
      _parentNode[_roots[c]] = -1;
      for (; head < _orderLength; head++) {
        int v = queue[head];
        for (int s = _adjacencyStart[v]; s < _adjacencyStart[v + 1]; s++) {
          int e = _adjacencyEdge[s];
          int u = _edges[e].A == v ? _edges[e].B : _edges[e].A;
          if (seen[u])
            continue;
          seen[u] = true;
          _parentEdge[u] = e;
          _parentNode[u] = v;
          queue[_orderLength++] = u;
        }
      }
    }
    Array.Copy(queue, _order, _orderLength);
  }

  private void SolveRings() {
    if (_loopEdge.Length == 0)
      return;
    var sums = new double[_nodes];
    var counts = new int[_nodes];
    for (int i = 0; i < _nodes; i++) {
      sums[_component[i]] += _injection[i];
      counts[_component[i]]++;
    }
    _solver.Begin();
    var diagonal = new double[_nodes];
    for (int e = 0; e < _loopEdge.Length; e++) {
      var (a, b, _, _, stiffness) = _edges[_loopEdge[e]];
      double k = stiffness * MathF.Abs(_ratio[a] * _ratio[b]);
      diagonal[a] += k;
      diagonal[b] += k;
      _solver.AddEdge(e, -k, -k);
    }
    for (int i = 0; i < _nodes; i++) {
      bool ring =
        _componentEdges[_component[i]] >= _componentNodes[_component[i]];
      if (!ring) {
        _solver.AddDiagonal(i, 1d);
        _angle[i] = 0d;
        continue;
      }
      _solver.AddDiagonal(i, diagonal[i]);
      _angle[i] = _injection[i] - sums[_component[i]] / counts[_component[i]];
    }
    foreach (int g in _loopGround)
      _solver.AddDiagonal(g, 1e-9d * diagonal[g]);
    _solver.Solve(_angle);
    for (int e = 0; e < _loopEdge.Length; e++) {
      var (a, b, _, _, stiffness) = _edges[_loopEdge[e]];
      double k = stiffness * MathF.Abs(_ratio[a] * _ratio[b]);
      _edgeTorque[_loopEdge[e]] = (float)(k * (_angle[a] - _angle[b]));
    }
  }

  private void BuildPath() {
    _pathTopology = _topology;
    int n = _nodes;
    int m = _edges.Count;
    _component = new int[n];
    for (int i = 0; i < n; i++)
      _component[i] = i;
    int Find(int x) {
      while (_component[x] != x)
        x = _component[x] = _component[_component[x]];
      return x;
    }
    for (int e = 0; e < m; e++) {
      int a = Find(_edges[e].A);
      int b = Find(_edges[e].B);
      if (a != b)
        _component[Math.Max(a, b)] = Math.Min(a, b);
    }
    for (int i = 0; i < n; i++)
      _component[i] = Find(i);
    _componentNodes = new int[n];
    _componentEdges = new int[n];
    for (int i = 0; i < n; i++)
      _componentNodes[_component[i]]++;
    for (int e = 0; e < m; e++)
      _componentEdges[_component[_edges[e].A]]++;

    _adjacencyStart = new int[n + 1];
    for (int e = 0; e < m; e++) {
      _adjacencyStart[_edges[e].A + 1]++;
      _adjacencyStart[_edges[e].B + 1]++;
    }
    for (int i = 0; i < n; i++)
      _adjacencyStart[i + 1] += _adjacencyStart[i];
    _adjacencyEdge = new int[2 * m];
    var fill = (int[])_adjacencyStart.Clone();
    for (int e = 0; e < m; e++) {
      _adjacencyEdge[fill[_edges[e].A]++] = e;
      _adjacencyEdge[fill[_edges[e].B]++] = e;
    }

    var loops = new System.Collections.Generic.List<int>();
    for (int e = 0; e < m; e++) {
      int c = _component[_edges[e].A];
      if (_componentEdges[c] >= _componentNodes[c])
        loops.Add(e);
    }
    _loopEdge = [.. loops];
    _loopFrom = new int[_loopEdge.Length];
    _loopTo = new int[_loopEdge.Length];
    for (int e = 0; e < _loopEdge.Length; e++) {
      _loopFrom[e] = _edges[_loopEdge[e]].A;
      _loopTo[e] = _edges[_loopEdge[e]].B;
    }
    var grounds = new System.Collections.Generic.List<int>();
    for (int c = 0; c < n; c++)
      if (_component[c] == c && _componentEdges[c] >= _componentNodes[c])
        grounds.Add(c);
    _loopGround = [.. grounds];
    if (_loopEdge.Length > 0)
      _solver.SetTopology(n, _loopFrom, _loopTo, _loopEdge.Length);

    _roots = new int[n];
    _rootWeight = new float[n];
    _rootBest = new int[n];
    Array.Fill(_roots, -1);
    _order = new int[n];
    _parentEdge = new int[n];
    _parentNode = new int[n];
    _orderLength = -1;
    _injection = new double[n];
    _angle = new double[n];
    _edgeTorque = new float[m];
  }
}
