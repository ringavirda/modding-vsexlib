using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>insert</c>: puts a stack into the block entity's inventory through
/// <c>ItemSlot.TryPutInto</c>, so the inventory's own acceptance rules apply.</summary>
internal sealed class InsertStep : ScenarioStep {
  public override string Kind => "insert";

  public override string What(SetupStep step) =>
    $"{step.Body["stack"]} {step.Body["at"]?.ToString(Formatting.None)}";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    BlockPos local = ctx.Position(step, o["at"], "at");
    BlockEntity be = SetStep.EntityAt(ctx, local);
    IBlockEntityContainer container =
      be as IBlockEntityContainer
      ?? be.Behaviors?.OfType<IBlockEntityContainer>().FirstOrDefault()
      ?? throw new StepFailure(
        $"{be.GetType().Name} at {FailureDetail.Cell(local)} holds no inventory.",
        Suspect.Scenario
      );
    int count = (int?)o["count"] ?? 1;
    ItemStack stack = StepValues.Stack(
      ctx,
      (string)o["stack"]!,
      count,
      (float?)o["temperature"]
    );
    var source = new DummySlot(stack);
    ItemSlot? target =
      o["slot"] != null
        ? container.Inventory[(int)o["slot"]!]
        : container.Inventory.GetBestSuitedSlot(source).slot;
    if (target == null)
      throw new StepFailure(
        $"{stack.Collectible.Code} fits no slot of the inventory."
      );
    PackLedger.Draw(ctx, stack.Collectible.Code.ToString(), count);
    int moved = source.TryPutInto(ctx.World, target, count);
    if (moved < count)
      throw new StepFailure(
        $"the inventory took {moved} of {count} {stack.Collectible.Code}."
      );
    be.MarkDirty();
    ctx.Touched.Add(local.Copy());
    report.Value = $"{moved} x {stack.Collectible.Code}";
    yield break;
  }
}
