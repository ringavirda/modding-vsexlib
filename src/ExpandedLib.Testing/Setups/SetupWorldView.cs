#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ExpandedLib.Industry.Molten;
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
  private readonly ConditionalWeakTable<
    MechanicalNetwork,
    MechanicalLine
  > _lines = new();

  public IWorldAccessor World => world.World;

  public BlockEntity? BlockEntityAt(BlockPos pos) => world.GetBlockEntity(pos);

  public Block BlockAt(BlockPos pos) => world.GetBlock(pos);

  public object? NetworkAt(BlockPos pos, string type) =>
    type == "mechanical"
      ? world.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()?.Network
        is { } line
        ? _lines.GetValue(line, l => new MechanicalLine(l))
        : null
      : world.NetworkAt(pos) is { } network && network.NetworkType == type
        ? network
        : null;

  public MoltenCellReading? MoltenCellAt(BlockPos pos)
  {
    BlockEntity? be = world.GetBlockEntity(pos);
    return
      (be as IMoltenCell ?? be?.GetBehavior<BEBehaviorMoltenCell>()) is { } cell
      ? new MoltenCellReading(
        cell.CellAmount,
        cell.CellMetalType,
        cell.CellTemperature
      )
      : null;
  }

  public SetupPlacement? Placed(string id) => placed.GetValueOrDefault(id);
}

/// <summary>A mechanical line as a setup reads it: the speed without its sign, the torque and the
/// resistance.</summary>
internal sealed class MechanicalLine(MechanicalNetwork network)
{
  public float Speed => Math.Abs(network.Speed);

  public float TotalAvailableTorque => network.TotalAvailableTorque;

  public float NetworkResistance => network.NetworkResistance;
}
#endif
