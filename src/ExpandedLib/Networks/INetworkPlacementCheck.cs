using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>A <see cref="BlockNetwork"/> type that can refuse a member before it is placed. <see cref="BlockNetworkModSystem.CanPlaceMember"/> asks each live network of the type that borders the new cell.</summary>
public interface INetworkPlacementCheck {
  /// <summary>Whether the network would take <paramref name="block"/> standing at <paramref name="pos"/>. Reads the network and the world and changes neither.</summary>
  /// <param name="world">The accessor the placement reads.</param>
  /// <param name="pos">The cell the block would fill; nothing is placed there yet.</param>
  /// <param name="block">The variant about to be placed.</param>
  /// <param name="failureCode">Set on a refusal to a plain code with a <c>game:placefailure-</c> line in the lang files; empty otherwise.</param>
  /// <returns>False when the network refuses the member.</returns>
  bool AcceptsMember(
    IBlockAccessor world,
    BlockPos pos,
    Block block,
    out string failureCode
  );
}
