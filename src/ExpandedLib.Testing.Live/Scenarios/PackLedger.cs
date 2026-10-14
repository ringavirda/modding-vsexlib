using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing.Live;

/// <summary>What a live case draws from and gives to its pack, accounted as the harness accounts it
/// (setups.md, The pack). Every method does nothing when the case has no pack.</summary>
internal static class PackLedger {
  /// <summary>Wraps a step's run: its <c>draws</c> are taken before the first tick, its
  /// <c>gives</c> after the last, and the item entities spawned beside the case are given at every
  /// tick.</summary>
  public static IEnumerable<bool> Accounted(
    ScenarioContext ctx,
    SetupStep step,
    IEnumerable<bool> run
  ) {
    foreach (var (code, count) in step.Draws)
      Draw(ctx, code, count);
    foreach (bool more in run) {
      GiveSpawns(ctx);
      yield return more;
    }
    foreach (var (code, count) in step.Gives)
      ctx.Pack?.Give(code, count);
    GiveSpawns(ctx);
  }

  /// <exception cref="StepFailure">The pack holds too few of <paramref name="code"/>.</exception>
  public static void Draw(
    ScenarioContext ctx,
    string code,
    int count,
    string? pin = null
  ) {
    if (ctx.Pack == null || count <= 0)
      return;
    try {
      ctx.Pack.Draw(code, count, ctx.Current, pin);
    } catch (InvalidOperationException e) {
      throw new StepFailure(e.Message, Suspect.Scenario, e);
    }
  }

  /// <exception cref="StepFailure">The pack holds too few of <paramref name="code"/>.</exception>
  public static void DrawBlock(ScenarioContext ctx, string code, string? pin = null) {
    if (ctx.Pack is not { DrawsBlocks: true } || ctx.Pack.TakeStanding(code))
      return;
    Draw(ctx, code, 1, pin);
  }

  /// <exception cref="StepFailure">The pack holds too few of a stage's stacks.</exception>
  public static void DrawStages(
    ScenarioContext ctx,
    ExRightClickConstruction construction,
    string pin
  ) {
    if (ctx.Pack is not { DrawsBlocks: true })
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
          ctx,
          Regex.Replace(need.Code.ToString(), @"\{[^}]*\}", "*"),
          need.Quantity,
          pin
        );
  }

  public static void Give(ScenarioContext ctx, ItemStack? stack) {
    if (stack != null)
      ctx.Pack?.Give(stack.Collectible.Code.ToString(), stack.StackSize);
  }

  /// <summary>Checks the end of a case: the metal its molten cells hold is handed on, then the
  /// standing blocks and the <c>ends</c> stacks are checked.</summary>
  /// <exception cref="StepFailure">A declared stack, unit or block is short.</exception>
  public static void Finish(ScenarioContext ctx) {
    SetupPack? pack = ctx.Pack;
    if (pack == null)
      return;
    foreach (var (metal, units) in MetalIn(ctx, ctx.Placed))
      pack.Give(metal, units);
    string[] standing =
    [
      .. ctx.Placed
        .Select(c => ctx.Setup.BlockAt(c))
        .Where(b => b.Id != 0)
        .Select(b => b.Code.ToString()),
    ];
    try {
      pack.CheckStanding(standing);
      pack.CheckEnds();
    } catch (InvalidOperationException e) {
      throw new StepFailure(e.Message, Suspect.Scenario, e);
    }
  }

  private static IEnumerable<(string Metal, int Units)> MetalIn(
    ScenarioContext ctx,
    IEnumerable<BlockPos> cells
  ) =>
    cells
      .Select(ctx.Setup.MoltenCellAt)
      .OfType<MoltenCellReading>()
      .Where(r => r.Amount > 0)
      .GroupBy(r => SetupPack.MetalCode(r.Metal))
      .Select(g => (g.Key, g.Sum(r => r.Amount)));

  private static void GiveSpawns(ScenarioContext ctx) {
    if (ctx.Pack == null)
      return;
    if (ctx.Placed.Count == 0)
      return;
    var cells = ctx.Placed.Select(c => ctx.Setup.Absolute(c)).ToList();
    var min = new BlockPos(
      cells.Min(c => c.X) - 1,
      cells.Min(c => c.Y) - 1,
      cells.Min(c => c.Z) - 1
    );
    var max = new BlockPos(
      cells.Max(c => c.X) + 1,
      cells.Max(c => c.Y) + 1,
      cells.Max(c => c.Z) + 1
    );
    foreach (
      Entity each in ctx.World.GetEntitiesInsideCuboid(
        min,
        max,
        e => e is EntityItem
      )
    )
      if (each is EntityItem item && ctx.SpawnsSeen.Add(each.EntityId))
        Give(ctx, item.Itemstack);
  }
}
