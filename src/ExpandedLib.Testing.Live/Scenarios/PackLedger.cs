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
    BlockPos local,
    string? pin = null,
    BlockPos? cell = null
  ) {
    if (
      ctx.Pack is not { DrawsBlocks: true }
      || ctx.Pack.TakeStanding(code, cell)
    )
      return;
    Draw(
      ctx,
      ctx.Pack.HeldFor(ctx.World, code, ctx.Setup.Absolute(local)),
      1,
      pin
    );
  }

  /// <summary>Draws the stacks of the stages <paramref name="construction"/> still has to pay up to
  /// <paramref name="target"/>; a <c>{placeholder}</c> in a code reads as the variant
  /// <paramref name="materials"/> names for it, or as <c>*</c>.</summary>
  /// <param name="cell">The plot-local cell of the construction's block; draws nothing when the pack
  /// took that block free from its standing list.</param>
  /// <param name="target">The last stage index the walk builds.</param>
  /// <exception cref="StepFailure">The pack holds too few of a stage's stacks.</exception>
  public static void DrawStages(
    ScenarioContext ctx,
    ExRightClickConstruction construction,
    BlockPos cell,
    int target,
    IReadOnlyDictionary<string, string> materials
  ) {
    if (ctx.Pack is not { DrawsBlocks: true } || ctx.Pack.TookStanding(cell))
      return;
    string pin = cell.ToString();
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
  /// <param name="order">The order whose bill is laid; a vessel laid for a bill that pays a liquid is
  /// filled with its litres. Null fills nothing.</param>
  /// <exception cref="StepFailure">The input has no loaded collectible, <paramref name="with"/>
  /// names none, or a vessel cannot be filled.</exception>
  public static ItemStack Laid(
    ScenarioContext ctx,
    CraftInput input,
    JObject? with,
    CraftOrder? order = null
  ) {
    try {
      ItemStack laid =
        ctx.Pack != null
        && with?[input.Name] == null
        && ctx.Pack.Laid(ctx.World, input) is { } held
          ? held
          : CraftBills.Laid(ctx.World, input, with);
      return order == null ? laid : CraftBills.Filled(ctx.World, order, laid);
    } catch (InvalidOperationException e) {
      throw new StepFailure(e.Message, Suspect.Scenario, e);
    }
  }

  /// <summary>The stack a sequence step's hand holds for the tool pattern <paramref name="pattern"/>,
  /// drawn from the pack when the case has one.</summary>
  /// <param name="label">What a failure names: the step and its recipe.</param>
  /// <exception cref="StepFailure">The pack holds no item that matches, no loaded item matches, or the
  /// pack holds none of the one it names.</exception>
  public static ItemStack? Tool(
    ScenarioContext ctx,
    string label,
    string? pattern
  ) {
    if (pattern == null)
      return null;
    ItemStack tool;
    string? drawn;
    try {
      tool = CraftBills.Tool(ctx.World, pattern, ctx.Pack, out drawn);
    } catch (InvalidOperationException e) {
      throw new StepFailure($"{label}: {e.Message}", Suspect.Scenario, e);
    }
    if (drawn != null)
      Draw(ctx, drawn, 1);
    return tool;
  }

  /// <summary>The hand and off-hand stacks a sequence step holds, each drawn from the pack as
  /// <see cref="Tool"/> draws one.</summary>
  /// <param name="label">What a failure names: the step and its recipe.</param>
  /// <exception cref="StepFailure">As <see cref="Tool"/>.</exception>
  public static (ItemStack? Hand, ItemStack? Offhand) Tools(
    ScenarioContext ctx,
    string label,
    Catalogues.CraftStep step
  ) => (Tool(ctx, label, step.Tool), Tool(ctx, label, step.Offhand));

  /// <summary>Gives the pack back the stacks a sequence step ends holding; a tool the game used up
  /// is null by then and stays drawn.</summary>
  public static void GiveTools(
    ScenarioContext ctx,
    ItemStack? hand,
    ItemStack? offhand
  ) {
    Give(ctx, hand);
    Give(ctx, offhand);
  }

  public static void Give(ScenarioContext ctx, ItemStack? stack) {
    if (stack != null)
      ctx.Pack?.Give(stack.Collectible.Code.ToString(), stack.StackSize);
  }

  /// <summary>Checks the end of a case: the metal its molten cells hold is handed on, then the
  /// standing blocks and, unless the case is a variant that carries <c>fails</c>, the <c>ends</c>
  /// stacks are checked.</summary>
  /// <param name="ctx">The running case.</param>
  /// <param name="failing">Whether the case is a variant with <c>fails</c>, which hands nothing on.</param>
  /// <exception cref="StepFailure">A declared stack, unit or block is short.</exception>
  public static void Finish(ScenarioContext ctx, bool failing) {
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
      if (!failing)
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
