using ExpandedLib.Networks;

namespace ExpandedLib.Testing;

/// <summary>A <see cref="BlockEntityNetworkNode"/> whose connectivity can be toggled at runtime via
/// <see cref="Broken"/>. Never <c>Initialize</c>d: attached through <see cref="TestWorld.Place"/> and
/// read directly by the graph.</summary>
public sealed class SeverableNode : BlockEntityNetworkNode {
  private string _networkType = "test";

  /// <summary>When <c>true</c>, this node severs the network at its position.</summary>
  public bool Broken { get; set; }

  public override string NetworkType {
    get => _networkType;
    set => _networkType = value;
  }

  public override bool IsConnectionBroken() => Broken;
}
