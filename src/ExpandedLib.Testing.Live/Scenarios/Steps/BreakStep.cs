using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary>What the last <c>break</c> declared it would drop and what dropped.</summary>
internal sealed record BreakRecord(
  IReadOnlyList<ItemStack> Declared,
  IReadOnlyList<ItemStack> Dropped
);

/// <summary><c>break</c>: takes the block at <c>at</c> down as the scenario player in survival holding
/// <c>with</c> (<c>by: "player"</c>, the default) or as the world with no breaker
/// (<c>by: "world"</c>). The block's declared drops are read just before, and the item entities the
/// break spawned, within the plot and 2 blocks around it, are collected one tick after it for
/// <c>expect drops</c>.</summary>
internal sealed class BreakStep : ScenarioStep {
  private const int Margin = 2;

  public override string Kind => "break";

  public override string What(SetupStep step) =>
    step.Body["at"]?.ToString(Formatting.None) ?? "";

  public override string? Refuse(SetupStep step) {
    var o = (JObject)step.Body;
    string by = (string?)o["by"] ?? "player";
    if (by is not ("player" or "world"))
      return $"\"by\" is \"player\" or \"world\", not \"{by}\".";
    if (by == "world" && o["with"] != null)
      return "\"with\" is for break by \"player\".";
    return null;
  }

  /// <summary>The scenario player a break by <paramref name="by"/> acts as.</summary>
  /// <param name="by"><c>player</c> or <c>world</c>.</param>
  /// <param name="scenarioPlayer">The scenario player, or null when the run has none.</param>
  /// <returns>Null for <c>world</c>, whose break has no breaker.</returns>
  public static ScenarioPlayer? Actor(
    string by,
    ScenarioPlayer? scenarioPlayer
  ) => by == "world" ? null : scenarioPlayer;

  /// <summary>The stacks a block at <paramref name="at"/> declares it drops for
  /// <paramref name="breaker"/>: <c>Block.GetDrops</c> of the principal when the cell is a
  /// filler, plus the construction's refund when the principal is a construction.</summary>
  public static List<ItemStack> DeclaredDrops(
    IWorldAccessor world,
    BlockPos at,
    IPlayer? breaker
  ) {
    BlockPos cell = at;
    if (
      world.BlockAccessor.GetBlockEntity(at)
        is BlockEntityStructureFiller { Principal: { } principal }
      && world.BlockAccessor.GetBlock(principal).Id != 0
    )
      cell = principal;
    Block block = world.BlockAccessor.GetBlock(cell);
    List<ItemStack> declared = [.. block.GetDrops(world, cell, breaker) ?? []];
    BlockEntity? entity = world.BlockAccessor.GetBlockEntity(cell);
    if (
      entity?.GetBehavior<ExRightClickConstructable>() is { } construction
      && breaker?.WorldData.CurrentGameMode != EnumGameMode.Creative
    ) {
      float ratio =
        ExRccSettings.BrokenDropsRatio(block.Code.Domain)
        ?? entity
          .Block.BlockEntityBehaviors.First(b =>
            b.Name == "ExRightClickConstructable"
          )
          .properties["brokenDropsRatio"]
          .AsFloat(1f);
      declared.AddRange(construction.GetConstructionDrops(ratio, world.Rand));
    }
    return declared;
  }

  /// <summary>The stack sizes of <paramref name="stacks"/> summed per collectible code.</summary>
  public static Dictionary<string, int> Sum(IEnumerable<ItemStack> stacks) =>
    stacks
      .GroupBy(s => s.Collectible.Code.ToString())
      .ToDictionary(g => g.Key, g => g.Sum(s => s.StackSize));

  /// <summary>How <paramref name="dropped"/> differs from <paramref name="expected"/>, or null when
  /// each listed code's total is its count and, with <paramref name="exact"/>, no other code
  /// dropped.</summary>
  public static string? Mismatch(
    IReadOnlyList<(string Code, int Count)> expected,
    IEnumerable<ItemStack> dropped,
    bool exact
  ) {
    Dictionary<string, int> actual = Sum(dropped);
    var wanted = new Dictionary<string, int>();
    foreach ((string code, int count) in expected)
      wanted[code] = wanted.GetValueOrDefault(code) + count;
    List<string> wrong =
    [
      .. wanted
        .Where(w => actual.GetValueOrDefault(w.Key) != w.Value)
        .Select(w =>
          $"{w.Key} x{actual.GetValueOrDefault(w.Key)}, expected x{w.Value}"
        ),
      .. exact
        ? actual
          .Where(a => !wanted.ContainsKey(a.Key))
          .Select(a => $"{a.Key} x{a.Value}, not listed")
        : [],
    ];
    return wrong.Count == 0 ? null : string.Join("; ", wrong);
  }

  /// <summary>How <paramref name="dropped"/> differs from <paramref name="declared"/>, or null when
  /// both name the same codes and each code's total differs by no more than the number of declared
  /// stacks of that code, one rounding of a fractional salvage per stack.</summary>
  public static string? DeclaredMismatch(
    IEnumerable<ItemStack> declared,
    IEnumerable<ItemStack> dropped
  ) {
    ItemStack[] declaredStacks = [.. declared];
    Dictionary<string, int> want = Sum(declaredStacks);
    Dictionary<string, int> got = Sum(dropped);
    Dictionary<string, int> stacks = declaredStacks
      .GroupBy(s => s.Collectible.Code.ToString())
      .ToDictionary(g => g.Key, g => g.Count());
    List<string> wrong =
    [
      .. want
        .Keys.Union(got.Keys)
        .OrderBy(c => c, StringComparer.Ordinal)
        .Where(c =>
          !want.ContainsKey(c)
          || !got.ContainsKey(c)
          || Math.Abs(want[c] - got[c]) > stacks[c]
        )
        .Select(c =>
          $"{c} x{got.GetValueOrDefault(c)}, declared x{want.GetValueOrDefault(c)}"
        ),
    ];
    return wrong.Count == 0 ? null : string.Join("; ", wrong);
  }

  private static List<EntityItem> ItemsAround(ScenarioContext ctx, BlockPos at) {
    (BlockPos min, BlockPos max) = ctx.PlotBounds ?? (at, at);
    return
    [
      .. ctx
        .World.GetEntitiesInsideCuboid(
          new BlockPos(min.X - Margin, min.Y - Margin, min.Z - Margin),
          new BlockPos(
            max.X + Margin + 1,
            max.Y + Margin + 1,
            max.Z + Margin + 1
          ),
          e => e is EntityItem
        )
        .Cast<EntityItem>(),
    ];
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    string by = (string?)o["by"] ?? "player";
    ScenarioPlayer? player = Actor(by, ctx.Player);
    if (by == "player" && player == null)
      throw new StepFailure(
        "\"break\" by the player needs the scenario player.",
        Suspect.Scenario
      );
    BlockPos local = ctx.Position(step, o["at"], "at");
    BlockPos at = ctx.Setup.Absolute(local);
    ctx.Touched.Add(local.Copy());
    ItemStack? stack = o["with"] is JObject hand
      ? StepValues.Stack(
        ctx,
        (string)hand["stack"]!,
        (int?)hand["count"] ?? 1,
        (float?)hand["temperature"]
      )
      : null;
    if (stack != null)
      PackLedger.Draw(ctx, stack.Collectible.Code.ToString(), stack.StackSize);
    if (ctx.IsAir(local))
      throw new StepFailure(
        $"{FailureDetail.Cell(local)} holds air, nothing to break.",
        Suspect.Scenario
      );
    if (
      player != null
      && !ctx.World.Claims.TryAccess(
        player.Player,
        at,
        EnumBlockAccessFlags.BuildOrBreak
      )
    )
      throw new StepFailure($"the player may not break {local}.");
    string block = ctx.Setup.BlockAt(local).Code.ToString();
    HashSet<long> before = [.. ItemsAround(ctx, at).Select(e => e.EntityId)];
    List<ItemStack> declared;
    if (player == null) {
      declared = DeclaredDrops(ctx.World, at, null);
      ctx.World.BlockAccessor.BreakBlock(at, null);
    } else if (
        !player.Break(
          at,
          stack,
          p => DeclaredDrops(ctx.World, at, p),
          out declared,
          out string? refused
        )
      )
      throw new StepFailure(
        $"{block} at {FailureDetail.Cell(local)}: {refused}."
      );
    PackLedger.Give(ctx, stack);
    yield return true;
    List<ItemStack> dropped = [];
    foreach (EntityItem each in ItemsAround(ctx, at))
      if (!before.Contains(each.EntityId)) {
        dropped.Add(each.Itemstack.Clone());
        each.Die(EnumDespawnReason.Removed);
      }
    ctx.LastBreak = new BreakRecord(declared, dropped);
    report.Value =
      $"{block} by {by}, dropped "
      + (
        dropped.Count == 0
          ? "nothing"
          : string.Join(", ", Sum(dropped).Select(d => $"{d.Key} x{d.Value}"))
      );
  }
}
