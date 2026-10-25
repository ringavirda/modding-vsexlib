using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// The physical shaft network, type "mpshaft": one rigid run whose nodes, joints and implicit step live in a
/// <see cref="MpShaftStore"/>. Runs merge by momentum, a split keeps the run's speed, and a run with a node in an
/// unloaded chunk holds still whole.
/// </summary>
internal sealed class MpShaftNetwork(BlockNetworkModSystem system)
  : BlockNetwork(system),
    INetworkPlacementCheck {
  public override string NetworkType => "mpshaft";

  public override int StepIntervalMs => 200;

  public override bool SupportsPartialSplit => true;

  private float _pendingInertia;
  private float _pendingSpeed;
  private readonly Dictionary<NodeKey, MpShaftRunState> _pending = [];
  private float _clockS;
  private readonly MpReadingGate _gate = new();
  private double _lastHours = -1d;
  private bool _jointsStale = true;
  private readonly List<(int Edge, IMpEnergyLink Link)> _links = [];
  private readonly Dictionary<IMpEnergyLink, int> _slipping = [];
  private readonly Dictionary<IMpEnergyLink, bool> _built = [];
  private readonly HashSet<IMpEnergyLink> _catching = [];
  private int _edgesSolved;
  private const int AngleSendTicks = 5;
  private const float AngleSendMoved = 0.02f;
  private NodeKey?[] _anchors = [];
  private float[] _sentSpeed = [];
  private int _anchorTopology = -1;
  private int _sendTicks;

  /// <summary>Overrides <c>MpShaftFailure</c> for this run when set.</summary>
  internal bool? FailureOverride { get; set; }

  /// <summary>The run's node store and joints.</summary>
  public MpShaftStore Store { get; private set; } = new();

  /// <summary>Reference speed of the run in rad/s, signed; a restored speed not yet taken into the store is mixed in by momentum.</summary>
  public float Speed {
    get {
      float own = Store.GroupCount > 0 ? Store.ReflectedInertia(0) : 0f;
      float live = Store.GroupCount > 0 ? Store.Speed(0) : 0f;
      var (pendingInertia, pendingMomentum) = PendingTotals();
      float total = own + pendingInertia;
      return total > 0f ? (own * live + pendingMomentum) / total : live;
    }
  }

  private (float Inertia, float Momentum) PendingTotals() {
    float inertia = _pendingInertia;
    float momentum = _pendingInertia * _pendingSpeed;
    foreach (var run in _pending.Values) {
      inertia += run.Inertia;
      momentum += run.Inertia * run.Speed;
    }
    return (inertia, momentum);
  }

  public override void OnTick(
    IBlockAccessor world,
    float dt,
    BlockNetworkModSystem manager
  ) {
    double now = manager.ServerWorld?.Calendar?.TotalHours ?? -1d;
    float hours =
      now >= 0d && _lastHours >= 0d ? (float)(now - _lastHours) : 0f;
    _lastHours = now;
    if (dt <= 0f)
      return;
    if (NodeKeys.Any(k => world.GetChunkAtBlockPos(k.Pos) == null)) {
      SendAngles(world, true);
      return;
    }
    Sync(world);
    if (_built.Any(b => b.Key.Engaged != b.Value))
      _jointsStale = true;
    if (_jointsStale)
      BuildJoints(world);
    SetNodeData(world);
    for (int pass = 0; pass < 2 && ReleaseOverCap(dt); pass++) {
      BuildJoints(world);
      SetNodeData(world);
    }
    var before = SlipSpeeds();
    Store.Step(dt, float.PositiveInfinity);
    Store.SolveTorquePath();
    _edgesSolved = Store.EdgeCount;
    AdvanceLinks(dt, MathF.Max(0f, hours), before);
    AccrueStrain(world, MathF.Max(0f, hours));
    _clockS += dt;
    foreach (var (key, node) in Store.Nodes) {
      if (world.GetBlockEntity(key.Pos) is not IMpEnergySegment segment)
        continue;
      var reading = new MpSegmentReading(
        Store.NodeTorque(node),
        segment.RatingNm,
        segment.Strain,
        Store.NodeSpeed(node),
        segment.Size
      );
      if (_gate.TryAdmit(key, reading, _clockS))
        segment.StoreReading(reading);
    }
    State = new MpShaftRunState(
      Store.GroupCount > 0 ? Store.Speed(0) : 0f,
      Store.GroupCount > 0 ? Store.ReflectedInertia(0) : 0f
    );
    BroadcastUpdate(world);
    SendAngles(world, false);
  }

  // Tells every client each run's angle and speed: all runs every AngleSendTicks ticks, and at once when a run's speed has moved by more than AngleSendMoved of it or stopped. A suspended run is sent at speed 0.
  private void SendAngles(IBlockAccessor world, bool suspended) {
    if (Store.GroupCount == 0)
      return;
    if (_anchorTopology != Store.Topology)
      RefreshAnchors(world);
    int[] runs =
    [
      .. Enumerable.Range(0, _anchors.Length).Where(g => _anchors[g] != null),
    ];
    float[] speeds = [.. runs.Select(g => suspended ? 0f : Store.Speed(g))];
    bool due = ++_sendTicks >= AngleSendTicks;
    for (int i = 0; i < runs.Length && !due; i++)
      due = SpeedMoved(_sentSpeed[runs[i]], speeds[i]);
    if (!due)
      return;
    _sendTicks = 0;
    for (int i = 0; i < runs.Length; i++)
      _sentSpeed[runs[i]] = speeds[i];
    MpRunAngles.Send(
      new MpRunAnglePacket {
        X = [.. runs.Select(g => _anchors[g]!.Value.Pos.X)],
        Y = [.. runs.Select(g => _anchors[g]!.Value.Pos.Y)],
        Z = [.. runs.Select(g => _anchors[g]!.Value.Pos.Z)],
        Slot = [.. runs.Select(g => (int)_anchors[g]!.Value.Slot)],
        Angle = [.. runs.Select(Store.Angle)],
        Speed = speeds,
      }
    );
  }

  private static bool SpeedMoved(float sent, float speed) =>
    float.IsNaN(sent)
    || (speed == 0f && sent != 0f)
    || MathF.Abs(speed - sent)
      > AngleSendMoved * MathF.Max(MathF.Abs(sent), 1f);

  // A run is named by its least node key, which survives the renumbering of groups; each cell's turning parts are told theirs.
  private void RefreshAnchors(IBlockAccessor world) {
    _anchors = new NodeKey?[Store.GroupCount];
    _sentSpeed = new float[Store.GroupCount];
    Array.Fill(_sentSpeed, float.NaN);
    foreach (var (key, node) in Store.Nodes) {
      int g = Store.GroupOf(node);
      if (_anchors[g] is not { } least || AnchorOrder(key, least) < 0)
        _anchors[g] = key;
    }
    foreach (var (key, node) in Store.Nodes)
      world
        .GetBlockEntity(key.Pos)
        ?.GetBehavior<BEBehaviorTurningParts>()
        ?.SetRun(
          key.Slot,
          _anchors[Store.GroupOf(node)]!.Value,
          Store.RatioOf(node)
        );
    _anchorTopology = Store.Topology;
    _sendTicks = AngleSendTicks;
  }

  private static int AnchorOrder(NodeKey a, NodeKey b) {
    int order = a.Pos.X.CompareTo(b.Pos.X);
    if (order == 0)
      order = a.Pos.Y.CompareTo(b.Pos.Y);
    if (order == 0)
      order = a.Pos.Z.CompareTo(b.Pos.Z);
    return order != 0 ? order : a.Slot.CompareTo(b.Slot);
  }

  private void SetNodeData(IBlockAccessor world) {
    foreach (var (edge, link) in _links) {
      int output = Store.IndexOf(link.To);
      float ratio = Store.RatioOf(output);
      float carried =
        edge < _edgesSolved && ratio != 0f
          ? Store.EdgeTorque(edge) / ratio
          : 0f;
      Store.SetMesh(output, link.Efficiency, carried);
    }
    foreach (var (key, node) in Store.Nodes) {
      var be = world.GetBlockEntity(key.Pos);
      float inertia = (be as IMpEnergyStorage)?.Inertia ?? 0f;
      float rise = inertia - Store.InertiaOf(node);
      if (rise > 0f) {
        float n = Store.RatioOf(node);
        Store.Mix(Store.GroupOf(node), rise * n * n, 0f);
      }
      Store.SetInertia(node, inertia);
      float w = Store.NodeSpeed(node);
      Store.SetStanding(
        node,
        ((be as IMpEnergySegment)?.StandingNm ?? 0f)
          + ((be as IMpEnergyConsumer)?.StandingNm ?? 0f)
      );
      Store.SetRating(node, (be as IMpEnergySegment)?.RatingNm ?? 0f);
      var producer = be as IMpEnergyProducer;
      Store.SetDrive(
        node,
        producer?.DriveTorque(w) ?? 0f,
        producer?.DriveSlope(w) ?? 0f
      );
      var consumer = be as IMpEnergyConsumer;
      Store.SetLoad(
        node,
        consumer?.LoadTorque(w) ?? 0f,
        consumer?.LoadSlope(w) ?? 0f
      );
      Store.SetSlip(node, 0f);
    }
    foreach (var (link, heading) in _slipping) {
      var (input, output, delta) = SlipEnds(link);
      float direction = delta != 0f ? MathF.Sign(delta) : heading;
      float torque = MathF.Max(0f, link.SlipTorque(delta));
      Store.AddSlip(input, -direction * torque);
      Store.AddSlip(output, direction * torque * link.Ratio * link.Sign);
    }
  }

  // Input over output speed taken to the input's side; zero when the sides turn as the link's ratio holds them.
  private (int Input, int Output, float Delta) SlipEnds(IMpEnergyLink link) {
    int input = Store.IndexOf(link.From);
    int output = Store.IndexOf(link.To);
    float delta =
      Store.NodeSpeed(input) - Store.NodeSpeed(output) * link.Ratio * link.Sign;
    return (input, output, delta);
  }

  // Tries the step with every locked link held; a link that must carry more than its cap starts to slip, in the direction it would have carried. Returns true when any did.
  private bool ReleaseOverCap(float dt) {
    if (_links.Count == 0)
      return false;
    var trial = Store.Clone();
    trial.Step(dt, float.PositiveInfinity);
    trial.SolveTorquePath();
    bool released = false;
    foreach (var (edge, link) in _links) {
      float ratio = trial.RatioOf(trial.IndexOf(link.From));
      float carried = trial.EdgeTorque(edge);
      if (ratio == 0f || MathF.Abs(carried / ratio) <= link.CapNm * 1.0001f)
        continue;
      _slipping[link] = MathF.Sign(carried) * MathF.Sign(ratio);
      released = true;
    }
    return released;
  }

  private Dictionary<IMpEnergyLink, float> SlipSpeeds() {
    var speeds = new Dictionary<IMpEnergyLink, float>();
    foreach (var link in _slipping.Keys)
      speeds[link] = SlipEnds(link).Delta;
    return speeds;
  }

  // Slipping links wear by the power they dissipate and lock again when their sides meet; worn-out links break.
  private void AdvanceLinks(
    float dt,
    float hours,
    Dictionary<IMpEnergyLink, float> before
  ) {
    bool failure = FailureOverride ?? ExlibValues.MpShaftFailure;
    foreach (var link in _built.Keys.ToList()) {
      float power = 0f;
      if (before.TryGetValue(link, out float start)) {
        var (input, output, delta) = SlipEnds(link);
        power =
          link.SlipTorque(start) * (MathF.Abs(start) + MathF.Abs(delta)) / 2f;
        float scale = MathF.Max(
          MathF.Abs(Store.NodeSpeed(input)),
          MathF.Abs(Store.NodeSpeed(output) * link.Ratio)
        );
        if (
          start != 0f
          && (
            start * delta <= 0f
            || (
              MathF.Abs(delta) < MathF.Abs(start)
              && MathF.Abs(delta) <= 0.01f * scale
            )
          )
        ) {
          _slipping.Remove(link);
          _catching.Add(link);
          _jointsStale = true;
        }
      }
      link.Advance(dt, hours, power);
      if (failure && link.WornOut) {
        link.Break();
        _jointsStale = true;
      }
    }
  }

  private void AccrueStrain(IBlockAccessor world, float hours) {
    bool failure = FailureOverride ?? ExlibValues.MpShaftFailure;
    float tau = ExlibValues.MpStrainTauHours;
    List<Action>? failed = null;
    foreach (var cell in Store.Nodes.GroupBy(n => n.Key.Pos)) {
      var be = world.GetBlockEntity(cell.Key);
      float load = cell.Max(n => Store.NodeLoad(n.Value));
      float torque = cell.Max(n => Store.NodeTorque(n.Value));
      float speed = cell.Max(n => MathF.Abs(Store.NodeSpeed(n.Value)));
      if (be is IMpEnergySegment segment) {
        float over = load - 1f;
        if (over > 0f)
          segment.Strain = MathF.Min(1f, segment.Strain + over * hours / tau);
        float ultimate = segment.UltimateNm;
        if (ultimate > 0f && torque >= ultimate)
          segment.Strain = 1f;
        if (failure && segment.Strain >= 1f)
          (failed ??= []).Add(segment.Part);
      }
      if (be is IMpEnergyRim { RimLimitRadS: > 0f } rim) {
        float over = speed / rim.RimLimitRadS - 1f;
        if (over > 0f)
          rim.RimStrain = MathF.Min(1f, rim.RimStrain + over * hours / tau);
        if (failure && rim.RimStrain >= 1f)
          (failed ??= []).Add(rim.Burst);
      }
    }
    if (failed != null)
      foreach (var fail in failed)
        fail();
  }

  // Restored speeds join by momentum before the nodes do; a node enters in a group of its own, at its restored speed or at rest, and the joints built next join it by momentum at their ratio.
  private void Sync(IBlockAccessor world) {
    if (Store.GroupCount == 0)
      Store.AddGroup();
    var entering = new List<(NodeKey Key, float Inertia, bool Restored)>();
    float unkeyedLeft = _pendingInertia;
    foreach (var key in NodeKeys) {
      if (Store.Nodes.ContainsKey(key))
        continue;
      float inertia =
        (world.GetBlockEntity(key.Pos) as IMpEnergyStorage)?.Inertia ?? 0f;
      bool restored = _pending.ContainsKey(key);
      if (!restored && unkeyedLeft > 0f) {
        restored = true;
        unkeyedLeft -= inertia;
      }
      entering.Add((key, inertia, restored));
    }
    var (pendingInertia, pendingMomentum) = PendingTotals();
    if (pendingInertia > 0f)
      Store.Mix(0, pendingInertia, pendingMomentum / pendingInertia);
    _pendingInertia = 0f;
    _pendingSpeed = 0f;
    _pending.Clear();
    float restoredSpeed = Store.Speed(0);
    foreach (var (key, inertia, restored) in entering)
      Store.AddNode(
        key,
        Store.AddGroup(restored ? restoredSpeed : 0f),
        inertia,
        0f
      );
    if (entering.Count > 0)
      _jointsStale = true;
  }

  // The store is rebuilt from the graph, so a joint whose crossing or link is gone is dropped and a merged run's seam joined.
  private void BuildJoints(IBlockAccessor world) {
    var joints = MpShaftJoints.Gather(
      world,
      NodeKeys,
      pos => NetworkMembership.Resolve(world, pos, NetworkType),
      pos => MpShaftJoints.LinksOf(world, pos)
    );
    _built.Clear();
    var apart = new HashSet<NodeKey>();
    foreach (var cell in NodeKeys.Select(k => k.Pos).Distinct())
      foreach (var link in MpShaftJoints.LinksOf(world, cell))
        if (NodeKeys.Contains(link.From) && NodeKeys.Contains(link.To)) {
          _built[link] = link.Engaged;
          apart.Add(link.From);
          apart.Add(link.To);
        }
    foreach (var link in _slipping.Keys.ToList())
      if (!_built.TryGetValue(link, out bool engaged) || !engaged)
        _slipping.Remove(link);
    foreach (var (link, engaged) in _built)
      if (
        engaged
        && !_slipping.ContainsKey(link)
        && !_catching.Contains(link)
        && Store.Nodes.ContainsKey(link.From)
        && Store.Nodes.ContainsKey(link.To)
      ) {
        var (input, output, delta) = SlipEnds(link);
        float scale = MathF.Max(
          MathF.Abs(Store.NodeSpeed(input)),
          MathF.Abs(Store.NodeSpeed(output) * link.Ratio)
        );
        if (MathF.Abs(delta) > 0.01f * scale)
          _slipping[link] = MathF.Sign(delta);
      }
    _catching.Clear();
    joints.RemoveAll(j => j.Link != null && _slipping.ContainsKey(j.Link));
    var rebuilt = new MpShaftStore();
    rebuilt.TakeLoose(Store, NodeKeys);
    foreach (var refused in MpShaftJoints.Apply(rebuilt, joints))
      if (refused.Link != null)
        _slipping[refused.Link] = 1;
    rebuilt.Settle(apart);
    Store = rebuilt;
    _edgesSolved = 0;
    _links.Clear();
    foreach (var joint in joints)
      if (joint.Link != null) {
        int edge = Store.EdgeIndex(
          Store.IndexOf(joint.Input),
          Store.IndexOf(joint.Output)
        );
        if (edge >= 0)
          _links.Add((edge, joint.Link));
      }
    _jointsStale = false;
  }

  /// <summary>Tries the joints <paramref name="block"/> would add at <paramref name="pos"/>, with every joint the run already has, on a copy of the store; the run itself is not touched.</summary>
  /// <param name="world">The accessor the joints are read through.</param>
  /// <param name="pos">The cell the block would fill.</param>
  /// <param name="block">The variant about to be placed; a block that is no network member is accepted.</param>
  /// <param name="failureCode">Set to <c>exlib-mpshaft-jam</c> when a new joint closes a ring whose ratio or sign product is not 1; empty otherwise.</param>
  /// <returns>False when a new joint would jam the run.</returns>
  public bool AcceptsMember(
    IBlockAccessor world,
    BlockPos pos,
    Block block,
    out string failureCode
  ) {
    failureCode = "";
    if (block is not INetworkMember member)
      return true;
    var scratch = Store.Clone();
    var keys = new HashSet<NodeKey>(NodeKeys);
    foreach (byte slot in member.Slots(world, pos))
      keys.Add(new NodeKey(pos, slot));
    foreach (var key in keys)
      if (!scratch.Nodes.ContainsKey(key))
        scratch.AddNode(key, scratch.AddGroup(), 0f, 0f);
    var links = (block as IMpEnergyLinkSource)?.LinksAt(world, pos) ?? [];
    var joints = MpShaftJoints.Gather(
      world,
      keys,
      p =>
        p.Equals(pos)
          ? member
          : NetworkMembership.Resolve(world, p, NetworkType),
      p => p.Equals(pos) ? links : MpShaftJoints.LinksOf(world, p)
    );
    bool Touches(MpShaftJoint j) =>
      j.Input.Pos.Equals(pos) || j.Output.Pos.Equals(pos);
    MpShaftJoints.Apply(scratch, joints.Where(j => !Touches(j)));
    if (MpShaftJoints.Apply(scratch, joints.Where(Touches)).Count == 0)
      return true;
    failureCode = "exlib-mpshaft-jam";
    return false;
  }

  public override void OnTopologyChanged() {
    _jointsStale = true;
    _gate.Retain(NodeKeys);
    foreach (
      var key in _pending.Keys.Where(k => !NodeKeys.Contains(k)).ToList()
    )
      _pending.Remove(key);
    if (Store.Nodes.Keys.All(NodeKeys.Contains))
      return;
    var kept = new MpShaftStore();
    kept.Take(Store, NodeKeys);
    Store = kept;
  }

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) {
    var run = (MpShaftNetwork)other;
    Store.Take(run.Store, run.Store.Nodes.Keys.ToHashSet());
    RestoreState(new MpShaftRunState(run._pendingSpeed, run._pendingInertia));
    foreach (var (key, saved) in run._pending)
      RestoreState(key, saved);
  }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  ) => throw new NotSupportedException("An mpshaft run splits by node keys.");

  public override void OnSplitFragment(
    BlockNetwork original,
    IReadOnlySet<NodeKey> fragmentKeys
  ) {
    var run = (MpShaftNetwork)original;
    Store.Take(run.Store, fragmentKeys);
    foreach (var (key, saved) in run._pending)
      if (fragmentKeys.Contains(key))
        _pending[key] = saved;
    if (run._pendingInertia > 0f) {
      _pendingInertia = run._pendingInertia;
      _pendingSpeed = run._pendingSpeed;
      run._pendingInertia = 0f;
      run._pendingSpeed = 0f;
    }
  }

  public override void InheritStateFrom(BlockNetwork source) {
    if (source is not MpShaftNetwork run)
      return;
    Store = run.Store;
    foreach (var (key, saved) in run._pending)
      RestoreState(key, saved);
    RestoreState(new MpShaftRunState(run._pendingSpeed, run._pendingInertia));
  }

  /// <summary>Takes a saved run into this one by momentum: with a restore already waiting, or with the nodes already in the store, the speed becomes the mean weighted by inertia. Anything but an <see cref="MpShaftRunState"/> is ignored. The restore belongs to no node, so a split hands it whole to the first fragment taken.</summary>
  public override void RestoreState(object? state) {
    if (state is not MpShaftRunState run || run.Inertia <= 0f)
      return;
    float total = _pendingInertia + run.Inertia;
    _pendingSpeed =
      (_pendingInertia * _pendingSpeed + run.Inertia * run.Speed) / total;
    _pendingInertia = total;
  }

  /// <summary>Takes a saved run into this one on behalf of node <paramref name="key"/>, by momentum with any restore already waiting for that node. A split hands it to the fragment holding the node, and removing the node drops it.</summary>
  /// <param name="key">The flywheel node whose block entity saved the run.</param>
  /// <param name="run">The saved run; ignored when its inertia is zero or less.</param>
  public void RestoreState(NodeKey key, MpShaftRunState run) {
    if (run.Inertia <= 0f)
      return;
    if (_pending.TryGetValue(key, out var waiting)) {
      float total = waiting.Inertia + run.Inertia;
      run = new MpShaftRunState(
        (waiting.Inertia * waiting.Speed + run.Inertia * run.Speed) / total,
        total
      );
    }
    _pending[key] = run;
  }

  /// <summary>Writes the run as <paramref name="ownInertia"/> of it, for a flywheel's block entity to save.</summary>
  /// <param name="host">The flywheel's tree.</param>
  /// <param name="ownInertia">The flywheel's inertia in kg*m^2.</param>
  public void SaveRun(ITreeAttribute host, float ownInertia) =>
    MpShaftSave.WriteRun(host, new MpShaftRunState(Speed, ownInertia));

  /// <summary>Takes the run saved in <paramref name="host"/> into this one for node <paramref name="key"/> by <see cref="RestoreState(NodeKey, MpShaftRunState)"/>.</summary>
  /// <param name="key">The flywheel node whose block entity holds <paramref name="host"/>.</param>
  /// <param name="host">The flywheel's tree.</param>
  /// <returns>False when the tree holds no readable run.</returns>
  public bool TryRestoreRun(NodeKey key, ITreeAttribute host) {
    if (!MpShaftSave.TryReadRun(host, out var run))
      return false;
    RestoreState(key, run);
    return true;
  }
}
