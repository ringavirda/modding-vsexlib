using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary><c>select</c>: makes hotbar slot <c>slot</c> the scenario player's active one as the server's
/// hotbar-slot packet does (<see cref="ScenarioPlayer.Select"/>), so <c>BeforeActiveSlotChanged</c> and
/// <c>AfterActiveSlotChanged</c> fire. The step fails when no after event fires for the change.</summary>
internal sealed class SelectStep : ScenarioStep {
  /// <summary>The hotbar slots <c>slot</c> may name, numbered from 0.</summary>
  public const int HotbarSlots = 10;

  public override string Kind => "select";

  public override string What(SetupStep step) => $"slot {step.Body["slot"]}";

  public override string? Refuse(SetupStep step) {
    int slot = (int)step.Body["slot"]!;
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
    int slot = (int)step.Body["slot"]!;
    int from = manager.ActiveHotbarSlotNumber;
    int changes = player.SlotChanges;
    bool went = player.Select(slot);
    if (went && player.SlotChanges != changes + 1)
      throw new StepFailure(
        $"the server fired {player.SlotChanges - changes} active-slot-changed events for one select, not 1."
      );
    report.Value = went
      ? $"slot {from} to {manager.ActiveHotbarSlotNumber}"
      : $"slot {from} kept, the change was vetoed";
    yield break;
  }
}
