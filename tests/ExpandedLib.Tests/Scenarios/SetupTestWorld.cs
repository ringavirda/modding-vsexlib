using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Tests.Scenarios;

/// <summary>A <see cref="TestWorld"/> as a setup reads it; what each id placed and what each
/// molten cell holds are set by the test.</summary>
internal sealed class SetupTestWorld : ISetupWorld {
  private readonly Dictionary<string, SetupPlacement> _placed = new();
  private readonly Dictionary<BlockPos, MoltenCellReading> _molten = new();

  public TestWorld Test { get; } = new();

  public IWorldAccessor World => Test.World;

  public BlockEntity? BlockEntityAt(BlockPos pos) => Test.GetBlockEntity(pos);

  public Block BlockAt(BlockPos pos) => Test.GetBlock(pos);

  public object? NetworkAt(BlockPos pos, string type) =>
    Test.NetworkAt(pos) is { } network && network.NetworkType == type
      ? network
      : null;

  public IReadOnlyList<BlockPos> NetworkCells(
    BlockPos at,
    string networkType
  ) =>
    NetworkAt(at, networkType) is BlockNetwork network
      ? [.. network.Nodes]
      : [];

  public MoltenCellReading? MoltenCellAt(BlockPos pos) =>
    _molten.TryGetValue(pos, out MoltenCellReading cell) ? cell : null;

  public SetupPlacement? Placed(string id) => _placed.GetValueOrDefault(id);

  /// <summary>Records <paramref name="cells"/> as what <paramref name="id"/> laid, the first
  /// as its cell.</summary>
  public SetupTestWorld Laid(string id, params BlockPos[] cells) {
    _placed[id] = new SetupPlacement(
      cells[0].Copy(),
      [.. cells.Select(c => c.Copy())]
    );
    return this;
  }

  public SetupTestWorld Molten(BlockPos pos, MoltenCellReading cell) {
    _molten[pos.Copy()] = cell;
    return this;
  }
}
