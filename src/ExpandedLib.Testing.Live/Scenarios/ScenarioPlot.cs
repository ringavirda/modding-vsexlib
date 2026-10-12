using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Testing.Live;

/// <summary>The chunk columns a plot holds, and the block at the middle of them its origin is
/// found above.</summary>
internal readonly record struct PlotBox(
  int MinColumnX,
  int MinColumnZ,
  int MaxColumnX,
  int MaxColumnZ,
  int CenterX,
  int CenterZ
);

/// <summary>Hands out plots in a row along +X (scenarios.md, Plots and coordinates).</summary>
internal sealed class PlotAllocator {
  public const int ColumnSize = 32;

  /// <summary>Blocks kept clear beyond a plot's size on every side.</summary>
  public const int Margin = 8;

  private readonly int _spawnZ;
  private int _nextColumnX;

  /// <param name="spawnX">The world spawn's block X.</param>
  /// <param name="spawnZ">The world spawn's block Z.</param>
  public PlotAllocator(int spawnX, int spawnZ) {
    _spawnZ = spawnZ;
    _nextColumnX = (int)Math.Floor((spawnX + ColumnSize) / (double)ColumnSize);
  }

  /// <summary>Whole chunk columns that hold <paramref name="size"/> blocks and the margin
  /// either side of them.</summary>
  public static int Columns(int size) =>
    (2 * (size + Margin) + ColumnSize - 1) / ColumnSize;

  /// <summary>The next plot of <paramref name="sizeX"/> by <paramref name="sizeZ"/> blocks,
  /// east of every plot handed out before.</summary>
  public PlotBox Next(int sizeX, int sizeZ) {
    int columnsX = Columns(sizeX);
    int columnsZ = Columns(sizeZ);
    int minX = _nextColumnX;
    int minZ = (int)Math.Floor(_spawnZ / (double)ColumnSize) - columnsZ / 2;
    _nextColumnX += columnsX;
    return new PlotBox(
      minX,
      minZ,
      minX + columnsX - 1,
      minZ + columnsZ - 1,
      minX * ColumnSize + columnsX * ColumnSize / 2,
      minZ * ColumnSize + columnsZ * ColumnSize / 2
    );
  }
}

/// <summary>A plot on the server: its columns loaded and kept, its origin, and the clearing
/// that ends its case.</summary>
internal sealed class ScenarioPlot(PlotBox box, int[] size) {
  public PlotBox Box => box;

  /// <summary>The plot's size in blocks: x, y, z.</summary>
  public int[] Size => size;

  /// <summary>The first air cell above the ground at the plot's centre, absolute; set once the
  /// columns have loaded.</summary>
  public BlockPos Origin { get; private set; } = new(0, 0, 0);

  /// <summary>Whether the plot's chunk columns have loaded.</summary>
  public bool Loaded => Volatile.Read(ref _loaded) == 1;

  private int _loaded;

  /// <summary>Starts loading and keeping the plot's chunk columns; <see cref="Loaded"/> turns
  /// true when they are ready.</summary>
  public void Load(ICoreServerAPI api) =>
    api.WorldManager.LoadChunkColumnPriority(
      box.MinColumnX,
      box.MinColumnZ,
      box.MaxColumnX,
      box.MaxColumnZ,
      new ChunkLoadOptions {
        KeepLoaded = true,
        OnLoaded = () => Volatile.Write(ref _loaded, 1),
      }
    );

  /// <summary>Sets <see cref="Origin"/> from the loaded ground; call on the main thread once
  /// <see cref="Loaded"/>.</summary>
  public void Locate(IWorldAccessor world) {
    int ground = world.BlockAccessor.GetTerrainMapheightAt(
      new BlockPos(box.CenterX, 0, box.CenterZ)
    );
    Origin = new BlockPos(box.CenterX, ground + 1, box.CenterZ);
  }

  /// <summary>The plot's cuboid from <c>y = 0</c> up, absolute: the cells <see cref="Clear"/>
  /// empties and a failed case saves.</summary>
  public (BlockPos Min, BlockPos Max) Bounds() =>
    (
      new BlockPos(Origin.X - size[0], Origin.Y, Origin.Z - size[2]),
      new BlockPos(
        Origin.X + size[0],
        Origin.Y + 2 * size[1],
        Origin.Z + size[2]
      )
    );

  /// <summary>Sets the plot to air from <c>y = 0</c> up and removes the item entities in
  /// it.</summary>
  public void Clear(IWorldAccessor world) {
    (BlockPos min, BlockPos max) = Bounds();
    IBlockAccessor blocks = world.BlockAccessor;
    var at = new BlockPos(0, 0, 0);
    for (int x = min.X; x <= max.X; x++)
      for (int z = min.Z; z <= max.Z; z++)
        for (int y = min.Y; y <= max.Y; y++) {
          at.Set(x, y, z);
          if (blocks.GetBlock(at).Id != 0)
            blocks.SetBlock(0, at);
        }
    foreach (
      Entity item in world.GetEntitiesInsideCuboid(
        min,
        max,
        e => e is EntityItem
      )
    )
      item.Die(EnumDespawnReason.Removed);
  }
}

/// <summary>The climate every region that holds a plot keeps (scenarios.md, Climate): 20 C and no
/// rain.</summary>
internal sealed class ClimateHold(int regionSize) {
  public const float Temperature = 20f;
  public const float Rainfall = 0f;

  private volatile ImmutableHashSet<(int X, int Z)> _regions = [];

  /// <summary>Whether the <c>OnGetClimate</c> handler is registered; set by the runner that
  /// registers it.</summary>
  public bool Attached { get; set; }

  /// <summary>The region index of block coordinate <paramref name="block"/>.</summary>
  public static int Region(int block, int regionSize) =>
    (int)Math.Floor(block / (double)regionSize);

  /// <summary>Holds the region of the block at (<paramref name="x"/>, <paramref name="z"/>).</summary>
  public void Hold(int x, int z) =>
    _regions = _regions.Add((Region(x, regionSize), Region(z, regionSize)));

  /// <summary>The <c>OnGetClimate</c> handler: overwrites every climate read inside a held
  /// region, in every mode.</summary>
  public void Apply(
    ref ClimateCondition climate,
    BlockPos pos,
    EnumGetClimateMode mode,
    double totalDays
  ) {
    if (
      climate == null
      || !_regions.Contains(
        (Region(pos.X, regionSize), Region(pos.Z, regionSize))
      )
    )
      return;
    climate.Temperature = Temperature;
    climate.WorldGenTemperature = Temperature;
    climate.Rainfall = Rainfall;
    climate.WorldgenRainfall = Rainfall;
  }
}
