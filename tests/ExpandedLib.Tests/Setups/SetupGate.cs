#if GAME_GE_1_22
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Tests.Setups;

internal sealed class SetupGateBlock : Block, INetworkConnector
{
  private bool ByPosition => Attributes?["byPosition"].AsBool() == true;

  public string NetworkType => "pipe";

  public bool HasConnectorAt(BlockFacing face) =>
    !ByPosition && face == BlockFacing.WEST;

  bool INetworkMember.HasConnectorAt(
    IBlockAccessor world,
    BlockPos pos,
    BlockFacing face
  ) => ByPosition && face == BlockFacing.WEST;
}
#endif
