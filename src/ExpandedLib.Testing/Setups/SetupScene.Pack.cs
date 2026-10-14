#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Blocks;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private SetupPack? _pack;
  private int _spawnsSeen;

  private void StartPack(System.Func<string, SetupFileModel?>? stages)
  {
    if (Model.Pack == null)
      return;
    try
    {
      _pack = SetupPack.Start(Model, stages ?? (_ => null));
    }
    catch (InvalidOperationException e)
    {
      throw Fail("pack", e.Message);
    }
  }

  private void Draw(string where, string code, int count, string? pin = null)
  {
    if (_pack == null || count <= 0)
      return;
    try
    {
      _pack.Draw(code, count, where, pin);
    }
    catch (InvalidOperationException e)
    {
      throw Fail(where, e.Message);
    }
  }

  private void Draw(string where, ItemStack stack) =>
    Draw(where, stack.Collectible.Code.ToString(), stack.StackSize);

  private void Give(ItemStack? stack)
  {
    if (stack != null)
      _pack?.Give(stack.Collectible.Code.ToString(), stack.StackSize);
  }

  private void DrawBlock(string where, string code, string? pin = null)
  {
    if (_pack is not { DrawsBlocks: true } || _pack.TakeStanding(code))
      return;
    Draw(where, code, 1, pin);
  }

  private void DrawStages(
    string where,
    ExRightClickConstruction construction,
    string pin
  )
  {
    if (_pack is not { DrawsBlocks: true })
      return;
    for (
      int i = construction.CurrentCompletedStage + 1;
      i < construction.Stages.Length;
      i++
    )
      foreach (
        ExConstructionIngredient need in construction.Stages[i].RequireStacks
          ?? []
      )
        Draw(
          where,
          Regex.Replace(need.Code.ToString(), @"\{[^}]*\}", "*"),
          need.Quantity,
          pin
        );
  }

  private void GiveSpawns()
  {
    List<(ItemStack Stack, Vec3d At)> spawns = World.Spawns;
    if (_pack == null || _laid.Count == 0)
    {
      _spawnsSeen = spawns.Count;
      return;
    }
    var cells = _laid.Concat(
      _placed.Values.SelectMany(p => p.Cells).Select(c => (c.X, c.Y, c.Z))
    );
    int x0 = cells.Min(c => c.X) - 1,
      x1 = cells.Max(c => c.X) + 1;
    int y0 = cells.Min(c => c.Y) - 1,
      y1 = cells.Max(c => c.Y) + 1;
    int z0 = cells.Min(c => c.Z) - 1,
      z1 = cells.Max(c => c.Z) + 1;
    foreach (var (stack, at) in spawns.Skip(_spawnsSeen))
      if (
        Math.Floor(at.X) >= x0
        && Math.Floor(at.X) <= x1
        && Math.Floor(at.Y) >= y0
        && Math.Floor(at.Y) <= y1
        && Math.Floor(at.Z) >= z0
        && Math.Floor(at.Z) <= z1
      )
        Give(stack);
    _spawnsSeen = spawns.Count;
  }

  private void DrawMetal(string where, IEnumerable<BlockPos> cells)
  {
    if (_pack == null)
      return;
    foreach (var (metal, units) in MetalIn(cells))
      Draw(where, metal, units);
  }

  private IEnumerable<(string Metal, int Units)> MetalIn(
    IEnumerable<BlockPos> cells
  ) =>
    cells
      .Select(View.MoltenCellAt)
      .OfType<MoltenCellReading>()
      .Where(r => r.Amount > 0)
      .GroupBy(r => SetupPack.MetalCode(r.Metal))
      .Select(g => (g.Key, g.Sum(r => r.Amount)));

  private ItemStack? PackedLaid(CraftInput input)
  {
    foreach (string key in _pack!.HeldKeys)
    {
      var code = new AssetLocation(key);
      CollectibleObject? item =
        (CollectibleObject?)World.World.GetItem(code)
        ?? World.World.GetBlock(code);
      if (item == null)
        continue;
      var stack = new ItemStack(item, input.Ingredient.Quantity);
      if (input.Ingredient.SatisfiesAsIngredient(stack))
        return stack;
    }
    return null;
  }

  private void DrawBill(
    string where,
    CraftOrder order,
    JObject? with,
    int times
  )
  {
    if (_pack == null)
      return;
    foreach (
      var bill in order
        .Inputs.Where(i => !i.Ingredient.IsTool)
        .Select(i => Laid(i, with, where))
        .GroupBy(s => s.Collectible.Code.ToString())
    )
      Draw(where, bill.Key, bill.Sum(s => s.StackSize) * times);
  }

  private SetupPackRecord? CheckPack()
  {
    if (_pack == null)
      return null;
    BlockPos[] cells =
    [
      .. _placed
        .Values.SelectMany(p => p.Cells)
        .GroupBy(c => (c.X, c.Y, c.Z))
        .Select(g => g.First()),
    ];
    BlockPos[] molten =
    [
      .. _laid
        .Select(c => new BlockPos(c.X, c.Y, c.Z))
        .Concat(cells)
        .GroupBy(c => (c.X, c.Y, c.Z))
        .Select(g => g.First()),
    ];
    foreach (var (metal, units) in MetalIn(molten))
      _pack.Give(metal, units);
    string[] standing =
    [
      .. cells
        .Select(c => World.GetBlock(c))
        .Where(b => b.Id != 0)
        .Select(b => b.Code.ToString()),
    ];
    try
    {
      _pack.CheckStanding(standing);
      _pack.CheckEnds();
    }
    catch (InvalidOperationException e)
    {
      throw Fail("pack", e.Message);
    }
    return _pack.Record(standing);
  }
}
#endif
