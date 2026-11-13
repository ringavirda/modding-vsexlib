using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary><c>select</c>: makes hotbar slot <c>slot</c> the scenario player's active one as the server's
/// hotbar-slot packet does (<see cref="ScenarioPlayer.Select"/>), so <c>BeforeActiveSlotChanged</c> and
/// <c>AfterActiveSlotChanged</c> fire. The step fails when no after event fires for the change. An <c>offHand</c>
/// stack is drawn from the pack into the off hand for the change and given back after it.</summary>
internal sealed class SelectStep : ScenarioStep {
  /// <summary>The hotbar slots <c>slot</c> may name, numbered from 0.</summary>
  public const int HotbarSlots = 10;

  public override string Kind => "select";

  public override string What(SetupStep step) =>
    step.Body["slot"] is { } slot ? $"slot {slot}" : $"{step.Body["code"]}";

  public override string? Refuse(SetupStep step) {
    if (step.Body["slot"] is not { } named)
      return null;
    int slot = (int)named;
    return slot >= 0 && slot < HotbarSlots
      ? null
      : $"slot {slot} is not a hotbar slot (0 to {HotbarSlots - 1}).";
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    ScenarioPlayer player =
      ctx.Player
      ?? throw new StepFailure(
        "\"select\" needs the scenario player.",
        Suspect.Scenario
      );
    IPlayerInventoryManager manager = player.Player.InventoryManager;
    int slot = step.Body["slot"] is { } named
      ? (int)named
      : SlotHolding(player, (string)step.Body["code"]!);
    int from = manager.ActiveHotbarSlotNumber;
    int changes = player.SlotChanges;
    ItemStack? offHand = step.Body["offHand"] is JObject off
      ? StepValues.Held(ctx, off)
      : null;
    if (offHand != null)
      PackLedger.Draw(
        ctx,
        offHand.Collectible.Code.ToString(),
        offHand.StackSize
      );
    player.HoldOffhand(offHand);
    bool went;
    try {
      went = player.Select(slot);
    } finally {
      PackLedger.Give(ctx, player.Offhand);
      player.HoldOffhand(null);
    }
    if (went && player.SlotChanges != changes + 1)
      throw new StepFailure(
        $"the server fired {player.SlotChanges - changes} active-slot-changed events for one select, not 1."
      );
    report.Value = went
      ? $"slot {from} to {manager.ActiveHotbarSlotNumber}"
      : $"slot {from} kept, the change was vetoed";
    yield break;
  }

  private static int SlotHolding(ScenarioPlayer player, string code) {
    IInventory hotbar = player.Player.InventoryManager.GetHotbarInventory();
    int found = SetupSlotExpect.SlotHolding(
      code,
      [.. Enumerable.Range(0, hotbar.Count).Select(i => hotbar[i].Itemstack)],
      HotbarSlots
    );
    return found >= 0
      ? found
      : throw new StepFailure($"no hotbar slot holds {code}.");
  }
}
