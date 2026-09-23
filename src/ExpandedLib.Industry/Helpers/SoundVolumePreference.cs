using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using ExpandedLib.Registries;

namespace ExpandedLib.Industry.Helpers;

/// <summary>The player's machine-sound volume, 0-1 in tenths, applied to
/// <see cref="ExSounds.MachineVolume"/>.</summary>
[PreferenceRegister]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SoundVolumePreference : IExPreference {
  public string Key => "sound";

  public IReadOnlyList<string> Options { get; } =
  ["1", "0.9", "0.8", "0.7", "0.6", "0.5", "0.4", "0.3", "0.2", "0.1", "0"];

  public string Default => "1";

  public void Apply(string value) =>
    ExSounds.MachineVolume = float.Parse(value, CultureInfo.InvariantCulture);
}
