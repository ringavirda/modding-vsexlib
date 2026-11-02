#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Testing;

public sealed partial class TestWorld
{
  private const double RayStep = 0.25;

  /// <summary>The bulk accessor <see cref="World"/> hands out: its <c>SetBlock</c> is held back
  /// until <c>Commit</c>, as the engine's is, so a block reads as it was while an explosion walks
  /// its drops.</summary>
  private IBulkBlockAccessor BuildBulkAccessor()
  {
    var pending = new List<(int Id, BlockPos Pos)>();
    var a = Made<IBulkBlockAccessor>();
    StubBlockReads(a);
    a.When(x => x.SetBlock(Arg.Any<int>(), Arg.Any<BlockPos>()))
      .Do(ci => pending.Add((ci.ArgAt<int>(0), ci.ArgAt<BlockPos>(1).Copy())));
    a.When(x => x.Commit())
      .Do(_ =>
      {
        foreach (var (id, pos) in pending)
          DoSetBlock(id, pos);
        pending.Clear();
      });
    return a;
  }

  /// <summary>The server's <c>CreateExplosion</c> on blocks, drawing from <c>World.Rand</c>.</summary>
  /// <remarks>Entity injury, sounds, particles and the <c>onexplosion</c> event are not modelled;
  /// like the engine, the drop-chance multiplier is not read. The edge-of-world checks are left
  /// out: <c>IsValidPos</c>, negative positions and the liquid notify at a broken cell. A broken
  /// cell is written and its removal hooks run one cell at a time, where the engine's commit
  /// writes every cell first and runs the hooks in a second pass.</remarks>
  private void Explode(
    BlockPos pos,
    EnumBlastType blastType,
    double destructionRadius,
    double injureRadius,
    string? ignitedByPlayerUid
  )
  {
    destructionRadius = GameMath.Clamp(destructionRadius, 1.0, 16.0);
    double reach = Math.Max(1.2 * destructionRadius, injureRadius);
    if (reach > ShapeUtil.MaxShells)
      throw new ArgumentOutOfRangeException(
        nameof(injureRadius),
        "Radius cannot be greater than " + (int)(ShapeUtil.MaxShells / 1.2f)
      );

    FastVec3f[] rays = ShapeUtil.GetCubicShellNormalizedVectors((int)reach);
    double baseReach = 0.8 * destructionRadius;
    double spread = 0.4 * destructionRadius;
    var at = new BlockPos(pos.dimension);
    var broken = new Dictionary<BlockPos, Block>();
    var direction = new Vec3f();

    foreach (FastVec3f ray in rays)
    {
      double strength = baseReach + World.Rand.NextDouble() * spread;
      double injure = injureRadius;
      double limit = Math.Max(strength, injureRadius);
      for (double d = 0.0; d < limit; d += RayStep)
      {
        at.Set(
          pos.X + (int)(ray.X * d + 0.5),
          pos.Y + (int)(ray.Y * d + 0.5),
          pos.Z + (int)(ray.Z * d + 0.5)
        );
        strength -= RayStep;
        injure -= RayStep;
        if (!broken.ContainsKey(at))
        {
          Block block = ReadBlock(at);
          double resistance = block.GetBlastResistance(
            World,
            at,
            direction.Set(ray),
            blastType
          );
          strength -= resistance;
          if (strength > 0.0)
          {
            broken[at.Copy()] = block;
            injure -= resistance;
          }
          if (strength <= 0.0 && resistance > 0.0)
            injure = 0.0;
        }
        if (strength <= 0.0 && injure <= 0.0)
          break;
      }
    }

    foreach (var (cell, block) in broken)
      if (block.BlockMaterial != EnumBlockMaterial.Air)
        block.OnBlockExploded(World, cell, pos, blastType, ignitedByPlayerUid!);
    World.BulkBlockAccessor.Commit();
    foreach (BlockPos cell in broken.Keys)
      NotifyNeighbours(cell);
  }
}
#endif
