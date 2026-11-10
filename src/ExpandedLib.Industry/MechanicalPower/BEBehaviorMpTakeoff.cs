using System;
using System.Linq;
using System.Text;
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
/// The network's resistance sum loads the iron run with that sum times <see cref="NmPerVanillaUnit"/> over <c>ratio</c> N*m, at
/// efficiency 1. Iron never takes vanilla torque: while a vanilla producer runs the network faster than the link holds it
/// the link gives no torque and puts no load on iron, and a stopped iron run gives the vanilla side nothing.
/// Declared, it reads <c>ratio</c> (iron turns per vanilla-side turn, 3 when absent), <c>ratedNm</c> (torque at the iron
/// side the link carries, 3280 when absent) and <c>facings</c> (the faces the vanilla axle meets, in the placed
/// orientation, one axis; north and south when absent). The block also declares a network member on "mpshaft" for the iron node.
/// The generator holds the speed through its torque: <c>T = R + HoldGain * (held - speed)</c> in vanilla torque units,
/// R being the network's resistance sum of the last step, held speed in vanilla units, T clamped to 0 and the rating.
/// Vanilla's step adds <c>T - R</c> to the speed, so the speed closes on the held one geometrically and stays.
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

  // Vanilla speed units a network runs over the held speed before a producer counts as running it faster.
  internal const float OverrunBand = 0.005f;

  private BlockFacing[] _facings = [BlockFacing.NORTH, BlockFacing.SOUTH];
  private float _ratio = DefaultRatio;
  private float _ratedNm = DefaultRatedNm;
  private float _ironSpeed;

  /// <summary>Iron turns per vanilla-side turn, above zero.</summary>
  public float Ratio => _ratio;

  /// <summary>Torque (N*m) at the iron side the link carries.</summary>
  public float RatedNm => _ratedNm;

  /// <summary>Torque (vanilla units) the link gives the network at most.</summary>
  public float RatedUnits => _ratedNm * _ratio / NmPerVanillaUnit;

  /// <summary>Speed (vanilla units) the link holds the network at: the iron speed last read, over the ratio and
  /// <see cref="RadSPerVanillaUnit"/>.</summary>
  public float HeldSpeed => _ironSpeed / _ratio / RadSPerVanillaUnit;

  /// <summary>True while the iron run turns and the vanilla network runs faster than <see cref="HeldSpeed"/>
  /// by more than the overrun band, or stands over a stopped iron run.</summary>
  public bool Overrunning => Network != null && OverrunAt(Network.Speed);

  /// <summary>Torque (N*m) at the iron side the link takes now: the network's resistance sum, to the rating, times
  /// <see cref="NmPerVanillaUnit"/> over the ratio; 0 while overrun, off a network or with the iron run stopped.</summary>
  public float TorqueTakenNm =>
    Network == null ? 0f : TakenUnits() * NmPerVanillaUnit / _ratio;

  /// <summary>True while the network's resistance sum is over what <see cref="RatedUnits"/> carries.</summary>
  public bool AtRating =>
    Network != null && Network.NetworkResistance > RatedUnits;

  public float LoadTorque(float speed) {
    _ironSpeed = MathF.Abs(speed);
    return TorqueTakenNm;
  }

  public override float GetResistance() => 0f;

  public override float GetTorque(long tick, float speed, out float resistance) {
    resistance = 0f;
    if (Network == null || HeldSpeed <= 0f || OverrunAt(Network.Speed))
      return 0f;
    float error = HeldSpeed - MathF.Abs(Network.Speed);
    return Math.Clamp(
      Network.NetworkResistance + HoldGain * error,
      0f,
      RatedUnits
    );
  }

  private bool OverrunAt(float networkSpeed) =>
    MathF.Abs(networkSpeed) > HeldSpeed + OverrunBand;

  private float TakenUnits() =>
    HeldSpeed <= 0f || OverrunAt(Network.Speed)
      ? 0f
      : MathF.Min(Network.NetworkResistance, RatedUnits);

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
