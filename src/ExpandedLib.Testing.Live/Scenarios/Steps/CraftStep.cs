using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.Server;

namespace ExpandedLib.Testing.Live;

/// <summary><c>craft</c>: makes <c>output</c> the way a player does. At a station it opens the window through
/// <see cref="WindowStep"/>'s slot clicks, lays the order's bill, presses its button, takes the tools
/// back and the outputs, and closes; a recipe with a craft sequence replaces the button with one
/// held click per step, the step's tool in the active hand and its off-hand tool beside it. Without <c>at</c> the bill goes into the scenario player's own
/// crafting grid and the product is taken from its output slot; a player with no crafting grid runs the
/// recipe's matcher on a 3 x 3 of loose slots instead. The product goes to the hotbar.</summary>
internal sealed class CraftStep : ScenarioStep {
  private const string GridInventory = "craftinggrid";

  public override string Kind => "craft";

  public override string What(SetupStep step) =>
    step.Body["output"]?.ToString(Formatting.None) ?? "";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    ScenarioPlayer player =
      ctx.Player
      ?? throw new StepFailure(
        "\"craft\" needs the scenario player.",
        Suspect.Scenario
      );
    var wanted = new AssetLocation((string)o["output"]!);
    string? recipe = (string?)o["recipe"];
    int times = (int?)o["times"] ?? 1;
    var with = o["with"] as JObject;
    BlockEntity? entity = null;
    BlockPos? local = null;
    string station = "the hand grid";
    IReadOnlyList<CraftOrder> orders;
    if (o["at"] != null) {
      local = ctx.Position(step, o["at"], "at");
      entity = SetStep.EntityAt(ctx, local);
      station = $"{ctx.Setup.BlockAt(local).Code}";
      orders = entity is ICraftStation craft
        ? craft.Orders(ctx.World)
        : throw new StepFailure(
          $"{station} is not a craft station.",
          Suspect.Scenario
        );
    } else
      orders = HandGrid.Orders(ctx.World);

    CraftOrder? order = orders.FirstOrDefault(x =>
      CraftBills.Makes(x, wanted, recipe)
    );
    if (order == null)
      throw new StepFailure(
        $"{station} has no order that makes {wanted}"
          + (recipe == null ? "" : $" by {recipe}")
          + $"; made at: {StationsMaking(ctx, wanted, recipe)}.",
        Suspect.Scenario
      );

    DrawBill(ctx, order, with, times);
    var made = new List<ItemStack>();
    string how = "";
    try {
      for (int i = 0; i < times; i++) {
        if (entity != null) {
          player.Survive();
          player.Face(ctx.Setup.Absolute(local!), BlockFacing.SOUTH);
          ctx.Touched.Add(local!.Copy());
          if (
            CraftSequenceRegistry.Shared.Sequence(order.Recipe) is { } sequence
          ) {
            var worked = new List<ItemStack>();
            foreach (
              bool more in WorkSequence(
                ctx,
                player,
                entity,
                local!,
                station,
                order,
                sequence,
                with,
                worked
              )
            )
              yield return more;
            made.AddRange(worked);
          } else
            made.AddRange(CraftAt(ctx, player, entity, station, order, with));
        } else {
          player.Survive();
          how = CraftInHand(ctx, player, order, with, made);
        }
      }
    } finally {
      player.Release();
    }

    int expected = order.Product.StackSize * times;
    Dictionary<string, int> yielded = CraftBills.Yield(
      order,
      CraftSequenceRegistry.Shared.Sequence(order.Recipe),
      times
    );
    if (!CraftBills.Yielded(made, yielded))
      throw new StepFailure(
        $"{station} made {string.Join(", ", made.Select(s => $"{s.StackSize} {s.Collectible.Code}"))}, not {string.Join(", ", yielded.Select(w => $"{w.Value} {w.Key}"))}."
      );
    foreach (ItemStack stack in made)
      PackLedger.Give(ctx, stack);
    Stow(player, made);
    report.Value = $"{expected} x {wanted} at {station}{how}";
    yield break;
  }

  private static void DrawBill(
    ScenarioContext ctx,
    CraftOrder order,
    JObject? with,
    int times
  ) {
    if (ctx.Pack == null)
      return;
    foreach (
      var bill in order
        .Inputs.Where(i => !i.Ingredient.IsTool)
        .Select(i => PackLedger.Laid(ctx, i, with, order))
        .GroupBy(s => s.Collectible.Code.ToString())
    )
      PackLedger.Draw(ctx, bill.Key, bill.Sum(s => s.StackSize) * times);
  }

  private static string StationsMaking(
    ScenarioContext ctx,
    AssetLocation wanted,
    string? recipe
  ) {
    var names = new List<string>();
    if (
      HandGrid.Orders(ctx.World).Any(x => CraftBills.Makes(x, wanted, recipe))
    )
      names.Add("the hand grid");
    foreach (BlockPos cell in ctx.Placed)
      if (
        ctx.Setup.BlockEntityAt(cell) is ICraftStation craft
        && craft.Orders(ctx.World).Any(x => CraftBills.Makes(x, wanted, recipe))
      )
        names.Add($"{ctx.Setup.BlockAt(cell).Code} at {cell}");
    return names.Count == 0 ? "no loaded station" : string.Join(", ", names);
  }

  private static IEnumerable<ItemStack> CraftAt(
    ScenarioContext ctx,
    ScenarioPlayer player,
    BlockEntity entity,
    string station,
    CraftOrder order,
    JObject? with
  ) {
    Lay(ctx, player.Player, entity, station, order, with);
    var before = CraftBills.Carried(Carried(player.Player));
    entity.OnReceivedClientPacket(
      player.Player,
      order.PressId!.Value,
      order.PressData
    );
    return Collect(player.Player, entity, station, order, before);
  }

  private static void Lay(
    ScenarioContext ctx,
    ServerPlayer who,
    BlockEntity entity,
    string station,
    CraftOrder order,
    JObject? with
  ) {
    ItemSlot hand = who.InventoryManager.ActiveHotbarSlot;
    entity.OnReceivedClientPacket(who, 1000, null);
    foreach (CraftInput input in order.Inputs) {
      ItemStack laid = PackLedger.Laid(ctx, input, with, order);
      hand.Itemstack = laid;
      int moved = WindowStep.PutOnce(who, entity, input.Slot);
      hand.Itemstack = null;
      if (moved < laid.StackSize)
        throw new StepFailure(
          $"slot {input.Slot} of {station} took {moved} of the {laid.StackSize} {laid.Collectible.Code} laid for {input.Name}."
        );
    }
  }

  private static IEnumerable<ItemSlot> Carried(ServerPlayer who) {
    IEnumerable<ItemSlot> slots = who.InventoryManager.GetHotbarInventory();
    if (
      who.InventoryManager.GetOwnInventory(
        GlobalConstants.backpackInvClassName
      ) is { } backpack
    )
      slots = slots.Concat(backpack);
    return slots;
  }

  /// <summary>Runs <paramref name="take"/>, which leaves what it took in <paramref name="hand"/>, with the
  /// hand empty, then empties the hand and puts back the stack it held before.</summary>
  /// <returns>The stack <paramref name="take"/> left in the hand, or null.</returns>
  internal static ItemStack? TakeAside(ItemSlot hand, Action take) {
    ItemStack? held = hand.Itemstack;
    hand.Itemstack = null;
    take();
    ItemStack? got = hand.Itemstack;
    hand.Itemstack = held;
    return got;
  }

  private static List<ItemStack> Collect(
    ServerPlayer who,
    BlockEntity entity,
    string station,
    CraftOrder order,
    Dictionary<ItemSlot, (CollectibleObject? Collectible, int Size)> before
  ) {
    ItemSlot hand = who.InventoryManager.ActiveHotbarSlot;
    var inventory = (
      entity as IBlockEntityContainer
      ?? throw new StepFailure(
        $"{entity.GetType().Name} has no inventory to click.",
        Suspect.Scenario
      )
    ).Inventory;
    foreach (CraftInput input in order.Inputs.Where(i => i.Ingredient.IsTool)) {
      if (inventory[input.Slot].Empty)
        continue;
      TakeAside(hand, () => WindowStep.Take(who, entity, input.Slot));
    }
    List<ItemStack> taken = CraftBills.HandedOver(before, Carried(who));
    foreach (int slot in order.OutputSlots) {
      if (inventory[slot].Empty)
        continue;
      if (TakeAside(hand, () => WindowStep.Take(who, entity, slot)) is { } got)
        taken.Add(got.Clone());
    }
    entity.OnReceivedClientPacket(who, 1001, null);
    if (taken.Count == 0)
      throw new StepFailure(
        $"the press of {order.Product.Collectible.Code} at {station} yielded nothing: the station refused."
      );
    return taken;
  }

  /// <summary>Lays the bill at the station, holds each step of <paramref name="sequence"/> on its block
  /// with the step's tool in the active hand and its off-hand tool, the block stepped on each runner
  /// tick for the step's seconds, then collects the outputs into <paramref name="taken"/>.</summary>
  private static IEnumerable<bool> WorkSequence(
    ScenarioContext ctx,
    ScenarioPlayer player,
    BlockEntity entity,
    BlockPos local,
    string station,
    CraftOrder order,
    CraftSequence sequence,
    JObject? with,
    List<ItemStack> taken
  ) {
    ServerPlayer who = player.Player;
    Lay(ctx, who, entity, station, order, with);
    BlockPos at = ctx.Setup.Absolute(local);
    BlockSelection selection = InteractStep.SelectionFor(
      ctx.World.BlockAccessor,
      at,
      null,
      BlockFacing.SOUTH
    );
    ItemSlot hand = who.InventoryManager.ActiveHotbarSlot;
    var before = CraftBills.Carried(Carried(who));
    for (int n = 0; n < sequence.Steps.Length; n++) {
      Catalogues.CraftStep step = sequence.Steps[n];
      string label = $"step {n + 1} of {sequence.Recipe}";
      (ItemStack? tool, ItemStack? offhand) = PackLedger.Tools(
        ctx,
        label,
        step
      );
      double seconds = CraftBills.Duration(step, tool) + 0.1;
      bool answered = player.UseStart(at, selection, tool, [], offhand);
      try {
        if (!answered)
          throw new StepFailure(
            $"{station} does not take the hold of {label}."
          );
        double started = ctx.Now;
        bool stepped = true;
        while (InteractStep.Goes(stepped, ctx.Now - started, seconds)) {
          yield return true;
          stepped = player.UseStep(selection, ctx.Now - started);
        }
        player.UseStop(selection, ctx.Now - started);
      } finally {
        PackLedger.GiveTools(ctx, hand.Itemstack, player.Offhand);
        hand.Itemstack = null;
        player.HoldOffhand(null);
      }
    }
    taken.AddRange(Collect(who, entity, station, order, before));
  }

  private static string CraftInHand(
    ScenarioContext ctx,
    ScenarioPlayer player,
    CraftOrder order,
    JObject? with,
    List<ItemStack> made
  ) {
    ServerPlayer who = player.Player;
    if (
      who.InventoryManager.GetOwnInventory(GridInventory)
      is not InventoryBase grid
    )
      return CraftByMatcher(ctx, who, order, with, made);
    for (int i = 0; i < HandGrid.Size * HandGrid.Size; i++)
      grid[i].Itemstack = null;
    foreach (CraftInput input in order.Inputs) {
      ItemSlot slot = grid[input.Slot];
      slot.Itemstack = PackLedger.Laid(ctx, input, with, order);
      grid.DidModifyItemSlot(slot);
    }
    int output = HandGrid.Size * HandGrid.Size;
    if (grid[output].Empty)
      throw new StepFailure(
        $"the crafting grid finds no recipe for the bill of {order.Recipe}."
      );
    ItemSlot cursor = who.InventoryManager.MouseItemSlot;
    var op = new ItemStackMoveOperation(
      ctx.World,
      EnumMouseButton.Left,
      0,
      EnumMergePriority.DirectMerge
    ) {
      ActingPlayer = who,
    };
    grid.ActivateSlot(output, cursor, ref op);
    if (cursor.Itemstack is { } got)
      made.Add(got.Clone());
    cursor.Itemstack = null;
    foreach (CraftInput input in order.Inputs.Where(i => i.Ingredient.IsTool)) {
      ItemSlot slot = grid[input.Slot];
      slot.Itemstack = null;
      grid.DidModifyItemSlot(slot);
    }
    return " by the crafting grid";
  }

  private static string CraftByMatcher(
    ScenarioContext ctx,
    ServerPlayer who,
    CraftOrder order,
    JObject? with,
    List<ItemStack> made
  ) {
    GridRecipe recipe = order.Grid!;
    ItemSlot[] slots =
    [
      .. Enumerable
        .Range(0, HandGrid.Size * HandGrid.Size)
        .Select(_ => (ItemSlot)new DummySlot()),
    ];
    foreach (CraftInput input in order.Inputs)
      slots[input.Slot].Itemstack = PackLedger.Laid(ctx, input, with, order);
    if (!recipe.Matches(who, ctx.World, slots, HandGrid.Size))
      throw new StepFailure(
        $"the hand grid's matcher refuses the bill of {order.Recipe}."
      );
    var output = new DummySlot();
    recipe.GenerateOutputStack(slots, output);
    recipe.ConsumeInput(who, slots, HandGrid.Size);
    made.Add(output.Itemstack);
    return " by the recipe matcher, the player has no crafting grid";
  }

  private static void Stow(ScenarioPlayer player, IEnumerable<ItemStack> made) {
    ItemSlot active = player.Player.InventoryManager.ActiveHotbarSlot;
    List<ItemSlot> slots =
    [
      .. player
        .Player.InventoryManager.GetHotbarInventory()
        .Where(s => s != active),
    ];
    foreach (ItemStack stack in made) {
      ItemSlot? held = slots.FirstOrDefault(s =>
        !s.Empty
        && s.Itemstack.Equals(
          player.Player.Entity.World,
          stack,
          GlobalConstants.IgnoredStackAttributes
        )
      );
      if (held != null)
        held.Itemstack.StackSize += stack.StackSize;
      else
        slots.First(s => s.Empty).Itemstack = stack;
    }
  }
}
