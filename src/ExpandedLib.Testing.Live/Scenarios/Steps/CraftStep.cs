using System.Collections.Generic;
using System.Linq;
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
/// back and the outputs, and closes. Without <c>at</c> the bill goes into the scenario player's own
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

    var made = new List<ItemStack>();
    string how = "";
    try {
      for (int i = 0; i < times; i++) {
        if (entity != null) {
          player.Survive();
          player.Face(ctx.Setup.Absolute(local!), BlockFacing.SOUTH);
          ctx.Touched.Add(local!.Copy());
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
    int count = made.Where(s => s.Collectible.Code.Equals(wanted))
      .Sum(s => s.StackSize);
    if (count != expected || made.Any(s => !s.Collectible.Code.Equals(wanted)))
      throw new StepFailure(
        $"{station} made {string.Join(", ", made.Select(s => $"{s.StackSize} {s.Collectible.Code}"))}, not {expected} {wanted}."
      );
    Stow(player, made);
    report.Value = $"{expected} x {wanted} at {station}{how}";
    yield break;
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

  private static ItemStack Laid(
    ScenarioContext ctx,
    CraftInput input,
    JObject? with
  ) {
    try {
      return CraftBills.Laid(ctx.World, input, with);
    } catch (System.InvalidOperationException e) {
      throw new StepFailure(e.Message, Suspect.Scenario, e);
    }
  }

  private static IEnumerable<ItemStack> CraftAt(
    ScenarioContext ctx,
    ScenarioPlayer player,
    BlockEntity entity,
    string station,
    CraftOrder order,
    JObject? with
  ) {
    ServerPlayer who = player.Player;
    ItemSlot hand = who.InventoryManager.ActiveHotbarSlot;
    var inventory = (
      entity as IBlockEntityContainer
      ?? throw new StepFailure(
        $"{entity.GetType().Name} has no inventory to click.",
        Suspect.Scenario
      )
    ).Inventory;
    entity.OnReceivedClientPacket(who, 1000, null);
    foreach (CraftInput input in order.Inputs) {
      ItemStack laid = Laid(ctx, input, with);
      hand.Itemstack = laid;
      int moved = WindowStep.PutOnce(who, entity, input.Slot);
      hand.Itemstack = null;
      if (moved < laid.StackSize)
        throw new StepFailure(
          $"slot {input.Slot} of {station} took {moved} of the {laid.StackSize} {laid.Collectible.Code} laid for {input.Name}."
        );
    }
    entity.OnReceivedClientPacket(who, order.PressId!.Value, order.PressData);
    foreach (CraftInput input in order.Inputs.Where(i => i.Ingredient.IsTool)) {
      if (inventory[input.Slot].Empty)
        continue;
      WindowStep.Take(who, entity, input.Slot);
      hand.Itemstack = null;
    }
    var taken = new List<ItemStack>();
    foreach (int slot in order.OutputSlots) {
      if (inventory[slot].Empty)
        continue;
      WindowStep.Take(who, entity, slot);
      if (hand.Itemstack is { } got)
        taken.Add(got.Clone());
      hand.Itemstack = null;
    }
    entity.OnReceivedClientPacket(who, 1001, null);
    if (taken.Count == 0)
      throw new StepFailure(
        $"the press of {order.Product.Collectible.Code} at {station} yielded nothing: the station refused."
      );
    return taken;
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
      slot.Itemstack = Laid(ctx, input, with);
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
      slots[input.Slot].Itemstack = Laid(ctx, input, with);
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
