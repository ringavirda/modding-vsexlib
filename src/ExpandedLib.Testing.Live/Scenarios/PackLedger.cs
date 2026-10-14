using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
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
  public static void DrawBlock(
    ScenarioContext ctx,
    string code,
    string? pin = null
  ) {
    if (ctx.Pack is not { DrawsBlocks: true } || ctx.Pack.TakeStanding(code))
      return;
    Draw(ctx, code, 1, pin);
  }

  /// <summary>Draws the stacks of the stages <paramref name="construction"/> still has to pay up to
  /// <paramref name="target"/>; a <c>{placeholder}</c> in a code reads as the variant
  /// <paramref name="materials"/> names for it, or as <c>*</c>.</summary>
  /// <param name="target">The last stage index the walk builds.</param>
  /// <exception cref="StepFailure">The pack holds too few of a stage's stacks.</exception>
  public static void DrawStages(
    ScenarioContext ctx,
    ExRightClickConstruction construction,
    string pin,
    int target,
    IReadOnlyDictionary<string, string> materials
  ) {
    if (ctx.Pack is not { DrawsBlocks: true })
      return;
    for (int i = construction.CurrentCompletedStage + 1; i <= target; i++)
      foreach (
        ExConstructionIngredient need in construction.Stages[i].RequireStacks
          ?? []
      )
        Draw(
          ctx,
          Regex.Replace(
            need.Code.ToString(),
            @"\{([^}]*)\}",
            m => materials.GetValueOrDefault(m.Groups[1].Value, "*")
          ),
          need.Quantity,
          pin
        );
  }

  /// <summary>Lays <paramref name="input"/> for a craft: the stack the pack holds when
  /// <paramref name="with"/> names nothing for the input, otherwise the first loaded
  /// variant.</summary>
  /// <exception cref="StepFailure">The input has no loaded collectible, or <paramref name="with"/>
  /// names none.</exception>
  public static ItemStack Laid(
    ScenarioContext ctx,
    CraftInput input,
    JObject? with
  ) {
    if (
      ctx.Pack != null
      && with?[input.Name] == null
      && ctx.Pack.Laid(ctx.World, input) is { } held
    )
      return held;
    try {
      return CraftBills.Laid(ctx.World, input, with);
    } catch (InvalidOperationException e) {
      throw new StepFailure(e.Message, Suspect.Scenario, e);
    }
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
      .. ctx
        .Placed.Select(c => ctx.Setup.BlockAt(c))
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

  /// <summary>Gives the pack the item entities that landed within one cell of the placed blocks and
  /// were not given before; does nothing without a pack or placed blocks.</summary>
  internal static void GiveSpawns(ScenarioContext ctx) {
    if (ctx.Pack == null || ctx.Placed.Count == 0)
      return;
    var cells = ctx.Placed.Select(c => ctx.Setup.Absolute(c)).ToList();
    int x0 = cells.Min(c => c.X) - 1,
      x1 = cells.Max(c => c.X) + 1;
    int y0 = cells.Min(c => c.Y) - 1,
      y1 = cells.Max(c => c.Y) + 1;
    int z0 = cells.Min(c => c.Z) - 1,
      z1 = cells.Max(c => c.Z) + 1;
    foreach (
      Entity each in ctx.World.GetEntitiesInsideCuboid(
        new BlockPos(x0, y0, z0),
        new BlockPos(x1 + 1, y1 + 1, z1 + 1),
        e =>
          e is EntityItem
          && Math.Floor(e.Pos.X) <= x1
          && Math.Floor(e.Pos.Y) <= y1
          && Math.Floor(e.Pos.Z) <= z1
      )
    )
      if (each is EntityItem item && ctx.SpawnsSeen.Add(each.EntityId))
        Give(ctx, item.Itemstack);
  }
}
