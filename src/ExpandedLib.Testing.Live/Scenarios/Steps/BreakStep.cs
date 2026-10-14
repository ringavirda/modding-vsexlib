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
/// break spawned are collected one tick after it for <c>expect drops</c>.</summary>
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

  /// <summary>The player a break by <paramref name="by"/> acts as.</summary>
  /// <param name="by"><c>player</c> or <c>world</c>.</param>
  /// <param name="scenarioPlayer">The scenario player.</param>
  /// <returns>Null for <c>world</c>, whose break has no breaker.</returns>
  public static IPlayer? Breaker(string by, IPlayer? scenarioPlayer) =>
    by == "world" ? null : scenarioPlayer;

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
  /// both name the same codes and each code's total differs by no more than 1, the rounding of a
  /// fractional salvage.</summary>
  public static string? DeclaredMismatch(
    IEnumerable<ItemStack> declared,
    IEnumerable<ItemStack> dropped
  ) {
    Dictionary<string, int> want = Sum(declared);
    Dictionary<string, int> got = Sum(dropped);
    List<string> wrong =
    [
      .. want
        .Keys.Union(got.Keys)
        .OrderBy(c => c, StringComparer.Ordinal)
        .Where(c =>
          !want.ContainsKey(c)
          || !got.ContainsKey(c)
          || Math.Abs(want[c] - got[c]) > 1
        )
        .Select(c =>
          $"{c} x{got.GetValueOrDefault(c)}, declared x{want.GetValueOrDefault(c)}"
        ),
    ];
    return wrong.Count == 0 ? null : string.Join("; ", wrong);
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    string by = (string?)o["by"] ?? "player";
    ScenarioPlayer? player = by == "world" ? null : ctx.Player;
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
    List<ItemStack> declared = DeclaredDrops(
      ctx.World,
      at,
      Breaker(by, player?.Player)
    );
    string block = ctx.Setup.BlockAt(local).Code.ToString();
    if (player == null)
      ctx.World.BlockAccessor.BreakBlock(at, null);
    else if (!player.Break(at, stack, out string? refused))
      throw new StepFailure(
        $"{block} at {FailureDetail.Cell(local)}: {refused}."
      );
    PackLedger.Give(ctx, stack);
    yield return true;
    List<ItemStack> dropped = [];
    foreach (
      Entity each in ctx.World.GetEntitiesInsideCuboid(
        new BlockPos(at.X - Margin, at.Y - Margin, at.Z - Margin),
        new BlockPos(at.X + Margin + 1, at.Y + Margin + 1, at.Z + Margin + 1),
        e => e is EntityItem
      )
    ) {
      dropped.Add(((EntityItem)each).Itemstack.Clone());
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
