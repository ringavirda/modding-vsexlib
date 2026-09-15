using ExpandedLib.Config;

namespace PlatedPipes;

/// <summary>
/// The plated tier's tunables, generated into a typed <c>PlatedPipesValues</c> accessor by
/// <c>ExConfigGenerator</c>. Loaded from and written to the <c>platedpipes</c> section of
/// <c>ModConfig/platedpipes.json</c>.
/// </summary>
[ExConfigRegister("platedpipes.json", "platedpipes", Manageable = true)]
public class PlatedPipesConfig : IExVersionedConfig {
  /// <summary>Mod version that last wrote this file. Managed by the config store - do not set by hand.</summary>
  public string? ConfigVersion { get; set; }

  /// <summary>Pressure (atm) above which a plain plated segment bursts; 0.1 to 50.</summary>
  [ExConfigRange(0.1f, 50f)]
  public float PlatedPipeBurstPressure { get; set; } = 2.5f;

  /// <summary>Litres per second a plain plated segment will pass; 1 to 1000.</summary>
  [ExConfigRange(1f, 1000f)]
  public float PlatedPipeThroughput { get; set; } = 50f;
}
