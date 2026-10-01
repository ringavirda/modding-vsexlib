namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// A pipe-network node's block entity that draws its run up a stack, the way a chimney on an
/// <see cref="IChimneyVentable"/> node does. <see cref="PipeNetwork.HasDraught"/> reads it.
/// </summary>
public interface IPipeDraught {
  /// <summary>True while the node draws: a stack that is built and standing.</summary>
  bool GivesDraught { get; }
}
