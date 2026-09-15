namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// A pipe-network block that limits how much can move through a run per second. The network
/// takes the smallest <see cref="MaxThroughput"/> across its nodes. Fittings and ports return
/// <see cref="float.MaxValue"/>.
/// </summary>
public interface IThroughputLimitedPipe {
  /// <summary>Litres per second this block passes; the smallest across a run caps the whole run.</summary>
  float MaxThroughput { get; }
}
