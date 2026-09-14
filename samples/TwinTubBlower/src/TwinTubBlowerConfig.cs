using ExpandedLib.Config;

namespace TwinTubBlower;

/// <summary>
/// The blower's tunables, generated into a typed <c>TwinTubBlowerValues</c> accessor by
/// <c>ExConfigGenerator</c>. Loaded from and written to the <c>twintubblower</c> section of
/// <c>ModConfig/twintubblower.json</c>.
/// </summary>
[ExConfigRegister("twintubblower.json", "twintubblower", Manageable = true)]
public class TwinTubBlowerConfig : IExVersionedConfig {
  /// <summary>Mod version that last wrote this file. Managed by the config store - do not set by hand.</summary>
  public string? ConfigVersion { get; set; }

  /// <summary>Litres of air per second the blower pushes into its network at
  /// <see cref="TwinTubBlowerMaxSpeed"/>; 1 to 1000.</summary>
  [ExConfigRange(1f, 1000f)]
  public float TwinTubBlowerOutputPerSecond { get; set; } = 45f;

  /// <summary>Pressure ceiling (atm) the blower can raise its network to; 0.1 to 50.</summary>
  [ExConfigRange(0.1f, 50f)]
  public float TwinTubBlowerMaxPressure { get; set; } = 2.2f;

  /// <summary>Axle speed at/below which the blower delivers nothing, rad/s; 0 to 50.</summary>
  [ExConfigRange(0f, 50f)]
  public float TwinTubBlowerMinSpeed { get; set; } = 0.5f;

  /// <summary>Axle speed at/above which the blower delivers its full
  /// <see cref="TwinTubBlowerOutputPerSecond"/>, rad/s; 0.1 to 50.</summary>
  [ExConfigRange(0.1f, 50f)]
  public float TwinTubBlowerMaxSpeed { get; set; } = 1.5f;
}
