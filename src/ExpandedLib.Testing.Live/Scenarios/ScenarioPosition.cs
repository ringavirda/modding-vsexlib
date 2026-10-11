using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing.Live;

/// <summary>A plot as a setup file's positions read it: every position the file and the model
/// pass is plot-local, and this adds the plot's origin on the way to the server's world.</summary>
internal sealed class ScenarioWorld(
  IWorldAccessor world,
  BlockPos origin,
  BlockNetworkModSystem? networks
) : ISetupWorld {
  private readonly Dictionary<string, SetupPlacement> _placed = [];

  public IWorldAccessor World => world;

  public BlockPos Origin => origin;

  public BlockPos Absolute(BlockPos local) =>
    new(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z);

  public BlockPos Local(BlockPos absolute) =>
    new(absolute.X - origin.X, absolute.Y - origin.Y, absolute.Z - origin.Z);

  public BlockEntity? BlockEntityAt(BlockPos pos) =>
    world.BlockAccessor.GetBlockEntity(Absolute(pos));

  public Block BlockAt(BlockPos pos) =>
    world.BlockAccessor.GetBlock(Absolute(pos));

  public object? NetworkAt(BlockPos pos, string type) {
    if (type == "mechanical")
      return BlockEntityAt(pos)?.GetBehavior<BEBehaviorMPBase>()?.Network;
    BlockNetwork? network = networks?.GetNetworkAt(Absolute(pos));
    return network is { } found && found.NetworkType == type ? found : null;
  }

  public MoltenCellReading? MoltenCellAt(BlockPos pos) =>
    BlockEntityAt(pos)?.GetBehavior<BEBehaviorMoltenCell>() is { } cell
      ? new MoltenCellReading(
        cell.CellAmount,
        cell.CellMetalType,
        cell.CellTemperature
      )
      : null;

  public SetupPlacement? Placed(string id) => _placed.GetValueOrDefault(id);

  public IReadOnlyDictionary<string, SetupPlacement> AllPlaced => _placed;

  public void Place(string id, SetupPlacement placement) =>
    _placed[id] = placement;

  /// <summary>The structure frame of the block just placed at <paramref name="local"/>, or
  /// null when it is neither a megablock nor a multiblock.</summary>
  public SetupFrame? FrameAt(BlockPos local) {
    Block block = BlockAt(local);
    BlockEntityMultiblockStructure? structure =
      BlockEntityAt(local) as BlockEntityMultiblockStructure;
    if (block is not IFillerHost && structure == null)
      return null;
    int angle = (block as BlockFilledMegastructure)?.StructureAngle
      ?? structure?.LayoutAngle
      ?? 0;
    IReadOnlyList<FillerCell> footprint =
      block is IFillerHost host
        ? StructureFillers.FootprintCells(host, local, angle)
        : [];
    Func<Vec3i, BlockPos>? layout =
      structure == null
        ? null
        : offset => Local(structure.PeripheralCell(offset.X, offset.Y, offset.Z));
    return new SetupFrame(local.Copy(), angle, footprint, layout);
  }
}
