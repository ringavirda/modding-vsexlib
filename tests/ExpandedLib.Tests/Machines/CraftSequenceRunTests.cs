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

  private sealed class Recorder : ICraftEffects
  {
    private const float ActiveSpan = 0.98f;

    private readonly Dictionary<string, float> _startedAt = [];

    public readonly List<string> Log = [];
    public float Now;
    public int Refused;

    // The engine ignores a start while the same clip is still active.
    public void StartAnimation(string animation)
    {
      if (
        _startedAt.TryGetValue(animation, out float at)
        && Now - at < ActiveSpan
      )
      {
        Refused++;
        return;
      }
      _startedAt[animation] = Now;
      Log.Add("start " + animation);
    }

    public void StopAnimation(string animation) => Log.Add("stop " + animation);

    public void Strike(string sound) => Log.Add("sound " + sound);
  }

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
    public readonly Recorder Fx = new();

    public TestWorld World => _world;
    public CraftSequenceRun Run;

    public Bench(
      CraftStep[] steps,
      int seed = 1,
      CraftByproduct[]? byproducts = null,
      CraftSequenceRegistry? tools = null
    )
    {
      Blank = _world.RegisterItem("test:blank");
      Part = _world.RegisterItem("test:part");
      Bit = _world.RegisterItem("test:bit");
      var order = new CraftOrder(
        "test:part",
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
        new CraftSequence("test:part", steps, byproducts ?? []),
        order,
        Slots,
        new Random(seed),
        tools ?? new CraftSequenceRegistry()
      );
      Slots[BillSlot].Itemstack = new ItemStack(Blank, 1);
    }

    public Item Tool(
      string code,
      int durability = 120,
      string? attributes = null
    )
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

    public CraftSequenceProgress Hold(float dt = 1f)
    {
      Fx.Now += dt;
      return Run.Hold(_world.World, dt, Active, Offhand, ViceOpen, Fx);
    }

    public int Durability(ItemSlot slot) =>
      slot.Itemstack!.Collectible.GetRemainingDurability(slot.Itemstack);

    public int Held(int slot) => Slots[slot].Itemstack?.StackSize ?? 0;

    public void Dispose() => _world.Dispose();
  }

  private static CraftStep Step(
    string? tool,
    string? offhand = null,
    string? at = null,
    float seconds = 1f,
    string? animation = null,
    string? sound = null
  ) => new(tool, offhand, seconds, animation, null, at, sound);

  // Fails when a hold with the wrong tool advances the step, or the run does not return to its
  // first step once the last step is done.
  [Fact]
  public void A_three_step_sequence_completes_only_in_order()
  {
    using var bench = new Bench([
      Step("test:chisel"),
      Step("test:file"),
      Step("test:saw"),
    ]);
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
    using var bench = new Bench([
      Step("test:file"),
      Step("test:file", "test:hammer"),
    ]);
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
    Item calipers = bench.Tool(
      "test:calipers",
      100000,
      """{ "stepWear": 0.1 }"""
    );
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

  // Fails when a byproduct code that resolves to nothing is dropped without an error line.
  [Fact]
  public void A_byproduct_that_resolves_to_nothing_is_logged_and_the_product_still_lands()
  {
    using var bench = new Bench(
      [Step("test:file")],
      byproducts: [new CraftByproduct("item", "test:ghost", 1)]
    );
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:file"));
    bench.World.Log.Expect(EnumLogType.Error, "test:ghost");

    Assert.Equal(CraftSequenceProgress.Crafted, bench.Hold());

    Assert.Equal(1, bench.Held(1));
    Assert.Equal(0, bench.Held(2));
    Assert.Contains(
      bench.World.Log.Errors,
      e => e.Contains("test:ghost") && e.Contains("test:part")
    );
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

  // Fails when the run does not look up the held tool's default, or plays it at the wrong time.
  [Fact]
  public void A_hammer_step_swings_and_sounds_the_hammers_default()
  {
    using var bench = new Bench([Step("game:hammer-*", seconds: 2f)]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("game:hammer-steel"));

    bench.Hold(0.25f);
    Assert.Equal(["start hammerhit"], bench.Fx.Log);

    bench.Hold(0.1f);
    Assert.Equal(
      ["start hammerhit", "sound sounds/effect/anvilhit"],
      bench.Fx.Log
    );
  }

  // Fails when a step's own animation or sound is ignored for the tool's default.
  [Fact]
  public void A_step_override_wins_over_the_tools_default()
  {
    using var bench = new Bench([
      Step(
        "game:hammer-*",
        seconds: 2f,
        animation: "fitting",
        sound: "sounds/effect/rivet"
      ),
    ]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("game:hammer-steel"));

    bench.Hold(0.5f);

    Assert.Equal(["start fitting", "sound sounds/effect/rivet"], bench.Fx.Log);
  }

  // Fails when the run plays one swing for the whole hold, or plays past the step's end.
  [Fact]
  public void Every_stroke_of_a_held_step_swings_once_and_strikes_once()
  {
    using var bench = new Bench([Step("game:hammer-*", seconds: 2f)]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("game:hammer-steel"));

    for (int i = 0; i < 64; i++)
      bench.Hold(1f / 32f);

    Assert.Equal(2, bench.Fx.Log.Count(l => l == "start hammerhit"));
    Assert.Equal(0, bench.Fx.Refused);
    Assert.Equal(
      2,
      bench.Fx.Log.Count(l => l == "sound sounds/effect/anvilhit")
    );
    Assert.Equal("stop hammerhit", bench.Fx.Log[^1]);
  }

  // Fails when a long frame plays the strokes that fall after the step's end.
  [Fact]
  public void A_frame_past_the_end_of_a_step_plays_only_the_strokes_inside_it()
  {
    using var bench = new Bench([
      Step("game:hammer-*", seconds: 2f),
      Step("game:hammer-*"),
    ]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("game:hammer-steel"));

    bench.Hold(3f);

    Assert.Equal(
      2,
      bench.Fx.Log.Count(l => l == "start hammerhit") + bench.Fx.Refused
    );
    Assert.Equal(
      2,
      bench.Fx.Log.Count(l => l == "sound sounds/effect/anvilhit")
    );
  }

  // Fails when a hold with the wrong tool plays, or Release leaves the animation running.
  [Fact]
  public void A_hold_that_does_not_match_plays_nothing_and_release_stops_the_swing()
  {
    using var bench = new Bench([Step("game:hammer-*", seconds: 2f)]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:saw"));
    bench.Hold(1f);
    Assert.Empty(bench.Fx.Log);

    bench.Active.Itemstack = new ItemStack(bench.Tool("game:hammer-iron"));
    bench.Hold(0.1f);
    bench.Run.Release(bench.Fx);

    Assert.Equal(["start hammerhit", "stop hammerhit"], bench.Fx.Log);
  }

  // Fails when a tool with no default and a step with no override still play something.
  [Fact]
  public void A_tool_without_a_default_and_a_step_without_an_override_play_nothing()
  {
    using var bench = new Bench([Step("test:saw")]);
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:saw"));

    bench.Hold(0.5f);

    Assert.Empty(bench.Fx.Log);
  }

  // Fails when a declared tool default is not read for its pattern.
  [Fact]
  public void A_declared_tool_default_plays_for_its_pattern()
  {
    var tools = new CraftSequenceRegistry();
    tools.DefineTool(
      new CraftToolDefault("test:saw", "sawing", "sounds/effect/saw", 1f, 0.5f)
    );
    using var bench = new Bench([Step("test:saw", seconds: 2f)], tools: tools);
    bench.Active.Itemstack = new ItemStack(bench.Tool("test:saw"));

    bench.Hold(0.6f);

    Assert.Equal(["start sawing", "sound sounds/effect/saw"], bench.Fx.Log);
  }
}
#endif
