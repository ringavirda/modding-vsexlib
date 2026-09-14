using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Testing;
using Grains;
using HandMill.BlockEntities;
using HandMill.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace HandMill.Tests;

/// <summary>
/// The mill core: it only grinds while its structure is complete and its run turns fast enough, it
/// takes grain and sacks, hands back flour, and loses readiness the moment a wall cell is broken.
/// </summary>
public class MillCoreTests {
  private sealed record Rig(
    TestWorld World,
    BlockEntityMillCore Mill,
    StructureRig Structure,
    BlockPos ShaftPos,
    BlockPos CrankPos
  );

  private static Rig Build() {
    var world = new TestWorld().RegisterNetwork(
      "mpenergy",
      sys => new MpEnergyNetwork(sys)
    );
    var mill = new BlockEntityMillCore();
    Block millBlock = TestBlocks.Configure(
      new BlockMillCore(),
      "handmill:millcore-n",
      1,
      ("side", "n")
    );
    world.Place(new BlockPos(10, 10, 10), millBlock, mill);

    ExBlockDef def = BlockMillCore.Definitions("handmill").Single();
    StructureRig structure = StructureRig.Around(world, mill, def, angle: 0);

    BlockPos shaftPos = structure.Cell(0, 0, -1);
    BlockPos crankPos = structure.Cell(0, 0, -2);

    return new Rig(world, mill, structure, shaftPos, crankPos);
  }

  private static void Occupy(Rig rig) {
    // OnLoaded resolves Type/Orientation off the variant map (TestBlocks.Configure only primes the
    // map itself); the real placement pipeline runs it at chunk load, so a fixture must too, or the
    // shaft and the crank answer no connector at all and the rig never completes.
    //
    // The crank goes down first with nothing yet registered beside it, so it
    // starts its own isolated network. The shaft, added next, scans outward
    // and finds the crank's already-registered network to its north and
    // joins it; only then is the mill (added last, inside Complete())
    // added to find the shaft's network to ITS north.
    Block crankBlock = TestBlocks.Configure(
      new BlockCrank(),
      "handmill:drive-crank-s",
      3,
      ("orientation", "s")
    );
    crankBlock.OnLoaded(rig.World.Api);
    var crankBe = new BlockEntityCrank();
    rig.World.Place(rig.CrankPos, crankBlock, crankBe);
    rig.World.Initialize(crankBe);

    Block shaftBlock = TestBlocks.Configure(
      new BlockShaft(),
      "handmill:drive-shaft-ns",
      2,
      ("orientation", "ns")
    );
    shaftBlock.OnLoaded(rig.World.Api);
    var shaftBe = new BlockEntityShaft();
    rig.Structure.Occupy(rig.ShaftPos, shaftBlock, shaftBe);
    rig.World.Initialize(shaftBe);
  }

  private static BlockEntityCrank Crank(Rig rig) =>
    (BlockEntityCrank)rig.World.GetBlockEntity(rig.CrankPos)!;

  private static void Step(TestWorld world, int seconds) {
    for (int i = 0; i < seconds; i++) {
      world.FireBlockEntityTicks();
      world.Tick(1);
    }
  }

  private static void SeedCatalogue() =>
    GrainCatalogue.Set([
      new GrainDef
      {
        Code = "spelt",
        Grain = "game:grain-spelt",
        Flour = "game:flour-spelt",
        Seconds = 6,
      },
    ]);

  [Fact]
  public void An_unraised_rig_never_completes() {
    Rig rig = Build();
    rig.World.Initialize(rig.Mill);

    rig.World.AdvanceBlockEntityTime(3000);

    Assert.False(rig.Mill.StructureComplete);
  }

  [Fact]
  public void Complete_wound_and_loaded_grinds_one_grain_to_flour() {
    Rig rig = Build();
    Occupy(rig);
    rig.Structure.Complete();
    Crank(rig).Wind(1000);
    SeedCatalogue();
    rig.World.RegisterItem("game:grain-spelt");
    rig.World.RegisterItem("game:flour-spelt");

    var grainSlot = new DummySlot(
      new ItemStack(
        rig.World.World.GetItem(new AssetLocation("game:grain-spelt")),
        1
      )
    );
    Assert.True(rig.Mill.TryLoad(grainSlot));

    // The toy torque/inertia ratio here makes the run's speed bang-bang between 0 and MaxSpeed
    // every one-second tick rather than settle, so grinding only accumulates on every other tick;
    // 20 seconds comfortably clears the 6 the catalogue entry demands either way.
    Step(rig.World, 20);

    var tree = new TreeAttribute();
    rig.Mill.ToTreeAttributes(tree);
    Assert.Equal(0, tree.GetInt("grain"));
    Assert.Equal(1, tree.GetInt("flour"));
  }

  [Fact]
  public void A_sack_loads_SackSize_grain() {
    Rig rig = Build();
    Occupy(rig);
    rig.Structure.Complete();
    SeedCatalogue();
    rig.World.RegisterItem("game:grain-spelt");
    Item sack = rig.World.RegisterItem("grains:sack-spelt");

    var sackSlot = new DummySlot(new ItemStack(sack, 1));
    Assert.True(rig.Mill.TryLoad(sackSlot));

    var tree = new TreeAttribute();
    rig.Mill.ToTreeAttributes(tree);
    Assert.Equal(GrainsValues.SackSize, tree.GetInt("grain"));
  }

  [Fact]
  public void An_unwound_crank_never_reaches_grinding_speed() {
    Rig rig = Build();
    Occupy(rig);
    rig.Structure.Complete();
    SeedCatalogue();
    rig.World.RegisterItem("game:grain-spelt");
    rig.World.RegisterItem("game:flour-spelt");

    var grainSlot = new DummySlot(
      new ItemStack(
        rig.World.World.GetItem(new AssetLocation("game:grain-spelt")),
        1
      )
    );
    Assert.True(rig.Mill.TryLoad(grainSlot));

    // No Wind() call: the run never reaches HandMillValues.MinGrindSpeed.
    Step(rig.World, 8);

    var tree = new TreeAttribute();
    rig.Mill.ToTreeAttributes(tree);
    Assert.Equal(1, tree.GetInt("grain"));
    Assert.Equal(0, tree.GetInt("flour"));
  }

  [Fact]
  public void Breaking_a_wall_cell_drops_completion_on_the_next_monitor_tick() {
    Rig rig = Build();
    Occupy(rig);
    rig.Structure.Complete();

    BlockPos wall = rig.Structure.Cell(-1, 0, -1);
    rig.World.Place(wall, rig.World.Air);
    rig.World.AdvanceBlockEntityTime(3000);

    Assert.False(rig.Mill.StructureComplete);
  }

  [Fact]
  public void Grain_flour_and_progress_round_trip_through_the_tree() {
    Rig rig = Build();
    Occupy(rig);
    rig.Structure.Complete();
    Crank(rig).Wind(1000);
    SeedCatalogue();
    rig.World.RegisterItem("game:grain-spelt");
    rig.World.RegisterItem("game:flour-spelt");

    var grainSlot = new DummySlot(
      new ItemStack(
        rig.World.World.GetItem(new AssetLocation("game:grain-spelt")),
        2
      )
    );
    Assert.True(rig.Mill.TryLoad(grainSlot));
    Step(rig.World, 3);

    var tree = new TreeAttribute();
    rig.Mill.ToTreeAttributes(tree);

    var restored = new BlockEntityMillCore {
      Pos = rig.Mill.Pos,
      Block = rig.Mill.Block,
    };
    restored.FromTreeAttributes(tree, rig.World.World);

    var restoredTree = new TreeAttribute();
    restored.ToTreeAttributes(restoredTree);
    // 3 seconds of grinding is short of the catalogue's 6, so the first
    // grain is still loaded and in progress; both must show up nonzero on
    // the restored copy, not just match the original.
    Assert.Equal(1, restoredTree.GetInt("grain"));
    Assert.True(restoredTree.GetFloat("progress") > 0f);
    Assert.Equal(0, restoredTree.GetInt("flour"));

    // Load the second grain and grind it through to pin a nonzero flour
    // value across the same round trip.
    Assert.True(rig.Mill.TryLoad(grainSlot));
    Step(rig.World, 40);

    var flourTree = new TreeAttribute();
    rig.Mill.ToTreeAttributes(flourTree);

    var restoredFlour = new BlockEntityMillCore {
      Pos = rig.Mill.Pos,
      Block = rig.Mill.Block,
    };
    restoredFlour.FromTreeAttributes(flourTree, rig.World.World);

    var restoredFlourTree = new TreeAttribute();
    restoredFlour.ToTreeAttributes(restoredFlourTree);
    Assert.True(restoredFlourTree.GetInt("flour") > 0);
  }
}
