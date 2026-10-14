using System.Collections.Generic;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Scenarios;

/// <summary>The world a setup's readings and positions are read against: the harness's fake
/// world or a live server's.</summary>
internal interface ISetupWorld {
  /// <summary>Handed to a member method whose parameters take an <see cref="IWorldAccessor"/>
  /// or an <see cref="IBlockAccessor"/>.</summary>
  IWorldAccessor World { get; }

  BlockEntity? BlockEntityAt(BlockPos pos);

  /// <summary>The block at the cell; air when nothing is there.</summary>
  Block BlockAt(BlockPos pos);

  /// <summary>The network of <paramref name="type"/> (<c>pipe</c>, <c>molten</c>,
  /// <c>mechanical</c>, ...) the cell is a node of, or null.</summary>
  object? NetworkAt(BlockPos pos, string type);

  /// <summary>The cells of the <see cref="BlockNetwork"/> of <paramref name="networkType"/> that the
  /// setup-local cell <paramref name="at"/> is a node of, as setup-local positions in no particular
  /// order. Empty when the cell is in no such network or the type is not a block network's.</summary>
  IReadOnlyList<BlockPos> NetworkCells(BlockPos at, string networkType);

  /// <summary>What a molten cell's block entity holds, or null when the cell holds none.</summary>
  MoltenCellReading? MoltenCellAt(BlockPos pos);

  /// <summary>What the step or layout entry with <paramref name="id"/> placed, or null when
  /// nothing has placed it yet.</summary>
  SetupPlacement? Placed(string id);
}

/// <summary>A molten cell's metal: <paramref name="Amount"/> in units, the metal's full code
/// (empty when empty), its temperature in C.</summary>
internal readonly record struct MoltenCellReading(
  int Amount,
  string Metal,
  float Temperature
);

/// <summary>Where an id was placed: <paramref name="Cell"/> the cell it went into (a run's
/// first cell), <paramref name="Cells"/> every cell it laid, <paramref name="Frame"/> the
/// structure frame taken at placement, null for a block that is no exlib structure.</summary>
internal sealed record SetupPlacement(
  BlockPos Cell,
  IReadOnlyList<BlockPos> Cells,
  SetupFrame? Frame = null
);

/// <summary>An exlib structure's frame as it was placed: the principal, its
/// <see cref="ExpandedLib.Helpers.ExOrientation"/> angle in degrees, its footprint cells
/// already turned, and where an authored layout cell lands (null for a megablock).</summary>
internal sealed record SetupFrame(
  BlockPos Principal,
  int Angle,
  IReadOnlyList<FillerCell> Footprint,
  System.Func<Vec3i, BlockPos>? Layout = null
);
