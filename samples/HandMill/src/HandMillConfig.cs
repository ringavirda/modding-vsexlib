using ExpandedLib.Config;

namespace HandMill;

/// <summary>
/// The mill's tunables, generated into a typed <c>HandMillValues</c> accessor by
/// <c>ExConfigGenerator</c>. Loaded from and written to the <c>handmill</c> section of
/// <c>ModConfig/handmill.json</c>.
/// </summary>
[ExConfigRegister("handmill.json", "handmill", Manageable = true)]
public class HandMillConfig : IExVersionedConfig {
  /// <summary>Mod version that last wrote this file. Managed by the config store - do not set by hand.</summary>
  public string? ConfigVersion { get; set; }

  /// <summary>Seconds one click keeps the crank driving; 1 to 120.</summary>
  [ExConfigRange(1, 120)]
  public int WindSeconds { get; set; } = 10;

  /// <summary>Crank torque at rest, N m, easing to zero at the run's burst speed; 1 to 500.</summary>
  [ExConfigRange(1f, 500f)]
  public float CrankTorque { get; set; } = 40f;

  /// <summary>Torque the mill draws while grinding, N m; 0.1 to 100.</summary>
  [ExConfigRange(0.1f, 100f)]
  public float GrindTorque { get; set; } = 15f;

  /// <summary>Shaft speed below which the mill does not grind, rad/s; 0.1 to 50.</summary>
  [ExConfigRange(0.1f, 50f)]
  public float MinGrindSpeed { get; set; } = 1f;

  /// <summary>Rotational inertia of one shaft, kg m2; 0.01 to 100.</summary>
  [ExConfigRange(0.01f, 100f)]
  public float ShaftInertia { get; set; } = 0.5f;

  /// <summary>Rotational inertia of the flywheel, kg m2; 1 to 1000.</summary>
  [ExConfigRange(1f, 1000f)]
  public float FlywheelInertia { get; set; } = 40f;
}
