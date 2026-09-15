using ExpandedLib.Networks;

namespace ExpandedLib.Testing;

/// <summary>The smallest concrete <see cref="BlockEntityNetworkNode"/>: everything on the base
/// class, one settable network type, nothing else.</summary>
public sealed class OrientableNode : BlockEntityNetworkNode {
  public override string NetworkType { get; set; } = "test";
}
