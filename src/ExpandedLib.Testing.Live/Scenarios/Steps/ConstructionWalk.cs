using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary>The stage part of <c>complete</c>: before each stage the hotbar is filled with a supply the
/// walk generates, then the scenario player right-clicks the block in survival, so exlib's own
/// consumption takes the stacks and stores the variants.</summary>
internal static class ConstructionWalk {
  /// <summary>The last stage index <paramref name="to"/> asks for.</summary>
  /// <param name="to">A stage index, or "all", true or null for the last.</param>
  /// <param name="stages">The number of stages the construction has.</param>
  /// <exception cref="StepFailure">The stage is not an index of the construction.</exception>
  public static int Target(JToken? to, int stages) {
    if (
      to == null
      || to.Type == JTokenType.Boolean && (bool)to
      || to.Type == JTokenType.String && (string?)to == "all"
    )
      return stages - 1;
    if (to.Type != JTokenType.Integer || (int)to < 0 || (int)to >= stages)
      throw new StepFailure(
        $"\"to\" {to} is not a stage of a construction of {stages} stage(s), 0 to {stages - 1}.",
        Suspect.Scenario
      );
    return (int)to;
  }

  /// <summary>The variant each stored wildcard key is supplied in.</summary>
  /// <exception cref="StepFailure">The token is not an object of strings.</exception>
  public static Dictionary<string, string> Materials(JToken? token) {
    var materials = new Dictionary<string, string>();
    if (token == null)
      return materials;
    if (
      token is not JObject o
      || o.Properties().Any(p => p.Value.Type != JTokenType.String)
    )
      throw new StepFailure(
        "\"materials\" maps each wildcard key to a variant name.",
        Suspect.Scenario
      );
    foreach (JProperty p in o.Properties())
      materials[p.Name] = (string)p.Value!;
    return materials;
  }

  /// <summary>Whether a collectible whose variant under <paramref name="key"/> is
  /// <paramref name="variant"/> can supply an ingredient that stores <paramref name="key"/>.</summary>
  /// <param name="key">The key the ingredient stores, or null for an ingredient that stores none.</param>
  /// <param name="wanted">The variant each key must be supplied in; a key it lacks takes any.</param>
  public static bool Allows(
    string? key,
    string? variant,
    IReadOnlyDictionary<string, string> wanted
  ) =>
    key == null
    || !wanted.TryGetValue(key, out string? only)
    || only == variant;

  /// <summary>The stacks that pay <paramref name="stage"/>: for each ingredient the first registered
  /// collectible that satisfies it, in the variant <paramref name="materials"/> names for a stored key
  /// and the one an earlier ingredient of the stage took for the same key.</summary>
  /// <exception cref="StepFailure">An ingredient is satisfied by no registered collectible.</exception>
  public static List<ItemStack> Supply(
    IWorldAccessor world,
    ExRightClickConstruction rcc,
    int stage,
    IReadOnlyDictionary<string, string> materials
  ) {
    var wanted = new Dictionary<string, string>(materials);
    var stacks = new List<ItemStack>();
    foreach (
      ExConstructionIngredient each in rcc.Stages[stage].RequireStacks ?? []
    ) {
      ExConstructionIngredient ingredient = each.Clone();
      foreach ((string stored, string value) in rcc.StoredWildCards)
        ingredient.FillPlaceHolder(stored, value);
      ingredient.Resolve(world, "construction walk");
      ItemStack stack =
        Pick(world, ingredient, wanted)
        ?? throw new StepFailure(
          $"stage {stage} asks for {ingredient.Code}, which no registered {ingredient.Type.ToString().ToLowerInvariant()} supplies"
            + (
              materials.Count == 0
                ? "."
                : $" in {string.Join(", ", materials.Select(m => $"{m.Key} {m.Value}"))}."
            ),
          Suspect.Scenario
        );
      if (
        ingredient.StoreWildCard is { } key
        && stack.Collectible.Variant.TryGetValue(key, out string? variant)
      )
        wanted[key] = variant;
      stacks.Add(stack);
    }
    return stacks;
  }

  private static ItemStack? Pick(
    IWorldAccessor world,
    ExConstructionIngredient ingredient,
    IReadOnlyDictionary<string, string> wanted
  ) {
    IEnumerable<CollectibleObject> registered =
      ingredient.Type == EnumItemClass.Item ? world.Items : world.Blocks;
    foreach (CollectibleObject c in registered) {
      if (c?.Code == null || c.Id == 0)
        continue;
      string? variant =
        ingredient.StoreWildCard is { } k
        && c.Variant.TryGetValue(k, out string? v)
          ? v
          : null;
      if (!Allows(ingredient.StoreWildCard, variant, wanted))
        continue;
      var stack = new ItemStack(c, ingredient.Quantity);
      if (ingredient.SatisfiesAsIngredient(stack, false))
        return stack;
    }
    return null;
  }

  /// <summary>The seconds a hold takes to finish every hit of <paramref name="step"/> with
  /// <paramref name="tool"/>.</summary>
  /// <param name="step">The tool step being paid.</param>
  /// <param name="tool">The stack in the main hand; null when the step's tool is not held.</param>
  public static double HoldSeconds(ExConstructionToolStep step, ItemStack? tool) {
    float stroke =
      CraftSequenceRegistry
        .Shared.ToolDefault(
          tool?.Collectible.Code.ToString() ?? step.Tool ?? ""
        )
        ?.Stroke
      ?? CraftToolDefault.DefaultStroke;
    return (Math.Max(1, step.Hits) * (double)stroke) + 0.1;
  }

  private static string HandText(ItemStack? tool) =>
    tool == null
      ? "tool broke"
      : $"{ScenarioPlayer.StackText(tool)}, {tool.Collectible.GetRemainingDurability(tool)} left";

  /// <summary>What one click on a construction left behind.</summary>
  /// <param name="Answered">What the block's interaction start answered.</param>
  /// <param name="Left">The hotbar after the click.</param>
  /// <param name="ToolLeft">The main hand after the click; null when the tool broke or none was held.</param>
  internal sealed record StageClick(
    bool Answered,
    string Left,
    ItemStack? ToolLeft
  );

  /// <summary>The last click paying a stage and the tool it held.</summary>
  internal sealed record StagePayment(
    bool Answered,
    string Left,
    ItemStack? Tool,
    ItemStack? ToolLeft
  );

  /// <summary>Pays stage <paramref name="stage"/> of <paramref name="rcc"/> with one click per tool step,
  /// holding each step's tool for the seconds its hits take, and a single plain click when the stage
  /// names no tool; stops as soon as the stage is built.</summary>
  /// <param name="label">Names the stage for the pack when a tool cannot be had.</param>
  /// <param name="supply">The stage's materials, put in the hotbar for every click.</param>
  /// <param name="click">Makes one click with the supply, the tool in the main hand and the seconds held.</param>
  /// <returns>The last click made.</returns>
  /// <exception cref="StepFailure">A step's tool is not in the pack and not loaded.</exception>
  internal static StagePayment Pay(
    ScenarioContext ctx,
    ExRightClickConstruction rcc,
    int stage,
    string label,
    List<ItemStack> supply,
    System.Func<List<ItemStack>, ItemStack?, double, StageClick> click
  ) {
    ExConstructionToolStep[] steps = rcc.Stages[stage].ToolSteps;
    ItemStack? tool = null;
    StageClick last = new(false, "", null);
    for (int step = 0; step < Math.Max(1, steps.Length); step++) {
      tool = PackLedger.Tool(
        ctx,
        label,
        steps.Length == 0 ? null : steps[step].Tool
      );
      last = click(
        supply,
        tool,
        steps.Length == 0 ? 0 : HoldSeconds(steps[step], tool)
      );
      PackLedger.Give(ctx, last.ToolLeft);
      if (rcc.CurrentCompletedStage == stage)
        break;
    }
    return new StagePayment(last.Answered, last.Left, tool, last.ToolLeft);
  }

  /// <summary>Builds the construction of the block at plot-local <paramref name="cell"/> up to the stage
  /// <paramref name="to"/> names, one right-click per stage, yielding once after each.</summary>
  /// <exception cref="StepFailure">There is no scenario player, a stage cannot be supplied, or a click
  /// does not build its stage.</exception>
  public static IEnumerable<bool> Walk(
    ScenarioContext ctx,
    BlockPos cell,
    ExRightClickConstructable behavior,
    JToken? to,
    JToken? materials,
    StepReport report
  ) {
    ScenarioPlayer player =
      ctx.Player
      ?? throw new StepFailure(
        "\"complete\" of a construction needs the scenario player.",
        Suspect.Scenario
      );
    ExRightClickConstruction rcc = behavior.Construction;
    int target = Target(to, rcc.Stages.Length);
    Dictionary<string, string> wanted = Materials(materials);
    BlockPos at = ctx.Setup.Absolute(cell);
    ctx.Touched.Add(cell.Copy());
    var built = new List<string>();
    while (rcc.CurrentCompletedStage < target) {
      int stage = rcc.CurrentCompletedStage + 1;
      List<ItemStack> supply = Supply(ctx.World, rcc, stage, wanted);
      string given = string.Join(
        ", ",
        supply.Select(s => $"{s.Collectible.Code} x {s.StackSize}")
      );
      StagePayment paid = Pay(
        ctx,
        rcc,
        stage,
        $"stage {stage} of {FailureDetail.Cell(cell)}",
        supply,
        (stacks, tool, seconds) => {
          bool answered = player.Construct(
            at,
            BlockFacing.SOUTH,
            stacks,
            tool,
            seconds,
            out string left,
            out ItemStack? toolLeft
          );
          return new StageClick(answered, left, toolLeft);
        }
      );
      if (rcc.CurrentCompletedStage != stage)
        throw new StepFailure(
          $"stage {stage} was not built: OnBlockInteractStart answered {paid.Answered} with {given} in the hotbar"
            + (
              paid.Tool == null
                ? "."
                : $" and {paid.Tool.Collectible.Code} in the main hand."
            )
        );
      built.Add(
        $"stage {stage} (hotbar after: {paid.Left}"
          + (paid.Tool == null ? ")" : $"; {HandText(paid.ToolLeft)})")
      );
      yield return true;
    }
    string text =
      built.Count == 0 ? "no stage to build" : string.Join("; ", built);
    report.Value = report.Value == null ? text : report.Value + "; " + text;
  }
}
