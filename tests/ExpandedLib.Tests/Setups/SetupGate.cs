#if GAME_GE_1_22
using System.Collections.Generic;
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

internal sealed class SetupSealedGateBlock : BlockNetworkNode
{
  public override string NetworkType => "pipe";

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "straight", ["we"] } };

  protected override string GetFallbackOrientation(string? type) => "we";

  public override bool HasConnectorAt(BlockFacing face) =>
    face == BlockFacing.WEST;

  public override bool IsValidNonNetworkConnection(
    Block neighborBlock,
    BlockFacing face
  ) => true;
}
#endif
