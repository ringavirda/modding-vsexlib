using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>set</c>: the block entity's tree keys, each written with the type the key
/// already has and loaded back, or its fields and properties by path.</summary>
internal sealed class SetStep : ScenarioStep {
  public override string Kind => "set";

  public override string What(SetupStep step) =>
    step.Body["at"]?.ToString(Formatting.None) ?? "";

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    BlockPos local = ctx.Position(step, o["at"], "at");
    if (o["tree"] is JObject tree)
      report.Value = WriteTree(ctx, local, tree);
    else {
      BlockEntity be = EntityAt(ctx, local);
      var member = (JObject)o["member"]!;
      foreach (JProperty property in member.Properties()) {
        (object root, IReadOnlyList<string> path) = StepValues.Root(
          be,
          property.Name
        );
        StepValues.SetMember(ctx, root, path, property.Value);
      }
      be.MarkDirty();
      report.Value = string.Join(
        ", ",
        member.Properties().Select(p => $"{p.Name}={p.Value}")
      );
    }
    ctx.Touched.Add(local.Copy());
    yield break;
  }

  /// <summary>Writes <paramref name="values"/> into the tree of the block entity at plot-local
  /// <paramref name="local"/> and loads it back.</summary>
  /// <returns>The keys written, as <c>key=value</c>.</returns>
  /// <exception cref="StepFailure">There is no block entity, a key is not in its tree, or a
  /// key's type cannot be written.</exception>
  public static string WriteTree(
    ScenarioContext ctx,
    BlockPos local,
    JObject values
  ) {
    BlockEntity be = EntityAt(ctx, local);
    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);
    foreach (JProperty property in values.Properties()) {
      string key = property.Name;
      JToken v = property.Value;
      switch (tree[key]) {
        case null:
          throw new StepFailure(
            $"{be.GetType().Name} at {FailureDetail.Cell(local)} has no tree key \"{key}\".",
            Suspect.Scenario
          );
        case IntAttribute:
          tree.SetInt(key, (int)v);
          break;
        case LongAttribute:
          tree.SetLong(key, (long)v);
          break;
        case FloatAttribute:
          tree.SetFloat(key, (float)v);
          break;
        case DoubleAttribute:
          tree.SetDouble(key, (double)v);
          break;
        case BoolAttribute:
          tree.SetBool(key, (bool)v);
          break;
        case StringAttribute:
          tree.SetString(key, (string)v!);
          break;
        case { } other:
          throw new StepFailure(
            $"tree key \"{key}\" holds {other.GetType().Name}, which set cannot write.",
            Suspect.Scenario
          );
      }
    }
    be.FromTreeAttributes(tree, ctx.World);
    be.MarkDirty();
    return string.Join(
      ", ",
      values.Properties().Select(p => $"{p.Name}={p.Value}")
    );
  }

  public static BlockEntity EntityAt(ScenarioContext ctx, BlockPos local) =>
    ctx.Setup.BlockEntityAt(local)
    ?? throw new StepFailure(
      $"no block entity at {FailureDetail.Cell(local)}.",
      Suspect.Scenario
    );
}
