using ExpandedLib.Config;

namespace Grains;

/// <summary>
/// The module's one tunable, generated into a typed <c>GrainsValues</c> accessor by
/// <c>ExConfigGenerator</c>. Loaded from and written to the <c>grains</c> section of
/// <c>ModConfig/grains.json</c>.
/// </summary>
[ExConfigRegister("grains.json", "grains", Manageable = true)]
public class GrainsConfig : IExVersionedConfig {
  /// <summary>Mod version that last wrote this file. Managed by the config store - do not set by hand.</summary>
  public string? ConfigVersion { get; set; }

  /// <summary>Grain pieces one sack stands for when a mill takes it; 1 to 64.</summary>
  [ExConfigRange(1, 64)]
  public int SackSize { get; set; } = 8;
}
