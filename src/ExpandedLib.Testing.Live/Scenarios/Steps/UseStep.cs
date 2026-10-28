using System.Collections.Generic;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>use</c>: the scenario player uses the held item on the cell as a held mouse button does. The
/// item's own use is stepped on each runner tick for <c>seconds</c>, then stopped; the hand is cleared once
/// the report has read what it held, unless the step says <c>keep</c>.</summary>
internal sealed class UseStep : ScenarioStep {
  public override string Kind => "use";

  public override string What(SetupStep step) =>
    step.Body["at"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";

  /// <summary>The point in the cell a <c>use</c> aims at: its <c>hit</c> in sixteenths divided to the 0 to 1
  /// of <see cref="BlockSelection.HitPosition"/>, else the middle of <paramref name="face"/>.</summary>
  public static Vec3d HitOf(JObject o, BlockFacing face) =>
    o["hit"] is JArray cell
      ? new Vec3d(
        (double)cell[0] / 16,
        (double)cell[1] / 16,
        (double)cell[2] / 16
      )
      : ScenarioPlayer.Hit(face);

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
        "\"use\" needs the scenario player.",
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
      : BlockFacing.UP;
    var selection = new BlockSelection {
      Position = at.Copy(),
      Face = face,
      HitPosition = HitOf(o, face),
    };
    ItemStack? stack = o["with"] is JObject hand
      ? StepValues.Held(ctx, hand)
      : null;
    ItemSlot active = player.Player.InventoryManager.ActiveHotbarSlot;
    if (stack != null)
      PackLedger.Draw(ctx, stack.Collectible.Code.ToString(), stack.StackSize);
    string? withText = stack == null ? null : ScenarioPlayer.StackText(stack);
    string[] keys = InteractStep.KeyNames(o["keys"]);
    double seconds = (double)o["seconds"]!;
    ctx.Touched.Add(local.Copy());
    InteractStep.RequireUse(
      ctx.World.Claims.TryAccess(player.Player, at, EnumBlockAccessFlags.Use),
      local
    );
    EnumHandHandling handling = player.HeldStart(
      at,
      selection,
      InteractStep.ClickStack(stack, active),
      keys
    );
    try {
      if (handling == EnumHandHandling.NotHandled)
        throw new StepFailure(
          $"{player.HandText()} did not take the use at {local}."
        );
      double started = ctx.Now;
      bool stepped = true;
      while (InteractStep.Goes(stepped, ctx.Now - started, seconds)) {
        yield return true;
        stepped = player.HeldStep(selection, ctx.Now - started);
      }
      player.HeldStop(selection, ctx.Now - started);
      PackLedger.Give(
        ctx,
        InteractStep.Returned(active, InteractStep.Keeps(o))
      );
      report.Value = InteractStep.HandLine(withText, player.HandText());
    } finally {
      InteractStep.Finish(player, o);
    }
  }
}
