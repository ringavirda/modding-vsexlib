using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>use</c>: the scenario player uses the held item on the cell as a held mouse button does. The
/// item's own use is stepped on each runner tick for <c>seconds</c>, then stopped; the hand is cleared once
/// the report has read what it held, unless the step says <c>keep</c>. With an <c>id</c> the block the use
/// placed is recorded as a placement: the cell within <see cref="Reach"/> of the aimed one whose block the
/// use changed.</summary>
internal sealed class UseStep : ScenarioStep {
  public override string Kind => "use";

  /// <summary>How far from the aimed plot-local cell, in cells on each axis, a use with an <c>id</c> looks
  /// for the block it placed.</summary>
  public const int Reach = 2;

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
    ItemStack? offHand = o["offHand"] is JObject off
      ? StepValues.Held(ctx, off)
      : null;
    if (offHand != null)
      PackLedger.Draw(
        ctx,
        offHand.Collectible.Code.ToString(),
        offHand.StackSize
      );
    string? withText = stack == null ? null : ScenarioPlayer.StackText(stack);
    string[] keys = InteractStep.KeyNames(o["keys"]);
    double seconds = (double)o["seconds"]!;
    ctx.Touched.Add(local.Copy());
    InteractStep.RequireUse(
      ctx.World.Claims.TryAccess(player.Player, at, EnumBlockAccessFlags.Use),
      local
    );
    Dictionary<BlockPos, int>? before =
      step.Id == null ? null : Around(ctx, local);
    EnumHandHandling handling = player.HeldStart(
      at,
      selection,
      InteractStep.ClickStack(stack, active),
      keys,
      offHand
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
      PackLedger.Give(
        ctx,
        player.Player.InventoryManager.OffhandHotbarSlot.Itemstack
      );
      report.Value = InteractStep.HandLine(withText, player.HandText());
      if (step.Id != null)
        RecordPlaced(ctx, step, local, before!);
    } finally {
      InteractStep.Finish(player, o);
    }
  }

  static Dictionary<BlockPos, int> Around(ScenarioContext ctx, BlockPos local) {
    var cells = new Dictionary<BlockPos, int>();
    for (int x = -Reach; x <= Reach; x++)
      for (int y = -Reach; y <= Reach; y++)
        for (int z = -Reach; z <= Reach; z++) {
          BlockPos cell = local.AddCopy(x, y, z);
          if (cell.Y >= 0)
            cells[cell] = ctx.Setup.BlockAt(cell).BlockId;
        }
    return cells;
  }

  static void RecordPlaced(
    ScenarioContext ctx,
    SetupStep step,
    BlockPos local,
    Dictionary<BlockPos, int> before
  ) {
    List<(BlockPos Pos, bool Entity, bool Host)> appeared =
    [
      .. Around(ctx, local)
        .Where(c => c.Value != 0 && before[c.Key] != c.Value)
        .Select(c =>
          (
            c.Key,
            ctx.Setup.BlockEntityAt(c.Key) != null,
            ctx.Setup.BlockAt(c.Key) is IFillerHost
          )
        ),
    ];
    BlockPos placed =
      SetupUsePlaced.Of(appeared, step.Id!, out string? problem)
      ?? throw new StepFailure(problem!);
    ctx.Placed.Add(placed.Copy());
    ctx.Setup.Place(
      step.Id!,
      new SetupPlacement(
        placed.Copy(),
        [placed.Copy()],
        ctx.Setup.FrameAt(placed)
      )
    );
  }
}
