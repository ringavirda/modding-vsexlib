using System;
using System.Diagnostics;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The fillers a megablock stands, read by principal, and the cost of the laws that read
/// them over a megablock of about 300 fillers.</summary>
public class FillersOfTests {
  private static readonly System.Reflection.Assembly[] Exlib =
  [
    typeof(BlockStructureFiller).Assembly,
  ];

  private static FillerCellSpec[] Cells(int across, int up) =>
    [
      .. from x in Enumerable.Range(0, across)
      from y in Enumerable.Range(0, across)
      from z in Enumerable.Range(0, up)
      where x + y + z > 0
      select new FillerCellSpec(x, y, z),
    ];

  private static ExBlockDef Mega(string code, FillerCellSpec[] cells) =>
    ExBlockDef
      .Create("test", code)
      .Class("ExFilledMegastructure")
      .SideVariant()
      .FillerOffsets(cells);

  private static BlockPos[] ScanOf(TestWorld world, BlockPos principal) =>
    [
      .. world
        .BlockEntities.Where(e =>
          e.Value is BlockEntityStructureFiller { Principal: { } p }
          && p.Equals(principal)
          && world.GetBlock(e.Key) is BlockStructureFiller
        )
        .Select(e => e.Key.Copy()),
    ];

  private static void Place(TestWorld world, string code, BlockPos at) {
    Block block = world.World.GetBlock(new AssetLocation($"test:{code}-n"));
    world.Accessor.SetBlock(block.BlockId, at);
    block.OnBlockPlaced(world.World, at, new ItemStack(block));
  }

  // Fails when the index keys a filler under its own cell, or any principal but the one its entity
  // names, so a structure beside another reads the wrong fillers.
  [Fact]
  public void A_megablock_beside_another_reads_only_its_own_fillers() {
    TestWorld world = BlockLaws.Stand(
      [Mega("small", Cells(2, 2)), Mega("large", Cells(3, 3))],
      Exlib,
      null
    );
    BlockPos small = new(64, 64, 64),
      large = new(70, 64, 64),
      none = new(90, 64, 64);
    Place(world, "small", small);
    Place(world, "large", large);

    Assert.Equal(7, world.FillersOf(small).Length);
    Assert.Equal(26, world.FillersOf(large).Length);
    Assert.Equal(ScanOf(world, small), world.FillersOf(small));
    Assert.Equal(ScanOf(world, large), world.FillersOf(large));
    Assert.Empty(world.FillersOf(none));
    Assert.DoesNotContain(
      world.FillersOf(small),
      c => world.FillersOf(large).Contains(c)
    );
  }

  // Fails when the index keeps a filler after its cell is cleared, or does not take one stored
  // after an earlier read.
  [Fact]
  public void A_megablock_removed_and_placed_again_reads_the_fillers_standing() {
    TestWorld world = BlockLaws.Stand(
      [Mega("small", Cells(2, 2))],
      Exlib,
      null
    );
    BlockPos at = new(64, 64, 64);
    Place(world, "small", at);
    Assert.Equal(7, world.FillersOf(at).Length);

    world.Accessor.SetBlock(0, at);
    Assert.Empty(world.FillersOf(at));

    Place(world, "small", at);
    Assert.Equal(ScanOf(world, at), world.FillersOf(at));
    Assert.Equal(7, world.FillersOf(at).Length);
  }

  // Fails when FillersOf scans every stored entity per call: 200 megablocks of 293 fillers store
  // 58,600 entities, and 1,000 reads cost 0.8 s with the scan.
  [Fact]
  public void Reading_the_fillers_of_one_of_200_megablocks_does_not_scan_the_rest() {
    TestWorld world = BlockLaws.Stand([Mega("big", Cells(7, 6))], Exlib, null);
    for (int i = 0; i < 200; i++)
      Place(
        world,
        "big",
        new BlockPos(64 + 32 * (i % 16), 64, 64 + 32 * (i / 16))
      );
    BlockPos last = new(64 + 32 * (199 % 16), 64, 64 + 32 * (199 / 16));

    var clock = Stopwatch.StartNew();
    int found = 0;
    for (int i = 0; i < 1000; i++)
      found = world.FillersOf(last).Length;
    clock.Stop();

    Assert.Equal(293, found);
    Assert.True(
      clock.Elapsed < TimeSpan.FromSeconds(DirectBoundSeconds),
      $"took {clock.Elapsed.TotalSeconds:F2} s"
    );
  }

  // Fails when FillersOf scans every stored entity per call: the interaction law over sixteen
  // variants of a 79-filler megablock stores about 100,000 entities, and read in the measured time
  // with the scan.
  [Fact]
  public void The_interaction_law_over_sixteen_megablock_variants_stays_fast() {
    TestWorld world = BlockLaws.Stand(
      [
        Mega("big", Cells(4, 5))
          .VariantGroup("kind", "a", "b", "c", "d")
          .EntityClass("test-plain")
          .Behavior("BlockEntityInteract")
          .Construction(c => c.Stage(s => s.Require("test:peg", 1))),
      ],
      Exlib,
      w => w.RegisterClass("test-plain", typeof(PlainBe))
    );

    var clock = Stopwatch.StartNew();
    BlockLaws.Law law = InteractionLaw.Run(world, "test");
    clock.Stop();

    Assert.Empty(law.Findings);
    Assert.True(
      clock.Elapsed < TimeSpan.FromSeconds(LawBoundSeconds),
      $"took {clock.Elapsed.TotalSeconds:F1} s over {law.Cases} cases"
    );
  }

  private const double DirectBoundSeconds = 0.2;
  private const double LawBoundSeconds = 5;

  private sealed class PlainBe : BlockEntity { }
}
