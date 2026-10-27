#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A bill that pays a liquid in a vessel: the litres leave the vessel and the vessel stays.</summary>
public sealed class CraftBillsLiquidTests : IDisposable
{
  private const string Oil = "game:oilportion-olive";
  private const float Asked = 0.5f;

  private readonly PlantedFiles _files = new();
  private readonly TestWorld _world;
  private readonly CraftOrder _order;
  private readonly Item _part;
  private readonly Block _bucket;

  public CraftBillsLiquidTests()
  {
    _files.Write(
      "modinfo.json",
      "{ \"type\": \"content\", \"modid\": \"walk\", \"name\": \"Walk\", \"version\": \"1.0.0\" }"
    );
    _files.Write(
      "assets/walk/itemtypes/part.json",
      "{ \"code\": \"part\", \"creativeinventory\": { \"general\": [\"*\"] } }"
    );
    _files.Write(
      "assets/walk/itemtypes/made.json",
      "{ \"code\": \"made\", \"creativeinventory\": { \"general\": [\"*\"] } }"
    );
    _files.Write("assets/walk/recipes/grid/oiled.json", Recipe(Asked));
    _world = new TestWorld();
    _world.LoadAssets([_files.Root], null, ["game:woodbucket", Oil]);
    _world.LoadRecipes();
    _order = HandGrid.Orders(_world.World).First(o => o.Recipe == "walk:oiled");
    _part = _world.World.GetItem(new AssetLocation("walk:part"));
    _bucket = Loaded(_world);
  }

  internal static Block Loaded(TestWorld world)
  {
    Block bucket = world.World.GetBlock(new AssetLocation("game:woodbucket"));
    bucket.OnLoaded(world.Api);
    return bucket;
  }

  public void Dispose()
  {
    _world.Dispose();
    _files.Dispose();
  }

  internal static string Recipe(float litres) =>
    $$"""
      {
        "name": "walk:oiled",
        "ingredientPattern": "PF",
        "width": 2,
        "height": 1,
        "ingredients": {
          "P": { "type": "item", "code": "walk:part" },
          "F": { "type": "block", "code": "game:woodbucket" }
        },
        "attributes": {
          "liquidContainerProps": {
            "requiresContent": { "type": "item", "code": "{{Oil}}" },
            "requiresLitres": {{litres}}
          }
        },
        "output": { "type": "item", "code": "walk:made" }
      }
      """;

  private ItemStack Vessel(float litres)
  {
    var stack = new ItemStack(_bucket);
    if (litres > 0)
      Assert.Null(HeldContent.Fill(_world.World, stack, Oil, litres));
    return stack;
  }

  private ItemSlot[] Bill(ItemStack vessel) =>
    [new DummySlot(new ItemStack(_part)), new DummySlot(vessel)];

  private float Litres(ItemStack vessel) =>
    ((BlockLiquidContainerBase)vessel.Collectible).GetCurrentLitres(vessel);

  // Fails when Take treats the vessel as plain stack, so the vessel leaves the slot and its litres stay,
  // or when the container's contents are ignored and nothing is drained.
  [Fact]
  public void A_bill_paid_from_a_vessel_of_oil_takes_the_litres_and_keeps_the_vessel()
  {
    ItemStack vessel = Vessel(2f);
    ItemSlot[] slots = Bill(vessel);

    foreach (CraftInput input in _order.Inputs)
    {
      Assert.True(CraftBills.Pays(_order, input, slots[input.Slot].Itemstack));
      CraftBills.Take(_order, input, slots[input.Slot]);
    }

    Assert.Null(slots[0].Itemstack);
    Assert.Same(vessel, slots[1].Itemstack);
    Assert.Equal(2f - Asked, Litres(vessel), 2);
  }

  // Fails when Pays stops asking the vessel for its litres, so a vessel short of them, or empty, pays.
  [Theory]
  [InlineData(0.2f)]
  [InlineData(0f)]
  public void A_vessel_short_of_the_litres_does_not_pay(float held)
  {
    ItemStack vessel = Vessel(held);
    CraftInput input = _order.Inputs.Single(i => i.Name == "F");

    Assert.False(CraftBills.Pays(_order, input, vessel));
  }

  // Fails when Filled leaves the vessel empty or fills it with other than the bill's litres.
  [Fact]
  public void A_vessel_laid_for_a_liquid_bill_is_filled_with_the_litres()
  {
    ItemStack laid = CraftBills.Filled(_world.World, _order, Vessel(0f));

    Assert.Equal(Asked, Litres(laid), 2);
    ItemStack part = CraftBills.Filled(
      _world.World,
      _order,
      new ItemStack(_part)
    );
    Assert.Equal(_part, part.Collectible);
  }

  private CraftSequenceRun Run(ItemStack vessel, out List<ItemSlot> slots)
  {
    var step = new CraftStep(null, null, 0.1f, null, null, null, null, 0);
    slots = [.. Bill(vessel), new DummySlot(), new DummySlot()];
    return new CraftSequenceRun(
      new CraftSequence("walk:oiled", [step], []),
      _order with
      {
        OutputSlots = [2, 3],
      },
      slots,
      new Random(1),
      new CraftSequenceRegistry()
    );
  }

  // Fails when the sequence run's bill check passes over the vessel's litres, so a short vessel crafts.
  [Fact]
  public void A_sequence_waits_when_the_vessel_is_short()
  {
    CraftSequenceRun run = Run(Vessel(0.2f), out List<ItemSlot> slots);

    Assert.Equal(
      CraftSequenceProgress.Idle,
      run.Hold(_world.World, 1f, new DummySlot(), null, false)
    );
    Assert.NotNull(slots[0].Itemstack);
  }

  // Fails when the sequence run takes the vessel whole, or leaves its litres, at the last step.
  [Fact]
  public void A_sequence_crafts_from_the_vessel_and_keeps_it()
  {
    ItemStack vessel = Vessel(2f);
    CraftSequenceRun run = Run(vessel, out List<ItemSlot> slots);

    Assert.Equal(
      CraftSequenceProgress.Crafted,
      run.Hold(_world.World, 1f, new DummySlot(), null, false)
    );

    Assert.Null(slots[0].Itemstack);
    Assert.Same(vessel, slots[1].Itemstack);
    Assert.Equal(2f - Asked, Litres(vessel), 2);
    Assert.Equal("walk:made", slots[2].Itemstack!.Collectible.Code.ToString());
  }
}
#endif
