#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The run a station drives for a craft sequence: step order, the hands each step needs,
/// tool wear, the vice, and the bill and outputs at the last step.</summary>
public sealed class CraftSequenceRunTests
{
  private const int BillSlot = 0;

  private sealed class Bench : IDisposable
  {
    private readonly TestWorld _world = new();
    private int _nextId = 700;

    public readonly Item Blank;
    public readonly Item Part;
    public readonly Item Bit;
    public readonly ItemSlot Active = new DummySlot();
    public readonly ItemSlot Offhand = new DummySlot();
    public readonly List<ItemSlot> Slots =
    [
      new DummySlot(),
      new DummySlot(),
      new DummySlot(),
    ];
    public bool ViceOpen;
    public CraftSequenceRun Run;

    public Bench(
      CraftStep[] steps,
      int seed = 1,
      CraftByproduct[]? byproducts = null
    )
    {
      Blank = _world.RegisterItem("test:blank");
      Part = _world.RegisterItem("test:part");
      Bit = _world.RegisterItem("test:bit");
      var order = new CraftOrder(
        "test:recipes/grid/part",
        [
          new CraftInput(
            BillSlot,
            "blank",
            new CraftingRecipeIngredient
            {
              Type = EnumItemClass.Item,
              MatchingType = EnumRecipeMatchType.Wildcard,
              Code = Blank.Code,
              Quantity = 1,
            }
          ),
        ],
        null,
        null,
        [1, 2],
        new ItemStack(Part, 1)
      );
      Run = new CraftSequenceRun(
        new CraftSequence("test:recipes/grid/part", steps, byproducts ?? []),
        order,
        Slots,
        new Random(seed)
      );
      Slots[BillSlot].Itemstack = new ItemStack(Blank, 1);
    }

    public Item Tool(string code, int durability = 120, string? attributes = null)
    {
      var item = new Item
      {
        Code = new AssetLocation(code),
        ItemId = _nextId++,
        Durability = durability,
      };
      if (attributes != null)
        item.Attributes = new JsonObject(JToken.Parse(attributes));
      _world.Register(item);
      return item;
    }

    public CraftSequenceProgress Hold(float dt = 1f) =>
      Run.Hold(_world.World, dt, Active, Offhand, ViceOpen);

    public int Durability(ItemSlot slot) =>
      slot.Itemstack!.Collectible.GetRemainingDurability(slot.Itemstack);

    public int Held(int slot) => Slots[slot].Itemstack?.StackSize ?? 0;

    public void Dispose() => _world.Dispose();
  }

  private static CraftStep Step(
    string? tool,
    string? offhand = null,
    string? at = null,
    float seconds = 1f
  ) => new(tool, offhand, seconds, null, null, at);

  // Fails when a hold with the wrong tool advances the step, or the run does not return to its
  // first step once the last step is done.
  [Fact]
  public void A_three_step_sequence_completes_only_in_order()
  {
    using var bench = new Bench(
      [Step("test:chisel"), Step("test:file"), Step("test:saw")]
    );
    Item chisel = bench.Tool("test:chisel");
    Item file = bench.Tool("test:file");
    Item saw = bench.Tool("test:saw");

    bench.Active.Itemstack = new ItemStack(file);
    Assert.Equal(CraftSequenceProgress.Idle, bench.Hold());
    Assert.Equal(0, bench.Run.Step);

    bench.Active.Itemstack = new ItemStack(chisel);
    Assert.Equal(CraftSequenceProgress.StepDone, bench.Hold());
    Assert.Equal(1, bench.Run.Step);

    bench.Active.Itemstack = new ItemStack(saw);
    Assert.Equal(CraftSequenceProgress.Idle, bench.Hold());
    Assert.Equal(1, bench.Run.Step);

    bench.Active.Itemstack = new ItemStack(file);
    Assert.Equal(CraftSequenceProgress.StepDone, bench.Hold());
    bench.Active.Itemstack = new ItemStack(saw);
    Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold());
    Assert.Equal(0, bench.Run.Step);
    Assert.Equal(0, bench.Held(BillSlot));
    Assert.Equal(bench.Part.Code, bench.Slots[1].Itemstack!.Collectible.Code);
  }

  // Fails when a step holds for less than its seconds, or the held time survives a release.
  [Fact]
  public void A_step_needs_its_seconds_held_without_a_break()
  {
    using var bench = new Bench([Step("test:chisel", seconds: 2f)]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:chisel"));

    Assert.Equal(CraftSequenceProgress.Working, bench.Hold(1.5f));
    bench.Active.Itemstack = null;
    Assert.Equal(CraftSequenceProgress.Idle, bench.Hold(1.5f));
    Assert.Equal(0f, bench.Run.Held);
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:chisel"));
    Assert.Equal(CraftSequenceProgress.Working, bench.Hold(1.5f));
    Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold(1f));
  }

  // Fails when the off-hand tool is not checked.
  [Fact]
  public void A_step_with_an_offhand_tool_needs_both()
  {
    using var bench = new Bench([Step("test:chisel", "test:hammer")]);
    Item chisel = bench.Tool("test:chisel");
    Item hammer = bench.Tool("test:hammer");
    bench.Active.Itemstack = new ItemStack(chisel);

    Assert.Equal(CraftSequenceProgress.Idle, bench.Hold());
    bench.Offhand.Itemstack = new ItemStack(chisel);
    Assert.Equal(CraftSequenceProgress.Idle, bench.Hold());
    bench.Offhand.Itemstack = new ItemStack(hammer);
    Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold());
  }

  // Fails when a finished step does not wear the active tool by one, or a step wears the off hand
  // it does not name.
  [Fact]
  public void A_finished_step_costs_the_tool_one_durability()
  {
    using var bench = new Bench(
      [Step("test:file"), Step("test:file", "test:hammer")]
    );
    Item file = bench.Tool("test:file", 120);
    Item hammer = bench.Tool("test:hammer", 50);
    bench.Active.Itemstack = new ItemStack(file);
    bench.Offhand.Itemstack = new ItemStack(hammer);

    bench.Hold();
    Assert.Equal(119, bench.Durability(bench.Active));
    Assert.Equal(50, bench.Durability(bench.Offhand));
    bench.Hold();
    Assert.Equal(118, bench.Durability(bench.Active));
    Assert.Equal(49, bench.Durability(bench.Offhand));
  }

  // Fails when the stepWear attribute is ignored and every step costs one.
  [Fact]
  public void A_tool_with_a_wear_attribute_loses_about_that_share_of_its_steps()
  {
    using var bench = new Bench([Step("test:calipers")], seed: 7);
    Item calipers = bench.Tool("test:calipers", 100000, """{ "stepWear": 0.1 }""");
    bench.Active.Itemstack = new ItemStack(calipers);

    for (int i = 0; i < 1000; i++)
    {
      bench.Slots[BillSlot].Itemstack = new ItemStack(bench.Blank, 1);
      bench.Slots[1].Itemstack = null;
      Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold());
    }

    int lost = 100000 - bench.Durability(bench.Active);
    Assert.InRange(lost, 80, 120);
  }

  // Fails when the tool stays in the slot at zero durability.
  [Fact]
  public void A_tool_worn_to_nothing_breaks()
  {
    using var bench = new Bench([Step("test:file")]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:file", 1));

    Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold());
    Assert.True(bench.Active.Empty);
  }

  // Fails when the run stops after one craft while the bill and the hold go on.
  [Fact]
  public void A_one_step_craft_repeats_while_held()
  {
    using var bench = new Bench([Step("test:hammer")]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:hammer"));
    bench.Slots[BillSlot].Itemstack = new ItemStack(bench.Blank, 3);

    for (int i = 0; i < 3; i++)
      Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold());

    Assert.Equal(3, bench.Held(1));
    Assert.Equal(0, bench.Held(BillSlot));
    Assert.Equal(CraftSequenceProgress.Idle, bench.Hold());
  }

  // Fails when the vice check goes, or a step with no tool takes a held one.
  [Fact]
  public void A_step_at_the_vice_waits_while_the_vice_is_open()
  {
    using var bench = new Bench([Step(null, at: CraftStep.Vice)]);
    bench.ViceOpen = true;

    Assert.Equal(CraftSequenceProgress.WaitingForVice, bench.Hold());
    Assert.Equal(0, bench.Held(1));

    bench.ViceOpen = false;
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:file"));
    Assert.Equal(CraftSequenceProgress.Idle, bench.Hold());
    bench.Active.Itemstack = null;
    Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold());
  }

  // Fails when the last step crafts into full outputs, puts only the product in when the byproduct
  // has no room, or loses the bill while it waits.
  [Fact]
  public void The_last_step_waits_on_a_full_output()
  {
    using var bench = new Bench(
      [Step("test:file")],
      byproducts: [new CraftByproduct("item", "test:bit", 1)]
    );
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:file"));
    bench.Slots[1].Itemstack = new ItemStack(bench.Blank, 64);
    bench.Slots[2].Itemstack = new ItemStack(bench.Part, 64);

    Assert.Equal(CraftSequenceProgress.WaitingForOutput, bench.Hold());
    Assert.Equal(1, bench.Held(BillSlot));
    Assert.Equal(120, bench.Durability(bench.Active));

    bench.Slots[1].Itemstack = null;
    Assert.Equal(CraftSequenceProgress.WaitingForOutput, bench.Hold());
    Assert.Equal(0, bench.Held(1));

    bench.Slots[2].Itemstack = null;
    Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold());
    Assert.Equal(0, bench.Held(BillSlot));
  }

  // Fails when the product or a byproduct is not put in the outputs, or merges into a different item.
  [Fact]
  public void The_last_step_puts_the_product_and_byproducts_in_the_outputs()
  {
    using var bench = new Bench(
      [Step("test:file")],
      byproducts: [new CraftByproduct("item", "test:bit", 2)]
    );
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:file"));
    bench.Slots[1].Itemstack = new ItemStack(bench.Part, 5);

    Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold());

    Assert.Equal(6, bench.Held(1));
    Assert.Equal(bench.Part.Code, bench.Slots[1].Itemstack!.Collectible.Code);
    Assert.Equal(2, bench.Held(2));
    Assert.Equal(bench.Bit.Code, bench.Slots[2].Itemstack!.Collectible.Code);
  }

  // Fails when a missing bill leaves the run mid-sequence.
  [Fact]
  public void A_missing_bill_returns_the_run_to_its_first_step()
  {
    using var bench = new Bench([Step("test:file"), Step("test:file")]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:file"));
    bench.Hold();
    Assert.Equal(1, bench.Run.Step);

    bench.Slots[BillSlot].Itemstack = null;

    Assert.Equal(CraftSequenceProgress.Idle, bench.Hold());
    Assert.Equal(0, bench.Run.Step);
  }
}
#endif
