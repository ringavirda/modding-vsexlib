#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private SetupPack? _pack;

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

  private void Draw(string where, string code, int count)
  {
    if (_pack == null || count <= 0)
      return;
    try
    {
      _pack.Draw(code, count);
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

  private void DrawBlock(string where, string code)
  {
    if (_pack is not { DrawsBlocks: true } || _pack.TakeStanding(code))
      return;
    Draw(where, code, 1);
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

  private void CheckPack()
  {
    if (_pack == null)
      return;
    BlockPos[] cells =
    [
      .. _placed
        .Values.SelectMany(p => p.Cells)
        .GroupBy(c => (c.X, c.Y, c.Z))
        .Select(g => g.First()),
    ];
    foreach (var (metal, units) in MetalIn(cells))
      _pack.Give(metal, units);
    try
    {
      _pack.CheckStanding([
        .. cells.Select(c => World.GetBlock(c).Code.ToString()),
      ]);
      _pack.CheckEnds();
    }
    catch (InvalidOperationException e)
    {
      throw Fail("pack", e.Message);
    }
  }
}
#endif
