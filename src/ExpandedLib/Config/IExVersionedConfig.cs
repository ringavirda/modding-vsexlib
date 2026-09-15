namespace ExpandedLib.Config;

/// <summary>A JSON config POCO that records the mod version it was last written under.</summary>
public interface IExVersionedConfig {
  /// <summary>Mod version that last wrote this config file. Null on first run.</summary>
  string? ConfigVersion { get; set; }
}
