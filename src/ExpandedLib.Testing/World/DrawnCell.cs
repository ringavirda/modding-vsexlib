#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Structures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>A mesh handed to a chunk's terrain pool, with the level of detail it was added at and
/// whether it is the block's own mesh.</summary>
/// <param name="Mesh">The mesh, in cell-local blocks; a pool call with a transform matrix holds the transformed clone.</param>
/// <param name="Lod">The level of detail the mesh was added at.</param>
/// <param name="FromBlock">True for the block's default mesh, false for one an entity or behaviour pooled.</param>
public readonly record struct PooledMesh(
  MeshData Mesh,
  int Lod,
  bool FromBlock
);

/// <summary>What the game's chunk tesselator keeps for one cell: the meshes the cell's block entity
/// pools through its <c>OnTesselation</c>, or the block's own mesh when that returns false, read
/// from a client twin built with <see cref="TestWorld.ClientTesselates"/> on.</summary>
/// <remarks>The rule is the one <c>JsonTesselator.Tesselate</c> and <c>BleedingJsonTesselator.Tesselate</c>
/// apply, and only for the draw types <c>JSON</c>, <c>JSONAndWater</c> and <c>JSONAndSnowLayer</c>: the entity is
/// called, a <see cref="RetryTesselationException"/> passes through, another exception is kept in
/// <see cref="Threw"/> and the block's own mesh is added after the meshes already pooled, true keeps
/// the pooled meshes only, false adds the block's own mesh. Any other draw type never calls the
/// entity and draws the block's own mesh, empty for <c>Empty</c> and for a block with no
/// shape. Lengths are blocks, cell-local: the cell's own unit cube spans 0 to 1 on each axis.</remarks>
public sealed class DrawnCell
{
  /// <summary>How far, in blocks, a vertex may lie outside the footprint and still count inside: one
  /// voxel, which admits the trims and lips shapes carry.</summary>
  public const double Tolerance = 1.0 / 16;

  private readonly List<PooledMesh> _meshes;
  private readonly List<string> _keys;
  private readonly List<BlockPos> _footprint;

  private DrawnCell(
    BlockPos pos,
    Block block,
    bool skipsBlockMesh,
    Exception? threw,
    Exception? blockMeshThrew,
    List<PooledMesh> meshes,
    List<string> keys,
    bool animatorDraws,
    bool redrawMarked,
    List<BlockPos> footprint
  )
  {
    Pos = pos;
    Block = block;
    SkipsBlockMesh = skipsBlockMesh;
    Threw = threw;
    BlockMeshThrew = blockMeshThrew;
    _meshes = meshes;
    _keys = keys;
    AnimatorDraws = animatorDraws;
    RedrawMarked = redrawMarked;
    _footprint = footprint;
    (Vertices, Indices, Bounds, VerticesOutside, Overshoot) = Measure();
  }

  /// <summary>The cell drawn.</summary>
  public BlockPos Pos { get; }

  /// <summary>The block in the cell.</summary>
  public Block Block { get; }

  /// <summary>True when the entity's <c>OnTesselation</c> returned true and so the block's own mesh
  /// was not added; false for a cell with no entity, a draw type that never calls it, or a
  /// false or thrown result.</summary>
  public bool SkipsBlockMesh { get; }

  /// <summary>The exception the entity's <c>OnTesselation</c> threw, or null.</summary>
  public Exception? Threw { get; }

  /// <summary>The exception the block's own mesh threw while tesselating (a missing shape file, a
  /// refused call), or null; the block then adds no mesh.</summary>
  public Exception? BlockMeshThrew { get; }

  /// <summary>The meshes kept for the cell, in the order they were added.</summary>
  public IReadOnlyList<PooledMesh> Meshes => _meshes;

  /// <summary>The vertex count over every kept mesh.</summary>
  public int Vertices { get; }

  /// <summary>The index count over every kept mesh.</summary>
  public int Indices { get; }

  /// <summary>The box around every kept vertex, or null when no mesh holds a vertex.</summary>
  public Cuboidd? Bounds { get; }

  /// <summary>The texture keys the tesselations of this draw asked the stand-in texture source for,
  /// in order, repeats included.</summary>
  public IReadOnlyList<string> TextureKeys => _keys;

  /// <summary>True when the twin's animation utility holds an animation that is running or still
  /// easing out, so the game's animated renderer draws the cell each frame.</summary>
  public bool AnimatorDraws { get; }

  /// <summary>True when the server or the twin marked the cell for redraw (<c>MarkBlockDirty</c>)
  /// since the cell was last drawn through this class; reading it takes the mark.</summary>
  public bool RedrawMarked { get; }

  /// <summary>The cells whose unit cubes the drawing may lie in, relative to <see cref="Pos"/>: the
  /// cell itself, the filler cells naming it as principal, and, for a formed multiblock anchor, its
  /// layout cells.</summary>
  public IReadOnlyList<BlockPos> Footprint => _footprint;

  /// <summary>The count of kept vertices lying farther than <see cref="Tolerance"/> from every
  /// footprint cell's unit cube, a vertex with a NaN coordinate included.</summary>
  public int VerticesOutside { get; }

  /// <summary>The farthest any kept vertex lies from its nearest footprint cell's unit cube, in
  /// blocks along the worst axis; infinite when a coordinate is NaN, 0 when no vertex is
  /// outside the cubes.</summary>
  public double Overshoot { get; }

  /// <summary>True when the cell draws nothing the game would show: no kept index and no running
  /// animation.</summary>
  public bool DrawsNothing => Indices == 0 && !AnimatorDraws;

  /// <summary>Draws the cell <paramref name="twin"/> stands in, from its current state.</summary>
  /// <remarks>The twin is not synced first; call <see cref="ClientTwin.Sync"/> when the server
  /// changed. The block is the one the twin was made for.</remarks>
  /// <param name="twin">A twin stood by <see cref="ClientTwin.Stand"/>.</param>
  /// <returns>What the chunk tesselator keeps for the cell.</returns>
  /// <exception cref="RetryTesselationException">The entity's <c>OnTesselation</c> throws it, as the
  /// game lets it pass.</exception>
  public static DrawnCell Of(ClientTwin twin) =>
    Draw(
      twin.World,
      twin.Twin.Pos,
      twin.Twin.Block ?? twin.Server.Block,
      twin.Twin,
      twin.Server
    );

  /// <summary>Draws the cell at <paramref name="pos"/>, standing a client twin for its entity when
  /// none stands there.</summary>
  /// <remarks>A twin stood here runs three client frames of 1/30 s before the draw, so a pose an
  /// entity starts from its first tick is running. A cell without an entity draws only its block's own mesh. Set
  /// <see cref="TestWorld.ClientTesselates"/> before this call.</remarks>
  /// <param name="world">The world holding the cell, with its entity initialised.</param>
  /// <param name="pos">The cell.</param>
  /// <returns>What the chunk tesselator keeps for the cell.</returns>
  /// <exception cref="InvalidOperationException"><see cref="TestWorld.ClientTesselates"/> is off, or
  /// the first shape read finds no game install.</exception>
  /// <exception cref="RetryTesselationException">The entity's <c>OnTesselation</c> throws it.</exception>
  public static DrawnCell Of(TestWorld world, BlockPos pos)
  {
    RequireSwitch(world);
    Block block = world.GetBlock(pos);
    BlockEntity? server = world.GetBlockEntity(pos);
    BlockEntity? twin = server == null ? null : world.StandingTwin(pos);
    if (twin == null && server != null)
    {
      ClientTwin stood = ClientTwin.Stand(world, server)[0];
      stood.Frames(1f / 30f, 3);
      twin = stood.Twin;
    }
    return Draw(world, pos, block, twin, server);
  }

  private static DrawnCell Draw(
    TestWorld world,
    BlockPos pos,
    Block block,
    BlockEntity? twin,
    BlockEntity? server
  )
  {
    RequireSwitch(world);
    DrawTesselator tess = world.DrawTesselator;
    tess.Textures.Clear();
    var pool = new RecordingMeshPool();
    bool skips = false;
    Exception? threw = null;
    Exception? blockMeshThrew = null;
    if (twin != null && CallsOnTesselation(block.DrawType))
    {
      try
      {
        skips = twin.OnTesselation(pool, tess);
      }
      catch (RetryTesselationException)
      {
        throw;
      }
      catch (Exception e)
      {
        threw = e;
      }
    }
    if (!skips)
    {
      try
      {
        if (block.DrawType != EnumDrawType.Empty)
        {
          tess.TesselateBlock(block, out MeshData own);
          pool.AddBlockMesh(own);
        }
      }
      catch (Exception e)
      {
        blockMeshThrew = e;
      }
    }
    var footprint = new List<BlockPos> { new(0, 0, 0, pos.dimension) };
    footprint.AddRange(
      BlockLaws
        .FillersOf(world, pos)
        .Select(c => new BlockPos(
          c.X - pos.X,
          c.Y - pos.Y,
          c.Z - pos.Z,
          pos.dimension
        ))
    );
    if (
      server is BlockEntityMultiblockStructure
      {
        StructureComplete: true
      } anchor
    )
      footprint.AddRange(
        anchor
          .LayoutCells.Select(c => c.At)
          .Where(c => !c.Equals(pos))
          .Select(c => new BlockPos(
            c.X - pos.X,
            c.Y - pos.Y,
            c.Z - pos.Z,
            pos.dimension
          ))
      );
    return new DrawnCell(
      pos.Copy(),
      block,
      skips,
      threw,
      blockMeshThrew,
      [.. pool.Meshes],
      [.. tess.Textures.Keys],
      twin != null && AnimationActive(twin),
      world.TakeRedrawMark(pos),
      footprint
    );
  }

  private static void RequireSwitch(TestWorld world)
  {
    if (!world.ClientTesselates)
      throw new InvalidOperationException(
        "DrawnCell reads the game's tesselation; set TestWorld.ClientTesselates first."
      );
  }

  private static bool CallsOnTesselation(EnumDrawType type) =>
    type
      is EnumDrawType.JSON
        or EnumDrawType.JSONAndWater
        or EnumDrawType.JSONAndSnowLayer;

  private static bool AnimationActive(BlockEntity twin) =>
    twin.GetBehavior<BEBehaviorAnimatable>()?.animUtil is { } util
    && (
      util.activeAnimationsByAnimCode.Count > 0
      || (util.animator?.ActiveAnimationCount ?? 0) > 0
    );

  private (int, int, Cuboidd?, int, double) Measure()
  {
    int vertices = 0;
    int indices = 0;
    int outside = 0;
    double overshoot = 0;
    Cuboidd? bounds = null;
    foreach (PooledMesh pooled in _meshes)
    {
      MeshData mesh = pooled.Mesh;
      vertices += mesh.VerticesCount;
      indices += mesh.IndicesCount;
      for (int i = 0; i < mesh.VerticesCount; i++)
      {
        double x = mesh.xyz[i * 3];
        double y = mesh.xyz[i * 3 + 1];
        double z = mesh.xyz[i * 3 + 2];
        if (bounds == null)
          bounds = new Cuboidd(x, y, z, x, y, z);
        else
        {
          bounds.X1 = Math.Min(bounds.X1, x);
          bounds.Y1 = Math.Min(bounds.Y1, y);
          bounds.Z1 = Math.Min(bounds.Z1, z);
          bounds.X2 = Math.Max(bounds.X2, x);
          bounds.Y2 = Math.Max(bounds.Y2, y);
          bounds.Z2 = Math.Max(bounds.Z2, z);
        }
        double away = DistanceToFootprint(x, y, z);
        if (double.IsNaN(away) || away > Tolerance)
        {
          outside++;
          overshoot = double.IsNaN(away)
            ? double.PositiveInfinity
            : Math.Max(overshoot, away);
        }
      }
    }
    return (vertices, indices, bounds, outside, overshoot);
  }

  private double DistanceToFootprint(double x, double y, double z)
  {
    if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
      return double.NaN;
    double best = double.PositiveInfinity;
    foreach (BlockPos cell in _footprint)
    {
      double dx = Math.Max(Math.Max(cell.X - x, x - (cell.X + 1)), 0);
      double dy = Math.Max(Math.Max(cell.Y - y, y - (cell.Y + 1)), 0);
      double dz = Math.Max(Math.Max(cell.Z - z, z - (cell.Z + 1)), 0);
      best = Math.Min(best, Math.Max(dx, Math.Max(dy, dz)));
    }
    return best;
  }
}
#endif
