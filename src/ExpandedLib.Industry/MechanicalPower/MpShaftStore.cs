using System;
using System.Collections.Generic;
using ExpandedLib.Networks;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// Node store of the "mpshaft" type as parallel arrays, grouped by rigid run, and the implicit
/// backward-Euler step that turns each group. A group turns at one signed reference speed
/// <c>w</c> (rad/s); a node turns at <c>ratio * w</c>. Drives, loads and mesh carry are declared per
/// node before <see cref="Step"/> and read at the node's own speed.
/// </summary>
internal sealed class MpShaftStore {
  private readonly Dictionary<NodeKey, int> _index = [];

  private float[] _speed = new float[4];
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

  public int NodeCount => _nodes;

  public int GroupCount => _groups;

  /// <summary>Adds a group at rest or at <paramref name="speed"/> and returns its index.</summary>
  /// <param name="speed">Reference speed in rad/s, signed.</param>
  public int AddGroup(float speed = 0f) {
    if (_groups == _speed.Length) {
      int size = _groups * 2;
      Array.Resize(ref _speed, size);
      Array.Resize(ref _inertiaSum, size);
      Array.Resize(ref _active, size);
      Array.Resize(ref _activeSlope, size);
      Array.Resize(ref _resist, size);
      Array.Resize(ref _resistSlope, size);
    }
    _speed[_groups] = speed;
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
    _index[key] = node;
    _group[node] = group;
    _inertia[node] = MathF.Max(0f, inertiaKgM2);
    _standing[node] = MathF.Max(0f, standingNm);
    _ratio[node] = ratio;
    _efficiency[node] = 1f;
    return node;
  }

  public int IndexOf(NodeKey key) => _index[key];

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
  /// Advances every group by <paramref name="dt"/> with the declared drives, loads and meshes. Loads,
  /// standing torque and mesh loss only oppose motion: a turning group with them over its drive stops at
  /// zero, and a group at rest stays there while the drive at rest does not exceed their sum. A group with no
  /// inertia keeps its speed.
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
    for (int g = 0; g < _groups; g++) {
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
    }
  }
}
