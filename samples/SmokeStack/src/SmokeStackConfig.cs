using ExpandedLib.Config;

namespace SmokeStack;

/// <summary>
/// The stack's tunables, generated into a typed <c>SmokeStackValues</c> accessor by
/// <c>ExConfigGenerator</c>. Loaded from and written to the <c>smokestack</c> section of
/// <c>ModConfig/smokestack.json</c>.
/// </summary>
[ExConfigRegister("smokestack.json", "smokestack", Manageable = true)]
public class SmokeStackConfig : IExVersionedConfig {
  /// <summary>Mod version that last wrote this file. Managed by the config store - do not set by hand.</summary>
  public string? ConfigVersion { get; set; }

  /// <summary>Litres of gas the stack draws off its network per production tick; 1 to 1000.</summary>
  [ExConfigRange(1f, 1000f)]
  public float SmokestackGasIntakeVolume { get; set; } = 48f;
}
