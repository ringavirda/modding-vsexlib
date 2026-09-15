using System;

namespace ExpandedLib.Config;

/// <summary>Marks a config POCO implementing <see cref="IExVersionedConfig"/> for which
/// <c>ExConfigGenerator</c> emits a static accessor class: the backing
/// <see cref="ExConfigRegister{TConfig}"/>, <c>Load(ICoreAPI)</c> and one property per config value.</summary>
[AttributeUsage(
  AttributeTargets.Class,
  AllowMultiple = false,
  Inherited = false
)]
public sealed class ExConfigRegisterAttribute : Attribute {
  /// <param name="fileName">Config file name under the game's <c>ModConfig</c> folder (e.g. <c>"ex_values.json"</c>).</param>
  /// <param name="modId">Owning mod id; resolves the running version and tags log lines.</param>
  public ExConfigRegisterAttribute(string fileName, string modId) {
    FileName = fileName;
    ModId = modId;
  }

  /// <summary>Config file name under the game's <c>ModConfig</c> folder.</summary>
  public string FileName { get; }

  /// <summary>The owning mod id.</summary>
  public string ModId { get; }

  /// <summary>Name of the generated accessor class; defaults to the config type name with a
  /// trailing <c>Config</c> swapped for <c>Values</c>, or the type name plus <c>Values</c>
  /// otherwise.</summary>
  public string? AccessorName { get; set; }

  /// <summary>Former names this config file used under <c>ModConfig</c>; if <see cref="FileName"/>
  /// is absent but one of these exists, it is renamed to the current name (first match wins).</summary>
  public string[]? LegacyFileNames { get; set; }

  /// <summary>Mod ids whose section of <see cref="FileName"/> this config now owns - the mods this
  /// one was renamed from or absorbed. Distinct from <see cref="LegacyFileNames"/>, which carries a
  /// legacy file rather than a section.</summary>
  public string[]? LegacySectionIds { get; set; }

  /// <summary>When <c>true</c>, the generated accessor registers this store with
  /// <see cref="ExpandedLib.Config.ExConfigProfiles"/> at load, exposing it to the generic
  /// <c>/exmod config</c> command.</summary>
  public bool Manageable { get; set; }
}
