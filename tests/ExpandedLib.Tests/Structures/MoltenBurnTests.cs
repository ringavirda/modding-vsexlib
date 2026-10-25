using System.Linq;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The burn of standing on a molten holder (<see cref="MoltenBurn"/>,
/// <see cref="BlockBehaviorMoltenBurn"/> and the patches that route a block's collision and path cost
/// to it) and the scan that gives every holder block the behaviour (<see cref="MoltenHolders"/>).</summary>
[Collection(ExHarmonyCollection.Name)]
public class MoltenBurnTests {
  private const string Iron = "game:ingot-iron";
  private static readonly BlockPos Pos = new(64, 16, 64, 0);

  private sealed class SurfaceEntity : BlockEntity, IMoltenSurface {
    public float? Reading;

    public float? SurfaceTemperature => Reading;
  }

  private static HarmonyFixture Patched() =>
    new("exlibtest.moltenburn", typeof(MoltenBurnPatch).Assembly);

  private sealed class Rig {
    public readonly TestWorld World = new();
    public readonly Block Block;
    public readonly BEBehaviorMoltenCell? Cell;
    public readonly Entity Entity;
    public long Ms;

    public Rig(bool carriesBurn = true, SurfaceEntity? surface = null) {
      World.RegisterItem(Iron, 1500f);
      Block = TestBlocks.Configure(
        new BlockStructureFiller(),
        "exlib:holder",
        70
      );
      World.Register(Block);
      if (carriesBurn) {
        var burn = new BlockBehaviorMoltenBurn(Block);
        Block.BlockBehaviors = [burn];
        Block.CollectibleBehaviors = [burn];
      }
      if (surface != null) {
        World.Place(Pos, Block, surface);
        World.Attach(surface);
      } else {
        var be = new BlockEntityStructureFiller();
        World.Place(Pos, Block, be);
        World.Attach(be);
        Cell = new BEBehaviorMoltenCell(be);
        be.Behaviors.Add(Cell);
        Cell.ConfigureFromFiller(
          null,
          null,
          new JsonObject(JToken.Parse("{}"))
        );
      }
      World.World.ElapsedMilliseconds.Returns(_ => Ms);
      Entity = Substitute.For<Entity>();
      Entity.Alive.Returns(true);
      Entity.World = World.World;
      ReflectionHelpers.SetProperty(
        Entity,
        nameof(Entity.Properties),
        new EntityProperties()
      );
    }

    public void Fill(float temperature) {
      Cell!.PushMetalRaw(50, Iron, temperature, World.World);
      Cell.UpdateThermal(World.World);
    }

    public void Touch(BlockFacing facing) =>
      Block.OnEntityCollide(
        World.World,
        Entity,
        Pos,
        facing,
        new Vec3d(),
        false
      );

    public int Hits() =>
      Entity
        .ReceivedCalls()
        .Count(c => c.GetMethodInfo().Name == nameof(Entity.ReceiveDamage));
  }

  // Fails when the once-a-second time stamp is removed: the second touch 200 ms later also hits.
  [Fact]
  public void Burn_HotTopOncePerSecond() {
    using var fixture = Patched();
    var rig = new Rig();
    rig.Fill(1450f);

    rig.Touch(BlockFacing.UP);
    rig.Ms = 200;
    rig.Touch(BlockFacing.UP);
    Assert.Equal(1, rig.Hits());

    rig.Ms = 1200;
    rig.Touch(BlockFacing.UP);
    Assert.Equal(2, rig.Hits());
    rig.Entity.Received(2)
      .ReceiveDamage(
        Arg.Is<DamageSource>(d =>
          d.Type == EnumDamageType.Fire && d.Source == EnumDamageSource.Block
        ),
        ExlibValues.MoltenBurnDamage
      );
  }

  // Fails when the threshold is read as 0: a 199 C holder then burns.
  [Fact]
  public void Burn_Under200_None() {
    using var fixture = Patched();
    var rig = new Rig(surface: new SurfaceEntity { Reading = 199f });

    rig.Touch(BlockFacing.UP);
    Assert.Equal(0, rig.Hits());

    ((SurfaceEntity)rig.World.GetBlockEntity(Pos)!).Reading = 200f;
    rig.Touch(BlockFacing.UP);
    Assert.Equal(1, rig.Hits());
  }

  // Fails when the face test is removed: a hot holder then burns an entity that touched its side or underside.
  [Fact]
  public void Burn_SideFace_None() {
    using var fixture = Patched();
    var rig = new Rig();
    rig.Fill(1450f);

    foreach (
      BlockFacing face in new[]
      {
        BlockFacing.NORTH,
        BlockFacing.EAST,
        BlockFacing.DOWN,
      }
    )
      rig.Touch(face);

    Assert.Equal(0, rig.Hits());
  }

  // Fails when the rule reads a solidified cell as cold: metal set but still at 900 C then does not burn.
  [Fact]
  public void Burn_SetButHot_Burns() {
    using var fixture = Patched();
    var rig = new Rig();
    rig.Fill(900f);
    Assert.True(rig.Cell!.Solidified);

    rig.Touch(BlockFacing.UP);

    Assert.Equal(1, rig.Hits());
  }

  // Fails when an empty holder burns, or a reading of the cells is used where the entity reports a surface.
  [Fact]
  public void Burn_EmptyHolder_None_And_SurfaceWinsOverCells() {
    using var fixture = Patched();
    var empty = new Rig();
    empty.Touch(BlockFacing.UP);
    Assert.Equal(0, empty.Hits());

    var parked = new Rig(surface: new SurfaceEntity { Reading = 900f });
    parked.Touch(BlockFacing.UP);
    Assert.Equal(1, parked.Hits());
  }

  // Fails when a hot holder's traversal cost is 1 instead of 10000, or a sea creature is steered round it.
  [Fact]
  public void Traversal_HotCell_Avoided() {
    using var fixture = Patched();
    var rig = new Rig();

    Assert.Equal(
      0f,
      rig.Block.GetTraversalCost(Pos, EnumAICreatureType.LandCreature)
    );

    rig.Fill(1450f);
    Assert.Equal(
      10000f,
      rig.Block.GetTraversalCost(Pos, EnumAICreatureType.LandCreature)
    );
    Assert.Equal(
      10000f,
      rig.Block.GetTraversalCost(Pos, EnumAICreatureType.Humanoid)
    );
    Assert.Equal(
      0f,
      rig.Block.GetTraversalCost(Pos, EnumAICreatureType.SeaCreature)
    );
  }

  // Fails when a block without the behaviour is routed to the burn.
  [Fact]
  public void A_block_without_the_behaviour_does_not_burn() {
    using var fixture = Patched();
    var rig = new Rig(carriesBurn: false);
    rig.Fill(1450f);

    rig.Touch(BlockFacing.UP);

    Assert.Equal(0, rig.Hits());
    Assert.Equal(
      0f,
      rig.Block.GetTraversalCost(Pos, EnumAICreatureType.LandCreature)
    );
  }

  #region The scan

  private static Block Holder(
    TestWorld world,
    string code,
    int id,
    string? entityClass,
    params string[] behaviors
  ) {
    Block block = TestBlocks.Configure(new Block(), code, id);
    block.EntityClass = entityClass;
    block.BlockEntityBehaviors =
    [
      .. behaviors.Select(b => new BlockEntityBehaviorType { Name = b }),
    ];
    world.Register(block);
    return block;
  }

  private static TestWorld ScanWorld() {
    var world = new TestWorld();
    world.RegisterClass("SurfaceEntity", typeof(SurfaceEntity));
    world.RegisterClass("Plain", typeof(BlockEntity));
    world.RegisterClass("StructureFiller", typeof(BlockEntityStructureFiller));
    world.RegisterClass("MoltenCell", typeof(BEBehaviorMoltenCell));
    world.RegisterClass("Other", typeof(BEBehaviorTest));
    return world;
  }

  private sealed class BEBehaviorTest(BlockEntity be) : BlockEntityBehavior(be);

  // Fails when the scan skips a kind of holder: its entity class, a declared cell behaviour, or a filler.
  [Fact]
  public void Attach_GivesEveryHolderKindTheBehaviour() {
    var world = ScanWorld();
    Block byClass = Holder(world, "test:byclass", 101, "SurfaceEntity");
    Block byBehavior = Holder(
      world,
      "test:bybehavior",
      102,
      "Plain",
      "MoltenCell"
    );
    Block filler = Holder(world, "test:filler", 103, "StructureFiller");
    Block plain = Holder(world, "test:plain", 104, "Plain", "Other");
    Block none = Holder(world, "test:none", 105, null);

    int attached = MoltenHolders.Attach(world.Api);

    Assert.Equal(3, attached);
    Assert.NotNull(byClass.GetBehavior<BlockBehaviorMoltenBurn>());
    Assert.NotNull(byBehavior.GetBehavior<BlockBehaviorMoltenBurn>());
    Assert.NotNull(filler.GetBehavior<BlockBehaviorMoltenBurn>());
    Assert.Null(plain.GetBehavior<BlockBehaviorMoltenBurn>());
    Assert.Null(none.GetBehavior<BlockBehaviorMoltenBurn>());
    Assert.Contains(byClass.BlockBehaviors, b => b is BlockBehaviorMoltenBurn);
  }

  private sealed class OffsetHost : Block, IFillerHost {
    public JsonObject? FillerOffsets { get; } =
      new(
        JToken.Parse(
          """[{ "x": 1, "y": 0, "z": 0, "behaviors": [{ "code": "MoltenCell" }] }]"""
        )
      );
  }

  // Fails when the scan's footprint clause is false: a block whose only sign is a hosted cell in its offsets.
  [Fact]
  public void Attach_FootprintHostedCellAloneGivesTheBehaviour() {
    var world = ScanWorld();
    Block block = TestBlocks.Configure(new OffsetHost(), "test:offsets", 106);
    world.Register(block);

    int attached = MoltenHolders.Attach(world.Api);

    Assert.Equal(1, attached);
    Assert.NotNull(block.GetBehavior<BlockBehaviorMoltenBurn>());
  }

  // Fails when a second scan stacks another behaviour on a block that has one.
  [Fact]
  public void Attach_IsIdempotent() {
    var world = ScanWorld();
    Block holder = Holder(world, "test:byclass", 101, "SurfaceEntity");

    MoltenHolders.Attach(world.Api);
    int second = MoltenHolders.Attach(world.Api);

    Assert.Equal(0, second);
    Assert.Single(holder.BlockBehaviors.OfType<BlockBehaviorMoltenBurn>());
  }

  #endregion
}
