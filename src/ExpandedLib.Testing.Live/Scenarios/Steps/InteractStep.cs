using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>interact</c>: the scenario player right-clicks the block as a held mouse button does. The
/// block is stepped on each runner tick for <c>seconds</c>, then stopped; the hand is cleared once the
/// report has read what it held.</summary>
internal sealed class InteractStep : ScenarioStep {
  private static readonly string[] Names = ["sneak", "ctrl", "sprint"];

  public override string Kind => "interact";

  public override string What(SetupStep step) =>
    step.Body["at"]?.ToString(Formatting.None) ?? "";

  /// <summary>Whether a hold that has run <paramref name="held"/> seconds goes on for a hold of
  /// <paramref name="seconds"/>.</summary>
  public static bool StillHeld(double held, double seconds) => held < seconds;

  /// <summary>The keys a step holds, each one of <c>sneak</c>, <c>ctrl</c> or <c>sprint</c>.</summary>
  /// <exception cref="StepFailure">A name is none of them.</exception>
  public static string[] KeyNames(JToken? token) {
    string[] keys = [.. (token as JArray ?? []).Select(k => (string)k!)];
    foreach (string key in keys)
      if (!Names.Contains(key))
        throw new StepFailure(
          $"\"{key}\" is not a key the scenario player holds.",
          Suspect.Scenario
        );
    return keys;
  }

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
        "\"interact\" needs the scenario player.",
        Suspect.Scenario
      );
    BlockPos local = ctx.Position(step, o["at"], "at");
    BlockPos at = ctx.Setup.Absolute(local);
    BlockFacing face = o["face"] is JToken token
      ? ExOrientation.FacingFromSide((string)token)
        ?? throw new StepFailure(
          $"\"{(string)token}\" is not a face.",
          Suspect.Scenario
        )
      : BlockFacing.SOUTH;
    ItemStack? stack = o["with"] is JObject hand
      ? StepValues.Stack(
        ctx,
        (string)hand["stack"]!,
        (int?)hand["count"] ?? 1,
        (float?)hand["temperature"]
      )
      : null;
    string[] keys = KeyNames(o["keys"]);
    double seconds = (double?)o["seconds"] ?? 0;
    ctx.Touched.Add(local.Copy());
    if (
      !ctx.World.Claims.TryAccess(player.Player, at, EnumBlockAccessFlags.Use)
    )
      throw new StepFailure($"the player may not use {local}.");
    bool answered = player.UseStart(
      at,
      face,
      stack,
      keys,
      out BlockSelection selection
    );
    try {
      double started = ctx.Now;
      if (answered) {
        while (StillHeld(ctx.Now - started, seconds)) {
          yield return true;
          if (!player.UseStep(selection, ctx.Now - started))
            break;
        }
        player.UseStop(selection, ctx.Now - started);
      }
      report.Value = $"hand: {player.HandText()}";
      if (o["returns"] is { } returns && (bool)returns != answered)
        throw new StepFailure(
          $"OnBlockInteractStart answered {answered}, not {(bool)returns}."
        );
    } finally {
      player.Release();
    }
  }
}
