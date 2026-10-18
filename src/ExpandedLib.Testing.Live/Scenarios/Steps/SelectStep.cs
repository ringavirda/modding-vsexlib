using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary><c>select</c>: makes hotbar slot <c>slot</c> the scenario player's active one by setting
/// <c>ActiveHotbarSlotNumber</c> on its inventory manager, which the server answers with
/// <c>AfterActiveSlotChanged</c>.</summary>
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
    manager.ActiveHotbarSlotNumber = slot;
    report.Value = $"slot {from} to {manager.ActiveHotbarSlotNumber}";
    yield break;
  }
}
