using System;

namespace ExpandedLib.Industry.Pipes;

/// <summary>Material class of a pipe edge in the physical network; sets its friction factor.</summary>
internal enum FlowPipeClass {
  Cast,
  Plated,
  Rolled,
  Wrought,
}

internal static class FlowPipeClasses {
  /// <summary>The class of a pipe block of tier <paramref name="tier"/>: plated for <see cref="BlockPipe.PlatedTier"/>, rolled for <see cref="BlockPipe.RolledTier"/>, cast for any other tier or none.</summary>
  public static FlowPipeClass OfTier(string? tier) =>
    tier switch {
      BlockPipe.PlatedTier => FlowPipeClass.Plated,
      BlockPipe.RolledTier => FlowPipeClass.Rolled,
      _ => FlowPipeClass.Cast,
    };

  /// <summary>Darcy friction factor of <paramref name="pipeClass"/>: 0.03 cast, 0.025 plated, 0.02 rolled and wrought.</summary>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="pipeClass"/> is not a defined class.</exception>
  public static double Friction(this FlowPipeClass pipeClass) =>
    pipeClass switch {
      FlowPipeClass.Cast => 0.03,
      FlowPipeClass.Plated => 0.025,
      FlowPipeClass.Rolled => 0.02,
      FlowPipeClass.Wrought => 0.02,
      _ => throw new ArgumentOutOfRangeException(nameof(pipeClass)),
    };
}
