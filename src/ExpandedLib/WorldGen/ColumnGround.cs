#if GAME_GE_1_22
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.WorldGen;

/// <summary>Reads and writes the 32 x 32 columns of the chunk column a deposit is generated into.</summary>
internal sealed class ColumnGround(IWorldAccessor world, IServerChunk[] chunks)
{
  private const int Size = 32;

  private readonly IMapChunk _map = chunks[0].MapChunk;

  /// <summary>Blocks from the world floor to the top of the chunk column.</summary>
  public int Height => chunks.Length * Size;

  /// <summary>Highest solid block of the column as terrain generation left it.</summary>
  public int TerrainY(int lx, int lz) =>
    _map.WorldGenTerrainHeightMap[lz * Size + lx];

  /// <summary>The block id at a column position; y is within <see cref="Height"/>.</summary>
  public int BlockId(int lx, int y, int lz) =>
    chunks[y / Size].Data.GetBlockIdUnsafe(Index(lx, y, lz));

  /// <summary>Writes a block id, clearing the fluid at the position when asked.</summary>
  public void Set(int lx, int y, int lz, int blockId, bool clearFluid)
  {
    var data = chunks[y / Size].Data;
    int index = Index(lx, y, lz);
    data.SetBlockUnsafe(index, blockId);
    if (clearFluid)
    {
      data.SetFluid(index, 0);
    }
  }

  /// <summary>The highest block at or below the terrain whose material is stone or ore, or 0 when
  /// the column has none.</summary>
  public int RockTop(int lx, int lz)
  {
    for (int y = Math.Min(TerrainY(lx, lz), Height - 1); y >= 1; y--)
    {
      int id = BlockId(lx, y, lz);
      if (
        id != 0
        && world.GetBlock(id).BlockMaterial
          is EnumBlockMaterial.Stone
            or EnumBlockMaterial.Ore
      )
      {
        return y;
      }
    }
    return 0;
  }

  private static int Index(int lx, int y, int lz) =>
    ((y % Size) * Size + lz) * Size + lx;
}
#endif
