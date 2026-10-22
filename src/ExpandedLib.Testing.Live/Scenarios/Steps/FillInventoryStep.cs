using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary><c>fillInventory</c>: fills every hotbar and backpack slot of the scenario player that can hold
/// the item <c>code</c> with a full stack of it (<see cref="SetupInventory.Fill"/>), so the player takes
/// no more of another item; the active slot's stack stays through the steps that empty the hand.</summary>
internal sealed class FillInventoryStep : ScenarioStep {
  public override string Kind => "fillInventory";

  public override string What(SetupStep step) => (string)step.Body["code"]!;

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    ScenarioPlayer player =
      ctx.Player
      ?? throw new StepFailure(
        "\"fillInventory\" needs the scenario player.",
        Suspect.Scenario
      );
    string code = (string)step.Body["code"]!;
    var location = new AssetLocation(code);
    CollectibleObject? item =
      ctx.World.GetItem(location)
      ?? (CollectibleObject?)ctx.World.GetBlock(location);
    if (item == null || item.Id == 0)
      throw new StepFailure(
        $"\"{code}\" is no item or block code.",
        Suspect.Scenario
      );
    int filled = SetupInventory.Fill(player.Player, item);
    player.KeepHand();
    report.Value = $"{filled} slots of {code}";
    yield break;
  }
}
