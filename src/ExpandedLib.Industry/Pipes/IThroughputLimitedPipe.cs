namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// A pipe-network block that limits how much can move through a run per second. The network
/// takes the smallest <see cref="MaxThroughput"/> across its nodes. Fittings and ports return
/// <see cref="float.MaxValue"/>.
/// </summary>
public interface IThroughputLimitedPipe {
  /// <summary>Litres per second this block passes; the smallest across a run caps the whole run.</summary>
  float MaxThroughput { get; }

  /// <summary>Litres per second this block passes of <paramref name="medium"/> (the network's medium
  /// name, such as "Steam", "Air" or "Water"); the smallest across a run caps the whole run.</summary>
  /// <param name="medium">Medium name, or <c>null</c> for none.</param>
  /// <returns>L/s; <see cref="MaxThroughput"/> unless the implementation limits that medium separately.</returns>
  float ThroughputFor(string? medium) => MaxThroughput;
}
