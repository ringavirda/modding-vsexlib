using System;

namespace ExpandedLib.Config;

/// <summary>Declares the valid numeric range for a config tunable, enforced by <see
/// cref="ExConfigRegister{TConfig}"/> on a live edit and on load. Values without this attribute are
/// guarded as non-negative and finite.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ExConfigRangeAttribute : Attribute {
  /// <summary>Smallest accepted value (inclusive).</summary>
  public double Min { get; }

  /// <summary>Largest accepted value (inclusive); <see cref="double.PositiveInfinity"/> for no upper bound.</summary>
  public double Max { get; }

  /// <summary>Bounds the value to <c>[min, +∞)</c> - a floor only (e.g. a capacity that must be positive).</summary>
  public ExConfigRangeAttribute(double min)
    : this(min, double.PositiveInfinity) { }

  /// <summary>Bounds the value to the inclusive range <c>[min, max]</c> (e.g. a fraction to <c>[0, 1]</c>).</summary>
  public ExConfigRangeAttribute(double min, double max) {
    Min = min;
    Max = max;
  }
}
