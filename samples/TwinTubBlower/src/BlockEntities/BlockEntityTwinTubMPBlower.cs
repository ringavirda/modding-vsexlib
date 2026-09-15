using System.Text;
using ExpandedLib;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TwinTubBlower.BlockEntities;

/// <summary>Mechanically driven pair of bellows that pushes cold ambient air into the network it
/// stands in; a <see cref="ExpandedLib.Industry.Pipes.BlockPipe"/> node driven by a
/// <see cref="BEBehaviorMPFillerPort"/> on the footprint's upper-rear cell.</summary>
[BlockEntityRegister]
public class BlockEntityTwinTubMPBlower : BlockEntityPipe, IRenderer {
  /// <summary>Structure-local cell hosting the mechanical-power port: the upper-rear cell of the
  /// 1x2x3 footprint, facing west.</summary>
  private static readonly Vec3i MpPortCell = new(0, 1, 0);

  /// <summary>Shortest interval between bellows-note plays, at least the clip length (~1.41s).</summary>
  private const long BellowsSoundIntervalMs = 1500;

  // Axle speed sampled on the last blow tick; server writes it, client reads it for the HUD.
  [Persist("blowerSpeed")]
  private float _lastSpeed;

  private long _blowTickId;
  private long _lastBellowsSoundMs;

  private ConstructedAnimator? _animator;

  /// <summary>The placed rotation, read from the block.</summary>
  private int Angle =>
    (Block as Blocks.BlockTwinTubMPBlower)?.StructureAngle ?? 0;

  /// <summary>True once the player has finished all five construction stages.</summary>
  public bool IsConstructed => _animator?.IsConstructed ?? false;

  /// <summary>Renders nothing; registered only for the per-render-frame callback that locks the
  /// cycle clip to the axle.</summary>
  public double RenderOrder => 0.0;

  public int RenderRange => 24;

  public void OnRenderFrame(float dt, EnumRenderStage stage) =>
    LockCycleToAxle();

  public void Dispose() { }

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    // Resolved on both sides (IsConstructed gates the blow tick); it only builds and poses on the client.
    _animator = new ConstructedAnimator(this, () => Block.Code.Path);
    _animator.Initialize(ApplyPose);
    // One blow per second, server-side; matches the network tick interval.
    if (api.Side == EnumAppSide.Server) {
      _blowTickId = RegisterGameTickListener(OnBlowTick, 1000);
      return;
    }

    (api as ICoreClientAPI)?.Event.RegisterRenderer(
      this,
      EnumRenderStage.Before,
      "twintubblower-cycle"
    );
  }

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    UnregisterTicks();
    _animator?.Dispose();
  }

  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    UnregisterTicks();
    _animator?.Dispose();
  }

  private void UnregisterTicks() {
    if (_blowTickId != 0) {
      UnregisterGameTickListener(_blowTickId);
      _blowTickId = 0;
    }
    // Renderer holds a reference to this entity; must unregister on removal to stop it writing frames.
    (Api as ICoreClientAPI)?.Event.UnregisterRenderer(
      this,
      EnumRenderStage.Before
    );
  }

  /// <summary>Samples the axle and pushes one second of air into the network, scaled by
  /// <see cref="SpeedFraction"/>.</summary>
  private void OnBlowTick(float dt) {
    if (!IsConstructed)
      return;

    float speed = PortSpeed();
    if (speed != _lastSpeed) {
      _lastSpeed = speed;
      MarkDirty();
    }
    if (SpeedFraction(speed) <= 0f)
      return;

    ExSounds.PlayThrottled(
      Api,
      Pos,
      ExSounds.Bellows,
      ref _lastBellowsSoundMs,
      BellowsSoundIntervalMs,
      0.5f,
      16f
    );
    if (
      ProduceAir(speed, dt) <= 0f
      && Block is Blocks.BlockTwinTubMPBlower block
    )
      ExParticles.GasLeak(Api.World, block.OutletCell(Pos), block.OutletFace);
  }

  /// <summary>Pushes <paramref name="dt"/> seconds of air into this blower's own network at axle
  /// speed <paramref name="speed"/>.</summary>
  /// <returns>Litres actually produced, or 0 below <see cref="TwinTubBlowerValues.TwinTubBlowerMinSpeed"/>,
  /// at the pressure ceiling, or with construction unfinished.</returns>
  public float ProduceAir(float speed, float dt) {
    if (!IsConstructed)
      return 0f;
    float fraction = SpeedFraction(speed);
    if (fraction <= 0f || dt <= 0f)
      return 0f;
    if (NetworkSystem?.GetNetworkAt(Pos) is not PipeNetwork net)
      return 0f;

    // Cold blast: the air enters at ambient temperature.
    // TryProduceGas clamps at the ceiling; landed litres is the pool's change, not the request.
    float before = net.State?.Volume ?? 0f;
    net.TryProduceGas(
      TwinTubBlowerValues.TwinTubBlowerOutputPerSecond * fraction * dt,
      AmbientTemperature,
      "Air",
      Api.World.BlockAccessor,
      maxOutputPressure: TwinTubBlowerValues.TwinTubBlowerMaxPressure
    );
    return GameMath.Max(0f, (net.State?.Volume ?? 0f) - before);
  }

  /// <summary>Ambient air temperature at the bellows in degrees Celsius, falling back to the configured
  /// world ambient where no climate is available.</summary>
  private float AmbientTemperature =>
    Api?.World?.BlockAccessor?.GetClimateAt(Pos)?.Temperature
    ?? ExlibValues.AmbientTemperature;

  /// <summary>Fraction of rated output at <paramref name="speed"/>: 0 at or below
  /// <see cref="TwinTubBlowerValues.TwinTubBlowerMinSpeed"/>, 1 at or above
  /// <see cref="TwinTubBlowerValues.TwinTubBlowerMaxSpeed"/>, linear between.</summary>
  public static float SpeedFraction(float speed) {
    float min = TwinTubBlowerValues.TwinTubBlowerMinSpeed;
    float max = TwinTubBlowerValues.TwinTubBlowerMaxSpeed;
    if (speed <= min)
      return 0f;
    if (max <= min)
      return 1f;
    return GameMath.Clamp((speed - min) / (max - min), 0f, 1f);
  }

  /// <summary>The mechanical-power port hosted on the footprint's upper-rear cell, or null if the
  /// filler entity has not loaded.</summary>
  private BEBehaviorMPFillerPort? Port() {
    BlockPos cell = ExOrientation.GlobalPos(
      Pos,
      MpPortCell.X,
      MpPortCell.Y,
      MpPortCell.Z,
      Angle
    );
    return Api
      .World.BlockAccessor.GetBlockEntity(cell)
      ?.GetBehavior<BEBehaviorMPFillerPort>();
  }

  /// <summary>The driving axle's speed, or 0 when no axle is coupled to the port cell.</summary>
  private float PortSpeed() =>
    Port() is { IsTurning: true } port ? port.Speed : 0f;

  #region Animation

  /// <summary>Holds one clip at a time: cycle while the bellows work, idle when they do not; one
  /// must always be active or the animator drops the mesh.</summary>
  private void ApplyPose() {
    bool blowing = SpeedFraction(_lastSpeed) > 0f;
    _animator?.Pose(util => {
      util.StopAnimation(blowing ? "idle" : "cycle");
      util.StartAnimation(
        new AnimationMetaData {
          Animation = blowing ? "cycle" : "idle",
          Code = blowing ? "cycle" : "idle",
          AnimationSpeed = 1f,
          EaseInSpeed = 10f,
          EaseOutSpeed = 5f,
        }.Init()
      );
    });
  }

  /// <summary>Pins the cycle clip's frame to the axle angle; called once per render frame.</summary>
  private void LockCycleToAxle() {
    if (SpeedFraction(_lastSpeed) <= 0f || Port() is not { } port)
      return;
    MPAnim.LockFrameToAngle(_animator?.AnimUtil, "cycle", DrivenAngle(port));
  }

  /// <summary>The axle's angle as a turn about the axis from the port face into the machine;
  /// negated on a west or north port, straight on east or south.</summary>
  private static float DrivenAngle(BEBehaviorMPFillerPort port) {
    Vec3i n = port.PortFacing.Normali;
    return (n.X + n.Z - n.Y) * port.CurrentAngleRad;
  }

  #endregion

  /// <summary>Re-poses on the client when the loaded speed crosses the idle/blowing threshold.</summary>
  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    bool wasBlowing = SpeedFraction(_lastSpeed) > 0f;
    base.FromTreeAttributes(tree, worldForResolving);

    if (SpeedFraction(_lastSpeed) > 0f != wasBlowing)
      ApplyPose();
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    // Pipe readout first (medium, throughput, pressure), then the bellows' own state.
    base.GetBlockInfo(forPlayer, dsc);

    float fraction = SpeedFraction(_lastSpeed);
    dsc.AppendLine(
      fraction <= 0f
        ? Lang.Get("twintubblower:blower-info-idle")
        : Lang.Get(
          "twintubblower:blower-info-blowing",
          ExMeasure.FlowRate(
            TwinTubBlowerValues.TwinTubBlowerOutputPerSecond * fraction
          ),
          (int)(fraction * 100f)
        )
    );
  }
}
