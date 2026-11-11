using System;
using System.Linq;
using System.Text;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// The one-way link from an "mpshaft" run into a vanilla mechanical-power network: a consumer on the iron shaft and a
/// generator node on the vanilla network, held at the iron speed over <c>ratio</c> and <see cref="RadSPerVanillaUnit"/>.
/// </summary>
/// <remarks>
/// It works in its own node's frame (<c>GearedRatio</c> g), gives the network only the torque the rest of it lacks to hold
/// the speed, and loads the iron run with that torque times <see cref="NmPerVanillaUnit"/> over <c>ratio</c> N*m. Iron never
/// takes vanilla torque: a producer that carries the load alone leaves it giving nothing, and a stopped, suspended or
/// backward-turning network costs the iron nothing. Declared, it reads <c>ratio</c> (3 when absent), <c>ratedNm</c> (3280 when
/// absent) and <c>facings</c> (one axis, the first the discovery face; north and south when absent); the block also declares a
/// network member on "mpshaft". The iron speed and the torque given are saved and synced.
/// </remarks>
[BlockEntityBehaviorRegister]
public class BEBehaviorMpTakeoff(BlockEntity blockentity)
  : BEBehaviorMPBase(blockentity),
    IMpEnergyConsumer {
  /// <summary>Torque (N*m) of one vanilla torque unit.</summary>
  public const float NmPerVanillaUnit = 7000f;

  /// <summary>Speed (rad/s) of one vanilla speed unit.</summary>
  public const float RadSPerVanillaUnit = 5f;

  /// <summary>Default <c>ratio</c>: iron turns per vanilla-side turn.</summary>
  public const float DefaultRatio = 3f;

  /// <summary>Default <c>ratedNm</c>: torque (N*m) at the iron side the link carries.</summary>
  public const float DefaultRatedNm = 3280f;

  // Vanilla torque units per vanilla speed unit of error.
  internal const float HoldGain = 0.5f;

  // Share of the gap between the torque given and the torque wanted that one step closes.
  internal const float TorqueBlend = 0.5f;

  // Vanilla speed units a network may run against the link's sense before the link gives and takes nothing.
  internal const float BackwardSpeed = 0.001f;

  // Changes in the iron speed (rad/s) and the torque given (vanilla units) that earn a sync.
  internal const float SyncSpeedStep = 0.05f;
  internal const float SyncTorqueStep = 0.01f;

  private BlockFacing[] _facings = [BlockFacing.NORTH, BlockFacing.SOUTH];
  private float _ratio = DefaultRatio;
  private float _ratedNm = DefaultRatedNm;
  private float _ironSpeed;
  private float _given;
  private int _stepsSinceLoad;
  private float _sentSpeed;
  private float _sentGiven;

  /// <summary>Iron turns per vanilla-side turn, above zero.</summary>
  public float Ratio => _ratio;

  /// <summary>Torque (N*m) at the iron side the link carries.</summary>
  public float RatedNm => _ratedNm;

  /// <summary>Torque (vanilla units) the link gives the network at most.</summary>
  public float RatedUnits => _ratedNm * _ratio / NmPerVanillaUnit;

  /// <summary>Speed (vanilla units) the link holds its own axle at: the iron speed last read, over the ratio and
  /// <see cref="RadSPerVanillaUnit"/>; 0 once the vanilla network has stepped <see cref="StaleSteps"/> times on the
  /// server without the iron run loading the link.</summary>
  public float HeldSpeed =>
    Stale ? 0f : _ironSpeed / _ratio / RadSPerVanillaUnit;

  /// <summary>True while the link gives no torque and its axle turns faster than <see cref="HeldSpeed"/> in the
  /// link's sense. A client holds the network's speed unsigned, so it compares the magnitude.</summary>
  public bool Overrunning =>
    Network != null
    && _given <= 0f
    && (
      Side == EnumAppSide.Client
        ? MathF.Abs(Network.Speed) * MathF.Abs(GearedRatio)
        : Sense * Network.Speed * GearedRatio
    ) > HeldSpeed;

  /// <summary>Torque (N*m) at the iron side the link takes now: the torque it gave the network last step, to the
  /// rating, times <see cref="NmPerVanillaUnit"/> over the ratio; 0 while the iron run is stopped or off a network.</summary>
  public float TorqueTakenNm =>
    Network == null || HeldSpeed <= 0f
      ? 0f
      : _given * NmPerVanillaUnit / _ratio;

  /// <summary>True while the link gives the network all the rating carries.</summary>
  public bool AtRating => Network != null && _given >= RatedUnits * 0.999f;

  /// <summary>Milliseconds between vanilla network steps: <c>MechanicalNetwork.updateNetwork</c> runs on every fifth of
  /// the mechanical listener's 20 ms ticks.</summary>
  internal const int VanillaStepMs = 100;

  /// <summary>Vanilla network steps without a <see cref="LoadTorque"/> call after which the iron speed reads as stale:
  /// two intervals of the iron run.</summary>
  internal const int StaleSteps = 2 * MpShaftNetwork.IntervalMs / VanillaStepMs;

  /// <summary>The side the link judges itself on; the behaviour's own side when null.</summary>
  internal EnumAppSide? SideOverride { get; set; }

  private EnumAppSide? Side => SideOverride ?? Api?.Side;

  // +1 when the network's positive sense is the one this link drives, -1 when discovery came in from the other end.
  private float Sense =>
    propagationDir == OutFacingForNetworkDiscovery ? 1f : -1f;

  private bool Stale =>
    Side == EnumAppSide.Server && _stepsSinceLoad > StaleSteps;

  /// <summary>The iron run's load on the link.</summary>
  /// <param name="speed">The iron node's speed, rad/s, signed.</param>
  /// <returns>Torque (N*m) the link takes at the iron side.</returns>
  public float LoadTorque(float speed) {
    _ironSpeed = MathF.Abs(speed);
    _stepsSinceLoad = 0;
    SyncIfMoved();
    return TorqueTakenNm;
  }

  public override float GetResistance() => 0f;

  public override float GetTorque(long tick, float speed, out float resistance) {
    resistance = 0f;
    if (_stepsSinceLoad <= StaleSteps)
      _stepsSinceLoad++;
    float sense = Sense;
    float held = HeldSpeed;
    if (Network == null || held <= 0f || sense * speed < -BackwardSpeed)
      return Give(0f) * sense;
    float g = MathF.Max(MathF.Abs(GearedRatio), 1e-3f);
    float others = sense * Network.NetworkTorque - g * _given;
    float need =
      (
        Network.NetworkResistance
        - others
        + HoldGain * (held - sense * speed) / g
      ) / g;
    float wanted = Math.Clamp(need, 0f, RatedUnits);
    float blended = _given + TorqueBlend * (wanted - _given);
    return Give(blended < 1e-4f ? 0f : blended) * sense;
  }

  private float Give(float torque) {
    _given = torque;
    SyncIfMoved();
    return torque;
  }

  internal bool SyncDue() {
    if (
      MathF.Abs(_ironSpeed - _sentSpeed) <= SyncSpeedStep
      && MathF.Abs(_given - _sentGiven) <= SyncTorqueStep
    )
      return false;
    _sentSpeed = _ironSpeed;
    _sentGiven = _given;
    return true;
  }

  private void SyncIfMoved() {
    if (SyncDue() && Api?.Side == EnumAppSide.Server)
      Blockentity.MarkDirty();
  }

  protected override CompositeShape? GetShape() => null;

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetFloat("ironSpeed", _ironSpeed);
    tree.SetFloat("given", _given);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    _ironSpeed = tree.GetFloat("ironSpeed");
    _given = tree.GetFloat("given");
    _sentSpeed = _ironSpeed;
    _sentGiven = _given;
    _stepsSinceLoad = 0;
  }

  internal void ReadProperties(JsonObject? properties) {
    _ratio = MathF.Max(
      properties?["ratio"].AsFloat(DefaultRatio) ?? DefaultRatio,
      1e-3f
    );
    _ratedNm = properties?["ratedNm"].AsFloat(DefaultRatedNm) ?? DefaultRatedNm;
    string[]? named = properties?["facings"].AsArray<string>();
    if (named is { Length: > 0 })
      _facings =
      [
        .. named
          .Select(f =>
            BlockFacing.FromCode(f) ?? BlockFacing.FromFirstLetter(f[0])
          )
          .OfType<BlockFacing>(),
      ];
  }

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    ReadProperties(properties);
    base.Initialize(api, properties);
    if (api.Side == EnumAppSide.Server)
      foreach (BlockFacing face in _facings.Skip(1))
        tryConnect(face);
  }

  public override void SetOrientations() {
    OutFacingForNetworkDiscovery = _facings.Length > 0 ? _facings[0] : null;
    AxisSign = OutFacingForNetworkDiscovery?.Axis switch {
      EnumAxis.X => [-1, 0, 0],
      EnumAxis.Y => [0, 1, 0],
      _ => [0, 0, -1],
    };
  }

  /// <summary>Fits the link to the given parameters, as a declaration does.</summary>
  /// <param name="ratio">Iron turns per vanilla-side turn, above zero.</param>
  /// <param name="ratedNm">Torque (N*m) at the iron side the link carries.</param>
  /// <param name="facings">The faces the vanilla axle meets, on one axis; the first is the discovery face.</param>
  public void Configure(
    float ratio,
    float ratedNm,
    params BlockFacing[] facings
  ) {
    _ratio = MathF.Max(ratio, 1e-3f);
    _ratedNm = ratedNm;
    if (facings.Length > 0)
      _facings = facings;
  }

  /// <summary>The body is drawn by the block; the link adds no mesh.</summary>
  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) => false;

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    if (Network == null)
      return;
    dsc.AppendLine(
      Lang.Get("exlib:mp-takeoff-info-taken", (int)MathF.Round(TorqueTakenNm))
    );
    dsc.AppendLine(
      Lang.Get(
        "exlib:mp-takeoff-info-speed",
        HeldSpeed.ToString("0.000"),
        (HeldSpeed * RadSPerVanillaUnit).ToString("0.00")
      )
    );
    if (Overrunning)
      dsc.AppendLine(Lang.Get("exlib:mp-takeoff-info-overrun"));
    else if (AtRating)
      dsc.AppendLine(
        Lang.Get("exlib:mp-takeoff-info-rating", (int)MathF.Round(_ratedNm))
      );
  }
}
