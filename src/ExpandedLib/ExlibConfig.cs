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

  /// <summary>Gas (L/s) bled per open-ended pipe connector.</summary>
  public float GasLeakRate { get; set; } = 8.0f;

  /// <summary>Liquid (L/s) drained from the network per open-ended pipe connector.</summary>
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

  #region Molten network
  /// <summary>Max metal (units) flowing across one canal connection per second.</summary>
  public int MoltenFlowRate { get; set; } = 50;

  /// <summary>Minimum metal (units) that must move across a canal connection for any flow that tick
  /// (stops sub-unit dribbles).</summary>
  public int MoltenMinFlowAmount { get; set; } = 10;

  /// <summary>Default time-based cooldown speed stamped on a molten carrier stack when a caller gives
  /// none.</summary>
  public float MoltenCooldownDefault { get; set; } = 24f;

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

  /// <summary>Burst shaft speed (rad/s); the weakest flywheel on a run sets the effective
  /// ceiling.</summary>
  [ExConfigRange(0.1, 1000)] // capacity scales with its square - must stay positive
  public float MpMaxSpeed { get; set; } = 2f;

  /// <summary>Gear-mesh loss for a transmission coupling, as a fraction of the coupled energy lost per
  /// second. 0 is a lossless mesh.</summary>
  [ExConfigRange(0, 1)]
  public float MpGearMeshLoss { get; set; } = 0.02f;
  #endregion

  #region Diagnostics
  /// <summary>Whether <c>ExpandedLibModSystem.AssetsFinalize</c> runs
  /// <c>ExpandedLib.Checks.ExlibChecks.All</c> after the catalogues load and logs the results.</summary>
  public bool RunChecksOnLoad { get; set; } = true;
  #endregion
}
