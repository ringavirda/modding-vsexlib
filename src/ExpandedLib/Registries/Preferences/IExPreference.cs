using System.Collections.Generic;

namespace ExpandedLib.Registries;

/// <summary>A single per-player, client-side display preference. Implementations carry no
/// constructor state; the registry instantiates them through a parameterless constructor.</summary>
public interface IExPreference {
  /// <summary>Stable key for this preference, used as the config key, the <c>.exmod</c>
  /// sub-command name and the lang-key stem. Lower-case, no spaces.</summary>
  string Key { get; }

  /// <summary>The allowed values, lower-case; the first entry is shown first.</summary>
  IReadOnlyList<string> Options { get; }

  /// <summary>The value used when the player has made no choice yet; one of <see cref="Options"/>.</summary>
  string Default { get; }

  /// <summary>Applies a stored value to the live client state. <paramref name="value"/> is always
  /// one of <see cref="Options"/>.</summary>
  void Apply(string value);
}
