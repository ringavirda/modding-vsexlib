using System.ComponentModel;
using ExpandedLib.Config;

namespace ExpandedLib;

/// <summary>
/// JSON-serializable gameplay tunables owned by the shared library (<c>exlib</c>), loaded from the
/// <c>exlib</c> section of <c>ModConfig/ex_values.json</c>. Gas and liquid volumes are litres; molten
/// flow is in canal units.
/// </summary>
[ExConfigRegister(
  "ex_values.json",
  "exlib",
  LegacyFileNames = new string[] { "exlib_values.json" },
  Manageable = true
)]
[EditorBrowsable(EditorBrowsableState.Never)]
public class ExlibConfig : IExVersionedConfig {
  /// <summary>Mod version that last wrote this file. Managed by the config store - do not set by hand.</summary>
  public string? ConfigVersion { get; set; }

  /// <summary>Version-driven default resets, applied when a tunable's coded default changes.</summary>
  public static readonly ExConfigMigration[] Migrations = [];

  #region World
  /// <summary>World ambient reference temperature (C) used by machine heat models.</summary>
  public float AmbientTemperature { get; set; } = 20f;
  #endregion

  #region Pipe network
  /// <summary>Litres a single pipe holds at 1 atm (both the gas and water pools).</summary>
  [ExConfigRange(1, 1_000_000)] // pipe capacity divides pressure - must stay positive
  public float LitresPerPipe { get; set; } = 30f;

  /// <summary>Gas (L/s) each open end of a gas run loses at 1 atm; the loss is in proportion to the
  /// run's pressure.</summary>
  public float GasLeakRate { get; set; } = 8.0f;

  /// <summary>Liquid (L/s) each open end of a water run drains.</summary>
  public float LiquidLeakRate { get; set; } = 10.0f;

  /// <summary>Water (L) lost to natural evaporation per in-game day (pipe water pool and the boiler
  /// pool that reuses this rate).</summary>
  public float EvaporationLitresPerDay { get; set; } = 50f;

  /// <summary>Seconds a pipe run may sit at its weakest pipe's burst pressure with nowhere to vent
  /// before a pipe bursts.</summary>
  public float PipeOverpressureSeconds { get; set; } = 30f;

  /// <summary>Degrees C per second a pipe run's gas sheds toward
  /// <see cref="PipeAmbientTemperature"/>.</summary>
  public float PipeGasCoolPerSecond { get; set; } = 2.0f;

  /// <summary>Temperature (C) a pipe run's gas cools toward.</summary>
  public float PipeAmbientTemperature { get; set; } = 20f;
  #endregion

  #region Fast-forward
  /// <summary>Milliseconds of real time one network listener call spends replaying the game time a group missed; every due group still gets one step a call. 0 replays the whole gap in one call.</summary>
  [ExConfigRange(0, 200)]
  public int FastForwardBudgetMs { get; set; } = 20;

  /// <summary>Machine seconds (120 to a game hour) of a gap a replay steps exactly; the rest of a longer gap is skipped.</summary>
  [ExConfigRange(1, 86_400)]
  public int FastForwardExactSeconds { get; set; } = 1440;
  #endregion

  #region Molten network
  /// <summary>Max metal (units) flowing across one canal connection per second.</summary>
  public int MoltenFlowRate { get; set; } = 50;

  /// <summary>Metal (units) a level cell of the molten network keeps when it hands the rest onward;
  /// also the minimum a single transfer moves, for callers that apply a floor.</summary>
  public int MoltenMinFlowAmount { get; set; } = 10;

  /// <summary>Canal hops, counted along the network's connections, within which an open mold
  /// (<c>IMoltenCell.IsOpenMold</c>) draws metal toward itself; 0 or less draws nothing.</summary>
  public int MoltenMoldDrawRadius { get; set; } = 3;

  /// <summary>Default time-based cooldown speed stamped on a molten carrier stack when a caller gives
  /// none.</summary>
  public float MoltenCooldownDefault { get; set; } = 24f;

  /// <summary>Fraction (0..1) of a spilled charge that a chiselled puddle returns as metal bits; set
  /// below a mould's full return so a spill never beats a mould.</summary>
  [ExConfigRange(0, 1)]
  public float MoltenSpillYield { get; set; } = 0.75f;

  /// <summary>Temperature (C), at or over which, of the hottest metal in a molten holder burns an entity
  /// standing on its top face.</summary>
  public float MoltenBurnMinTemperature { get; set; } = 200f;

  /// <summary>Fire damage one molten-holder burn deals; one hit at most per second per entity.</summary>
  public float MoltenBurnDamage { get; set; } = 1f;

  /// <summary>Most set cells of one metal a single crowbar use breaks out of a run, the struck cell
  /// included.</summary>
  [ExConfigRange(1, 64)]
  public int MoltenBarCells { get; set; } = 8;

  /// <summary>Fraction of the melting point above which a metal stack counts as liquid (flows).</summary>
  public float MetalLiquidThreshold { get; set; } = 0.8f;

  /// <summary>Fraction of the melting point below which a metal stack counts as fully hardened
  /// (chisellable).</summary>
  public float MetalHardenedThreshold { get; set; } = 0.3f;

  /// <summary>Below this temperature (C) hot metal emits no incandescent block light.</summary>
  public float MetalGlowMinTemp { get; set; } = 500f;

  // The recovery-item fallback is content knowledge, not a framework default; see
  // MetalRegistry.DefaultRecoveryFallback (Industry) and IiexConfig.MetalRecoveryFallback.
  #endregion

  #region Mechanical-energy network
  // Models each run as a spinning shaft: dw/dt = (drive - load - friction)/I, stored energy = I*w^2/2.

  /// <summary>Windage and bearing friction coefficient <c>b</c> (N-m per rad/s).</summary>
  public float MpFrictionCoeff { get; set; } = 0.05f;

  /// <summary>Standing-resistance torque floor (N-m) any drive must beat to keep the shaft
  /// turning.</summary>
  [ExConfigRange(0, 1000)]
  public float MpIdleTorque { get; set; } = 0.5f;

  /// <summary>Burst shaft speed (rad/s): one ceiling for every mechanical-energy run, the cap its
  /// speed is clamped to and the speed its storage capacity is sized at.</summary>
  [ExConfigRange(0.1, 1000)] // capacity scales with its square - must stay positive
  public float MpMaxSpeed { get; set; } = 2f;

  /// <summary>Gear-mesh loss of one geared station, as a fraction of the torque it carries. 0 is a
  /// lossless mesh.</summary>
  [ExConfigRange(0, 1)]
  public float MpGearMeshLoss { get; set; } = 0.02f;

  /// <summary>Whether an mpshaft segment past its strain or ultimate parts and a flywheel past its
  /// rim strain bursts and a belt worn out snaps; off, strain and wear still accrue and nothing
  /// breaks, a worn belt running on at wear 1.</summary>
  public bool MpShaftFailure { get; set; } = true;

  /// <summary>Time constant (in-game hours) of strain: a segment at twice its rating, or a rim at
  /// twice its speed limit, strains by 1 in this time.</summary>
  [ExConfigRange(0.01, 1000)]
  public float MpStrainTauHours { get; set; } = 1f;

  /// <summary>Torque (N-m) a belt on a size 1 pulley grips before it slips.</summary>
  [ExConfigRange(0, 100000)]
  public float MpBeltCapSize1Nm { get; set; } = 390f;

  /// <summary>Torque (N-m) a belt on a size 2 pulley grips before it slips.</summary>
  [ExConfigRange(0, 100000)]
  public float MpBeltCapSize2Nm { get; set; } = 1300f;

  /// <summary>Fraction of its cap a belt carries while it slips.</summary>
  [ExConfigRange(0.1, 1)]
  public float MpBeltSlipFraction { get; set; } = 0.8f;

  /// <summary>Shaft speed (rad/s) a belt's wear is rated at: its life is the energy it slips at its
  /// slip torque and this speed over one in-game day.</summary>
  [ExConfigRange(0.01, 1000)]
  public float MpBeltRatedRadS { get; set; } = 2f;

  /// <summary>Torque (N-m) a clutch on a line shaft carries once its clamp is fully on.</summary>
  [ExConfigRange(0, 100000)]
  public float MpClutchCapLineNm { get; set; } = 2000f;

  /// <summary>Torque (N-m) a clutch on a main shaft carries once its clamp is fully on.</summary>
  [ExConfigRange(0, 100000)]
  public float MpClutchCapMainNm { get; set; } = 5000f;

  /// <summary>Seconds a clutch's clamp takes to come on from nothing to its cap.</summary>
  [ExConfigRange(0.1, 600)]
  public float MpClutchRampS { get; set; } = 3f;

  /// <summary>Torque (N-m) an overrunning coupling carries locked before it slips.</summary>
  [ExConfigRange(0, 1000000)]
  public float MpOverrunCapNm { get; set; } = 17000f;
  #endregion

  #region Diagnostics
  /// <summary>Whether the server runs <c>ExpandedLib.Checks.ExlibChecks.All</c> at
  /// <c>EnumServerRunPhase.GameReady</c>, after every mod's <c>StartServerSide</c>, and logs the
  /// results.</summary>
  public bool RunChecksOnLoad { get; set; } = true;
  #endregion
}
