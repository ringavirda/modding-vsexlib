using ExpandedLib.Config;

namespace WidgetNamespace;

/// <summary>
/// The mod's tunables, generated into a typed <c>WidgetValues</c> accessor by
/// <c>ExConfigGenerator</c>. Loaded from and written to the <c>widgetdomain</c> section of
/// <c>ModConfig/widgetdomain.json</c>.
/// </summary>
[ExConfigRegister("widgetdomain.json", "widgetdomain", Manageable = true)]
public class WidgetConfig : IExVersionedConfig {
  /// <summary>Mod version that last wrote this file. Managed by the config store - do not set by hand.</summary>
  public string? ConfigVersion { get; set; }

  /// <summary>How many widgets a batch produces; 1 to 100.</summary>
  [ExConfigRange(1, 100)]
  public int WidgetCount { get; set; } = 10;
}
