using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>pickup</c>: the scenario player takes the item entity within one cell of <c>at</c> (lowest entity
/// id first, whose code matches <c>code</c> when given) into its empty active hotbar slot, attributes kept, and
/// the entity is removed. The stack is drawn from the pack only when the pack was given it as a spawn. An
/// <c>offHand</c> stack is drawn from the pack into the off hand for the step and given back after it; a stack
/// hotter than the player can hold is refused without a <c>heatResistant</c> one.</summary>
internal sealed class PickUpStep : ScenarioStep {
  public override string Kind => "pickup";

  public override string What(SetupStep step) =>
    step.Body["at"]?.ToString(Formatting.None) ?? "";

  /// <summary>The live item entities within one cell of <paramref name="cell"/>, lowest entity id first.</summary>
  /// <param name="ctx">The running case.</param>
  /// <param name="cell">The world cell the step names.</param>
  internal static List<EntityItem> Around(ScenarioContext ctx, BlockPos cell) =>
    [
      .. ctx
        .World.GetEntitiesInsideCuboid(
          new BlockPos(cell.X - 1, cell.Y - 1, cell.Z - 1),
          new BlockPos(cell.X + 2, cell.Y + 2, cell.Z + 2),
          e => e is EntityItem { Alive: true }
        )
        .Cast<EntityItem>()
        .OrderBy(e => e.EntityId),
    ];

  /// <summary>Puts a clone of <paramref name="item"/>'s stack in <paramref name="active"/>.</summary>
  /// <param name="ctx">The running case.</param>
  /// <param name="active">The player's active hotbar slot, which is empty.</param>
  /// <param name="item">The entity picked.</param>
  /// <param name="offHand">The stack held in the off hand, null when it is empty.</param>
  /// <returns>The stack now in <paramref name="active"/>.</returns>
  /// <exception cref="StepFailure">The stack is too hot to hold; nothing has changed.</exception>
  internal static ItemStack Take(
    ScenarioContext ctx,
    ItemSlot active,
    EntityItem item,
    ItemStack? offHand
  ) {
    if (SetupPickUp.HeatRefusal(ctx.World, item.Itemstack, offHand) is { } hot)
      throw new StepFailure(hot, Suspect.Scenario);
    ItemStack taken = SetupPickUp.Taken(item.Itemstack);
    if (ctx.SpawnsSeen.Contains(item.EntityId))
      PackLedger.Draw(ctx, taken.Collectible.Code.ToString(), taken.StackSize);
    ctx.SpawnsSeen.Add(item.EntityId);
    active.Itemstack = taken;
    active.MarkDirty();
    return taken;
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
        "\"pickup\" needs the scenario player.",
        Suspect.Scenario
      );
    PackLedger.GiveSpawns(ctx);
    ItemSlot active = player.Player.InventoryManager.ActiveHotbarSlot;
    if (SetupPickUp.HandRefusal(active.Itemstack) is { } full)
      throw new StepFailure(full, Suspect.Scenario);
    BlockPos local = ctx.Position(step, o["at"], "at");
    BlockPos cell = ctx.Setup.Absolute(local);
    ItemStack? offHand = o["offHand"] is JObject off
      ? StepValues.Held(ctx, off)
      : null;
    if (offHand != null)
      PackLedger.Draw(
        ctx,
        offHand.Collectible.Code.ToString(),
        offHand.StackSize
      );
    player.HoldOffhand(offHand);
    try {
      List<EntityItem> near = Around(ctx, cell);
      int? chosen = SetupPickUp.Pick(
        [.. near.Select(e => (e.Itemstack, e.Pos.XYZ))],
        cell,
        (string?)o["code"],
        $"plot {FailureDetail.Cell(local)}",
        out string? problem
      );
      if (chosen is not { } index)
        throw new StepFailure(problem!, Suspect.Mod);
      EntityItem item = near[index];
      ItemStack taken = Take(ctx, active, item, offHand);
      item.Die(EnumDespawnReason.Removed);
      report.Value =
        $"{taken.Collectible.Code} x{taken.StackSize} picked up at plot {FailureDetail.Cell(local)}";
    } finally {
      PackLedger.Give(ctx, player.Offhand);
      player.HoldOffhand(null);
    }
    yield break;
  }
}
