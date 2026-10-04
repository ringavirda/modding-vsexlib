using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A block entity that leaves the world stops ticking, as the game's
/// <see cref="BlockEntity.OnBlockRemoved"/> unregisters its listeners.</summary>
public class TestWorldRemovalTickTests {
  [Fact]
  public void A_cell_set_to_air_stops_its_entity_ticking() {
    var (world, pos, be) = Ticking(1);

    world.Accessor.SetBlock(0, pos);

    world.FireBlockEntityTicks();
    Assert.Equal(1, be.Ticks);
  }

  [Fact]
  public void A_removed_entity_stops_ticking() {
    var (world, pos, be) = Ticking(2);

    world.Accessor.RemoveBlockEntity(pos);

    world.FireBlockEntityTicks();
    Assert.Equal(1, be.Ticks);
  }

  [Fact]
  public void A_replaced_cell_ticks_its_new_entity_and_not_the_old() {
    var (world, pos, old) = Ticking(3);
    Block other = TestBlocks.Configure(new Block(), "test:other", 30);
    other.EntityClass = "test:other";
    world.Register(other);
    TickBe? fresh = null;
    world.RegisterBlockEntityFactory("test:other", () => fresh = new TickBe());

    world.Accessor.SetBlock(30, pos);

    Assert.NotNull(fresh);
    Assert.NotSame(old, fresh);
    world.FireBlockEntityTicks();
    Assert.Equal(1, old.Ticks);
    Assert.Equal(1, fresh!.Ticks);
  }

  private static (TestWorld, BlockPos, TickBe) Ticking(int id) {
    var world = new TestWorld();
    var pos = new BlockPos(id, 0, 0);
    var be = new TickBe();
    world.Place(
      pos,
      TestBlocks.Configure(new Block(), $"test:ticker{id}", id),
      be
    );
    world.Initialize(be);
    world.FireBlockEntityTicks();
    Assert.Equal(1, be.Ticks);
    return (world, pos, be);
  }

  private sealed class TickBe : BlockEntity {
    public int Ticks;

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      RegisterGameTickListener(_ => Ticks++, 1000);
    }
  }
}
