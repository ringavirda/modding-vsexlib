using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
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

  /// <summary>Whether the hold goes on: the block answered the last step <paramref name="stepped"/> and
  /// the time held is still under <paramref name="seconds"/>.</summary>
  public static bool Goes(bool stepped, double held, double seconds) =>
    stepped && StillHeld(held, seconds);

  /// <summary>Fails the step when the claims do not let the player use the block at
  /// <paramref name="local"/>.</summary>
  /// <exception cref="StepFailure">The use is not <paramref name="allowed"/>.</exception>
  public static void RequireUse(bool allowed, BlockPos local) {
    if (!allowed)
      throw new StepFailure($"the player may not use {local}.");
  }

  /// <summary>The control an interact step clicks, its <c>control</c> key, or null for the block's body.</summary>
  public static string? ControlOf(JObject o) => (string?)o["control"];

  /// <summary>The selection an interact step clicks: the cell's body at the middle of <paramref name="face"/>,
  /// or the box of control <paramref name="control"/> when one is named.</summary>
  /// <param name="accessor">The accessor the cell's selection boxes are read through.</param>
  /// <param name="at">The absolute cell clicked.</param>
  /// <param name="control">The id of the control's box, or null for the body.</param>
  /// <param name="face">The face the click lands on.</param>
  /// <exception cref="StepFailure"><paramref name="control"/> is no box's id of the cell.</exception>
  public static BlockSelection SelectionFor(
    IBlockAccessor accessor,
    BlockPos at,
    string? control,
    BlockFacing face
  ) {
    if (control == null)
      return new BlockSelection {
        Position = at.Copy(),
        Face = face,
        HitPosition = ScenarioPlayer.Hit(face),
      };
    try {
      return ControlAim.Selection(accessor, at, control, face);
    } catch (ArgumentException e) {
      throw new StepFailure(e.Message, Suspect.Scenario);
    }
  }

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
    BlockSelection selection = SelectionFor(
      ctx.World.BlockAccessor,
      at,
      ControlOf(o),
      face
    );
    ItemStack? stack = o["with"] is JObject hand
      ? StepValues.Held(ctx, hand)
      : null;
    if (stack != null)
      PackLedger.Draw(ctx, stack.Collectible.Code.ToString(), stack.StackSize);
    string[] keys = KeyNames(o["keys"]);
    double seconds = (double?)o["seconds"] ?? 0;
    ctx.Touched.Add(local.Copy());
    RequireUse(
      ctx.World.Claims.TryAccess(player.Player, at, EnumBlockAccessFlags.Use),
      local
    );
    int uses = player.Uses;
    bool answered = player.UseStart(at, selection, stack, keys);
    try {
      if (player.Uses != uses + 1)
        throw new StepFailure(
          $"the server fired {player.Uses - uses} did-use events for one click, not 1."
        );
      double started = ctx.Now;
      if (answered) {
        bool stepped = true;
        while (Goes(stepped, ctx.Now - started, seconds)) {
          yield return true;
          stepped = player.UseStep(selection, ctx.Now - started);
        }
        player.UseStop(selection, ctx.Now - started);
      }
      PackLedger.Give(
        ctx,
        player.Player.InventoryManager.ActiveHotbarSlot.Itemstack
      );
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
