#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private ItemSlot[] _handGrid = [];

  /// <summary>The slots of the hand grid the last <c>craft</c> step without an <c>at</c> laid and
  /// consumed; empty before one.</summary>
  internal IReadOnlyList<ItemSlot> HandGridSlots => _handGrid;

  private void CraftStep(SetupStep step)
  {
    var wanted = new AssetLocation((string)step.Body["output"]!);
    string? recipe = (string?)step.Body["recipe"];
    int times = (int?)step.Body["times"] ?? 1;
    var with = step.Body["with"] as JObject;
    BlockEntity? entity = null;
    string station = "the hand grid";
    IReadOnlyList<CraftOrder> orders;
    if (step.Body["at"] != null)
    {
      entity = EntityAt(step, out BlockPos at);
      station = $"{World.GetBlock(at).Code} at {Describe(at)}";
      orders = entity is ICraftStation craft
        ? craft.Orders(World.World)
        : throw Fail(step.Path, $"{station} is not a craft station.");
    }
    else
      orders = HandGrid.Orders(World.World);

    CraftOrder[] makes =
    [
      .. orders.Where(o => CraftBills.Makes(o, wanted, recipe)),
    ];
    if (makes.Length == 0)
      throw Fail(
        step.Path,
        $"{station} has no order that makes {wanted}"
          + (recipe == null ? "" : $" by {recipe}")
          + $"; made at: {StationsMaking(wanted, recipe)}."
      );
    CraftOrder order = makes[0];

    var made = new List<ItemStack>();
    for (int i = 0; i < times; i++)
      made.AddRange(
        entity == null
          ? CraftInHand(step.Path, order, with)
          : CraftAt(step.Path, entity, station, order, with)
      );
    Verify(step.Path, station, made, order, times);
    Stow(made);
  }

  /// <summary>Crafts <paramref name="order"/> once at <paramref name="entity"/>, or in the hand
  /// grid when it is null, and checks what came out against the order's product.</summary>
  /// <param name="where">What a failure message names.</param>
  /// <param name="entity">The placed station, or null for the hand grid.</param>
  /// <param name="order">An order of the hand grid or of <paramref name="entity"/>.</param>
  /// <param name="with">The collectible to lay for each input, by input name; null for the
  /// ingredient's first loaded variant.</param>
  /// <returns>The stacks the craft made.</returns>
  /// <exception cref="InvalidOperationException">The matcher refuses the bill, the station takes
  /// less than it is laid or yields nothing, or the product is not the order's.</exception>
  internal IReadOnlyList<ItemStack> CraftOnce(
    string where,
    BlockEntity? entity,
    CraftOrder order,
    JObject? with
  )
  {
    string station = entity == null ? "the hand grid" : $"{entity.Block.Code}";
    List<ItemStack> made =
    [
      .. entity == null
        ? CraftInHand(where, order, with)
        : CraftAt(where, entity, station, order, with),
    ];
    Verify(where, station, made, order, 1);
    return made;
  }

  private void Verify(
    string where,
    string station,
    List<ItemStack> made,
    CraftOrder order,
    int times
  )
  {
    AssetLocation wanted = order.Product.Collectible.Code;
    int expected = order.Product.StackSize * times;
    int count = made.Where(s => s.Collectible.Code.Equals(wanted))
      .Sum(s => s.StackSize);
    if (count != expected || made.Any(s => !s.Collectible.Code.Equals(wanted)))
      throw Fail(
        where,
        $"{station} made {string.Join(", ", made.Select(s => $"{s.StackSize} {s.Collectible.Code}"))}, not {expected} {wanted}."
      );
  }

  private string StationsMaking(AssetLocation wanted, string? recipe)
  {
    var names = new List<string>();
    if (
      HandGrid.Orders(World.World).Any(o => CraftBills.Makes(o, wanted, recipe))
    )
      names.Add("the hand grid");
    foreach ((string id, SetupPlacement placed) in _placed)
      if (
        World.GetBlockEntity(placed.Cell) is ICraftStation craft
        && craft
          .Orders(World.World)
          .Any(o => CraftBills.Makes(o, wanted, recipe))
      )
        names.Add($"{id} ({World.GetBlock(placed.Cell).Code})");
    return names.Count == 0 ? "no loaded station" : string.Join(", ", names);
  }

  private IEnumerable<ItemStack> CraftInHand(
    string where,
    CraftOrder order,
    JObject? with
  )
  {
    GridRecipe recipe = order.Grid!;
    ItemSlot[] grid =
    [
      .. Enumerable
        .Range(0, HandGrid.Size * HandGrid.Size)
        .Select(_ => (ItemSlot)new DummySlot()),
    ];
    _handGrid = grid;
    foreach (CraftInput input in order.Inputs)
      grid[input.Slot].Itemstack = Laid(input, with, where);
    if (!recipe.Matches(Player.Player, World.World, grid, HandGrid.Size))
      throw Fail(
        where,
        $"the hand grid's matcher refuses the bill of {order.Recipe}."
      );
    var output = new DummySlot();
    recipe.GenerateOutputStack(grid, output);
    recipe.ConsumeInput(Player.Player, grid, HandGrid.Size);
    foreach (CraftInput input in order.Inputs.Where(i => i.Ingredient.IsTool))
      grid[input.Slot].Itemstack = null;
    return [output.Itemstack];
  }

  private IEnumerable<ItemStack> CraftAt(
    string where,
    BlockEntity entity,
    string station,
    CraftOrder order,
    JObject? with
  )
  {
    TestWindow window = Player.Window(entity);
    void Verb(Action verb)
    {
      try
      {
        verb();
      }
      catch (InvalidOperationException e)
      {
        throw Fail(where, e.Message);
      }
    }

    Verb(window.Open);
    foreach (CraftInput input in order.Inputs)
    {
      ItemStack laid = Laid(input, with, where);
      Player.Hold(laid);
      int left = 0;
      Verb(() => left = window.Put(input.Slot));
      if (left < laid.StackSize)
        throw Fail(
          where,
          $"slot {input.Slot} of {station} took {left} of the {laid.StackSize} {laid.Collectible.Code} laid for {input.Name}."
        );
    }
    Verb(() => window.Press(order.PressId!.Value, order.PressData));
    foreach (CraftInput input in order.Inputs.Where(i => i.Ingredient.IsTool))
    {
      Verb(() => window.Take(input.Slot));
      Player.Hold(null);
    }
    var taken = new List<ItemStack>();
    foreach (int slot in order.OutputSlots)
    {
      Verb(() => window.Take(slot));
      if (Player.ActiveSlot.Itemstack is { } got)
        taken.Add(got.Clone());
      Player.Hold(null);
    }
    Verb(window.Close);
    if (taken.Count == 0)
      throw Fail(
        where,
        $"the press of {order.Product.Collectible.Code} at {station} yielded nothing: the station refused."
      );
    return taken;
  }

  private ItemStack Laid(CraftInput input, JObject? with, string where)
  {
    try
    {
      return CraftBills.Laid(World.World, input, with);
    }
    catch (InvalidOperationException e)
    {
      throw Fail(where, e.Message);
    }
  }

  private void Stow(IEnumerable<ItemStack> made)
  {
    Player.Hold(null);
    foreach (ItemStack stack in made)
    {
      ItemSlot? held = Player.Hotbar.FirstOrDefault(s =>
        !s.Empty
        && s.Itemstack.Equals(
          World.World,
          stack,
          GlobalConstants.IgnoredStackAttributes
        )
      );
      if (held != null)
        held.Itemstack.StackSize += stack.StackSize;
      else
        Player.Hotbar.First(s => s.Empty).Itemstack = stack;
    }
  }
}
#endif
