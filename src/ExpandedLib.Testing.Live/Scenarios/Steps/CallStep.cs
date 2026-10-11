using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>call</c>: a public instance method of the block entity, or of one of its
/// behaviours as <c>Behaviour.Method</c>, matched by name and argument count.</summary>
internal sealed class CallStep : ScenarioStep {
  public override string Kind => "call";

  public override string What(SetupStep step) =>
    $"{step.Body["method"]} {step.Body["at"]?.ToString(Formatting.None)}";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    BlockPos local = ctx.Position(step, o["at"], "at");
    BlockEntity be = SetStep.EntityAt(ctx, local);
    (object root, IReadOnlyList<string> path) = StepValues.Root(be, (string)o["method"]!);
    string name = path[0];
    JArray args = o["args"] as JArray ?? [];
    MethodInfo[] candidates =
    [
      .. root
        .GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .Where(m => m.Name == name && m.GetParameters().Length == args.Count),
    ];
    if (candidates.Length == 0)
      throw new StepFailure(
        $"{root.GetType().Name} has no public method {name} taking {args.Count} argument(s).",
        Suspect.Scenario
      );
    MethodInfo method = candidates[0];
    object?[] values =
    [
      .. method.GetParameters().Select((p, i) => StepValues.Convert(ctx, args[i], p.ParameterType)),
    ];
    object? result = method.Invoke(root, values);
    ctx.Touched.Add(local.Copy());

    object read = SetupValues.Normalize(result);
    if (o["returns"] != null) {
      object expected = SetupJson.Literal(o["returns"], ctx.Where(step).In("returns"));
      bool same =
        read is float a && expected is float b
          ? Math.Abs(a - b) <= CompareTest.Tolerance
          : SetupValues.Text(read) == SetupValues.Text(expected);
      if (!same)
        throw new StepFailure(
          $"{name} returned {SetupValues.Text(read)}, expected {SetupValues.Text(expected)}."
        );
    }
    string left = string.Concat(
      values
        .OfType<ItemSlot>()
        .Select(s => $"; slot holds {(s.Empty ? "nothing" : $"{s.Itemstack.Collectible.Code} x{s.StackSize}")}")
    );
    report.Value = SetupValues.Text(read) + left;
    yield break;
  }
}
