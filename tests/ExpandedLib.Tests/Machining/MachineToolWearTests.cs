using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The public wear helper: durability, the last point, and the spent drop.</summary>
public sealed class MachineToolWearTests : System.IDisposable {
  private readonly TestWorld _world = new();
  private readonly Item _bit;
  private readonly ItemSlot _slot;

  public MachineToolWearTests() {
    _world.RegisterItem("test:ironbits");
    _bit = _world.RegisterItem("test:bit");
    _bit.Durability = 3;
    _bit.Attributes = new JsonObject(
      JObject.FromObject(MachineTool.Tier(1, "bit", "test:ironbits", 4))
    );
    _slot = new DummyInventory(_world.Api, 1)[0];
    _slot.Itemstack = new ItemStack(_bit);
  }

  public void Dispose() => _world.Dispose();

  private int Left => _bit.GetRemainingDurability(_slot.Itemstack);

  [Fact]
  public void A_job_takes_its_points_and_the_tool_stays() {
    Assert.Null(MachineToolWear.Apply(_slot, 2));

    Assert.Equal(1, Left);
  }

  // Fails when the tool is removed at 1 durability instead of 0.
  [Fact]
  public void The_last_point_leaves_the_tool_fitted_and_the_next_removes_it_with_its_drop() {
    MachineToolWear.Apply(_slot, 2);
    Assert.NotNull(_slot.Itemstack);

    ItemStack? drop = MachineToolWear.Apply(_slot, 1);

    Assert.Null(_slot.Itemstack);
    Assert.Equal("test:ironbits", drop!.Collectible.Code.ToString());
    Assert.Equal(4, drop.StackSize);
  }

  [Fact]
  public void Zero_points_and_an_empty_slot_change_nothing() {
    Assert.Null(MachineToolWear.Apply(_slot, 0));
    Assert.Equal(3, Left);
    Assert.Null(MachineToolWear.Apply(new DummySlot(), 1));
    Assert.Null(MachineToolWear.Apply(null, 1));
  }

  [Fact]
  public void A_tool_that_names_no_spent_drop_leaves_nothing() {
    _bit.Attributes = new JsonObject(
      JObject.FromObject(MachineTool.Tier(1, "bit"))
    );

    Assert.Null(MachineToolWear.Apply(_slot, 3));
    Assert.Null(_slot.Itemstack);
  }
}
