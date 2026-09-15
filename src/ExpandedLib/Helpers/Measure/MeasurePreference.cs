using System.Collections.Generic;
using System.ComponentModel;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;

namespace ExpandedLib.Helpers;

/// <summary>Metric/imperial display-unit preference, applied to <see cref="ExMeasure.System"/>.</summary>
[PreferenceRegister]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MeasurePreference : IExPreference {
  public string Key => "measure";

  public IReadOnlyList<string> Options { get; } = ["metric", "imperial"];

  public string Default => "metric";

  public void Apply(string value) => ExMeasure.System = ExMeasure.Parse(value);
}
