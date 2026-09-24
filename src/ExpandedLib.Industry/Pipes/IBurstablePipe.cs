namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// A pipe-network block that can fail under over-pressure. The network walks its nodes for the
/// weakest <see cref="BurstPressure"/> among the <see cref="CanBurst"/> ones and fails one such block
/// when the over-pressure grace runs out.
/// </summary>
public interface IBurstablePipe {
  /// <summary>Whether this block participates in the burst model. <see cref="BlockPipe"/> answers
  /// true only when the block's class is exactly <see cref="BlockPipe"/>, so a subclass (passthrough,
  /// outlet, valve, machine port) does not burst unless it overrides this.</summary>
  bool CanBurst { get; }

  /// <summary>The pressure (atm) at which this block bursts.</summary>
  float BurstPressure { get; }
}
