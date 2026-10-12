#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>A scene's <see cref="TestWorld"/> as the file's positions and readings see it.</summary>
internal sealed class SetupWorldView(
  TestWorld world,
  IReadOnlyDictionary<string, SetupPlacement> placed
) : ISetupWorld
{
  public IWorldAccessor World => world.World;

  public BlockEntity? BlockEntityAt(BlockPos pos) => world.GetBlockEntity(pos);

  public Block BlockAt(BlockPos pos) => world.GetBlock(pos);

  public object? NetworkAt(BlockPos pos, string type) =>
    type == "mechanical"
      ? world.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()?.Network
    : world.NetworkAt(pos) is { } network && network.NetworkType == type
      ? network
    : null;

  public MoltenCellReading? MoltenCellAt(BlockPos pos) => null;

  public SetupPlacement? Placed(string id) => placed.GetValueOrDefault(id);
}
#endif
