using System.Collections.Generic;
using System.Globalization;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing.Live;

/// <summary><c>ignite</c>: lights the block the way a held torch does. Each runner tick asks
/// <c>IIgnitable.OnTryIgniteBlock</c> with the seconds held so far, until it answers <c>IgniteNow</c>, which
/// is followed by one <c>OnTryIgniteBlockOver</c>.</summary>
internal sealed class IgniteStep : ScenarioStep {
  public override string Kind => "ignite";

  public override string What(SetupStep step) =>
    step.Body["at"]?.ToString(Formatting.None) ?? "";

  /// <summary>What an ignition answer means at <paramref name="held"/> seconds held against
  /// <paramref name="seconds"/>.</summary>
  public static IgniteVerdict Verdict(
    EnumIgniteState state,
    double held,
    double seconds
  ) =>
    state switch {
      EnumIgniteState.IgniteNow => IgniteVerdict.Lit,
      EnumIgniteState.NotIgnitable
      or EnumIgniteState.NotIgnitablePreventDefault => IgniteVerdict.Refused,
      _ => held >= seconds ? IgniteVerdict.TimedOut : IgniteVerdict.Wait,
    };

  /// <summary>What one ignition answer does to the step.</summary>
  public enum IgniteVerdict {
    Wait,
    Lit,
    Refused,
    TimedOut,
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
        "\"ignite\" needs the scenario player.",
        Suspect.Scenario
      );
    BlockPos local = ctx.Position(step, o["at"], "at");
    BlockPos at = ctx.Setup.Absolute(local);
    string code = ctx.Setup.BlockAt(local).Code.ToString();
    IIgnitable ignitable =
      ctx.World.BlockAccessor.GetBlock(at)
        .GetInterface<IIgnitable>(ctx.World, at)
      ?? throw new StepFailure($"{code} is not ignitable.", Suspect.Scenario);
    double seconds = (double?)o["seconds"] ?? 5;
    EntityAgent entity = player.Player.Entity;
    ctx.Touched.Add(local.Copy());
    double started = ctx.Now;
    while (true) {
      double held = ctx.Now - started;
      EnumIgniteState state = ignitable.OnTryIgniteBlock(
        entity,
        at,
        (float)held
      );
      switch (Verdict(state, held, seconds)) {
        case IgniteVerdict.Lit:
          EnumHandling handling = EnumHandling.PassThrough;
          ignitable.OnTryIgniteBlockOver(entity, at, (float)held, ref handling);
          report.Value =
            $"lit at {held.ToString("0.0", CultureInfo.InvariantCulture)} s";
          yield break;
        case IgniteVerdict.Refused:
          throw new StepFailure(
            $"{code} refused ignition at {held.ToString("0.0", CultureInfo.InvariantCulture)} s ({state})."
          );
        case IgniteVerdict.TimedOut:
          throw new StepFailure($"{code} was not lit within {seconds} s.");
      }
      yield return true;
    }
  }
}
