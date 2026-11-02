#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The test world's <c>CreateExplosion</c>: the engine's rays over a block volume, the
/// blocks' own explosion hooks and the drops they spawn.</summary>
public class TestWorldExplosionTests
{
  private const int Reach = 12;
  private const string Cobble = "test:cobble";
  private const string Lump = "test:lump";
  private static readonly BlockPos Centre = new(32, 32, 32);

  // Seeds 1..40 of blasting.md case (a) gave 144..154 broken (mean 148.4, sd 2.2) against
  // blast.py's 149, and 126..140 lumps (mean 133.6) against its 134.
  private const int SeamBrokenMin = 142;
  private const int SeamBrokenMax = 155;
  private static readonly int[] Seeds = [1, 2, 3, 4, 5];

  private sealed class ProbeBlock : Block
  {
    public Block? SeenAtHook { get; private set; }

    public override double ExplosionDropChance(
      IWorldAccessor world,
      BlockPos pos,
      EnumBlastType blastType
    )
    {
      SeenAtHook = world.BlockAccessor.GetBlock(pos);
      return 1;
    }
  }

  private sealed class Volume
  {
    public TestWorld World { get; } = new();
    private readonly Block _stone;
    private readonly Block _ore;

    public Volume(System.Func<int, int, int, bool> isOre)
    {
      TestModDomain.Register();
      _stone = Define("test:stone", 1, EnumBlockMaterial.Stone, Cobble);
      _ore = Define("test:ore", 2, EnumBlockMaterial.Ore, Lump);
      foreach (var (offset, pos) in Cells())
        World.Accessor.SetBlock(
          isOre(offset.X, offset.Y, offset.Z) ? _ore.BlockId : _stone.BlockId,
          pos
        );
      World.Accessor.SetBlock(0, Centre);
    }

    /// <summary>A 3-thick ore layer through the centre of a stone cube, as blasting.md case (a).</summary>
    public static Volume Seam3() => new((_, y, _) => y is >= -1 and <= 1);

    public static Volume Rock() => new((_, _, _) => false);

    private Block Define(
      string code,
      int id,
      EnumBlockMaterial material,
      string drop
    )
    {
      Block block = TestBlocks.Configure(new Block(), code, id);
      block.BlockMaterial = material;
      block.Drops =
      [
        new BlockDropItemStack(new ItemStack(World.RegisterItem(drop)), 1f),
      ];
      World.Register(block);
      return block;
    }

    public void Blast(EnumBlastType type, int seed)
    {
      World.World.Rand.Returns(new Random(seed));
      World.World.CreateExplosion(Centre, type, 4, 10);
    }

    /// <summary>The cells inside the cube that read as air now, less the bomb's own, split by the
    /// offset from the centre.</summary>
    public int Cleared(System.Func<BlockPos, bool> where) =>
      Cells().Count(c => where(c.Offset) && World.GetBlock(c.Pos).BlockId == 0)
      - (where(new BlockPos(0, 0, 0)) ? 1 : 0);

    public int Dropped(string code) =>
      World
        .Drops.Where(d => d.Collectible.Code.ToString() == code)
        .Sum(d => d.StackSize);

    private static IEnumerable<(BlockPos Offset, BlockPos Pos)> Cells()
    {
      for (int x = -Reach; x <= Reach; x++)
      for (int y = -Reach; y <= Reach; y++)
      for (int z = -Reach; z <= Reach; z++)
        yield return (new BlockPos(x, y, z), Centre.AddCopy(x, y, z));
    }
  }

  private static bool InSeam(BlockPos o) => o.Y is >= -1 and <= 1;

  private static bool InStone(BlockPos o) => !InSeam(o);

  // Fails when a block's blast resistance is not read (the stone around the seam breaks too).
  [Fact]
  public void Ore_blast_in_a_laid_seam_breaks_ore_and_leaves_the_stone()
  {
    var v = Volume.Seam3();

    v.Blast(EnumBlastType.OreBlast, 1);

    Assert.Equal(0, v.Cleared(InStone));
    Assert.InRange(v.Cleared(InSeam), SeamBrokenMin, SeamBrokenMax);
  }

  // Fails when the rays or the resistance table drift from the engine's: the count leaves the band.
  [Fact]
  public void Seam_blast_broken_count_stays_in_the_case_a_band_over_seeds()
  {
    foreach (int seed in Seeds)
    {
      var v = Volume.Seam3();

      v.Blast(EnumBlastType.OreBlast, seed);

      Assert.InRange(v.Cleared(InSeam), SeamBrokenMin, SeamBrokenMax);
    }
  }

  // Fails when the blast type is ignored (a rock blast then breaks no more stone than an ore blast).
  [Fact]
  public void Rock_blast_breaks_the_stone_round_the_centre_and_more_of_it_than_an_ore_blast()
  {
    var rock = Volume.Rock();
    var ore = Volume.Rock();

    rock.Blast(EnumBlastType.RockBlast, 1);
    ore.Blast(EnumBlastType.OreBlast, 1);

    foreach (BlockFacing face in BlockFacing.ALLFACES)
      Assert.Equal(0, rock.World.GetBlock(Centre.AddCopy(face)).BlockId);
    Assert.NotEqual(0, rock.World.GetBlock(Centre.AddCopy(9, 0, 0)).BlockId);
    Assert.True(rock.Cleared(_ => true) > 3 * ore.Cleared(_ => true));
  }

  // Fails when a broken block's own OnBlockExploded does not run (no drops) or runs after the
  // block is already air (the ore's 0.9 drop chance reads as the 0.25 default).
  [Fact]
  public void Ore_blast_drops_lumps_at_the_ore_drop_chance_and_no_cobble()
  {
    foreach (int seed in Seeds)
    {
      var v = Volume.Seam3();

      v.Blast(EnumBlastType.OreBlast, seed);

      int broken = v.Cleared(InSeam);
      Assert.InRange(v.Dropped(Lump), broken * 0.8, broken * 0.98);
      Assert.Equal(0, v.Dropped(Cobble));
    }
  }

  // Fails when the stone's drop chance is not read: a rock blast drops 0.2 of what it breaks.
  [Fact]
  public void Rock_blast_drops_cobble_at_the_rock_blast_chance()
  {
    var v = Volume.Rock();

    v.Blast(EnumBlastType.RockBlast, 1);

    int broken = v.Cleared(_ => true);
    Assert.InRange(v.Dropped(Cobble), broken * 0.1, broken * 0.3);
  }

  // Fails when the block's removal is written before its hook runs (the hook then reads air).
  [Fact]
  public void A_blast_removes_blocks_only_after_their_hooks_ran()
  {
    var v = Volume.Rock();
    ProbeBlock probe = TestBlocks.Configure(new ProbeBlock(), "test:probe", 3);
    probe.BlockMaterial = EnumBlockMaterial.Stone;
    v.World.Register(probe);
    BlockPos at = Centre.AddCopy(1, 0, 0);
    v.World.Accessor.SetBlock(probe.BlockId, at);

    v.Blast(EnumBlastType.RockBlast, 1);

    Assert.Same(probe, probe.SeenAtHook);
    Assert.Equal(0, v.World.GetBlock(at).BlockId);
  }

  // Fails when the world's claim list is left unconfigured: a bomb's permission test then throws on null.
  [Fact]
  public void The_world_holds_no_land_claims()
  {
    Assert.Empty(new TestWorld().Api.WorldManager.LandClaims);
  }
}
#endif
