using ExpandedLib.Networks;
using ExpandedLib.Registries;

namespace WidgetNamespace.BlockEntities;

/// <summary>
/// Block entity for a <see cref="Blocks.BlockWidget"/>: a pass-through node of the widgetnet run.
/// Graph membership (add, remove, connectivity) is handled by the
/// <see cref="BlockEntityNetworkNode"/> base.
/// </summary>
[BlockEntityRegister]
public class BlockEntityWidget : BlockEntityNetworkNode {
  public override string NetworkType {
    get => "widgetnet";
    set { }
  }

  /// <summary>How much this node buffers before yielding to a neighbour's pull. Tune per network.</summary>
  public const float Inertia = 0.5f;
}
