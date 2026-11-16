using System.Collections.Generic;
using ExpandedLib.Assembling;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>assemble</c>: the scenario player fixes an opted-in part into a cell by hand assembly,
/// stepping through the candidates with <c>steps</c> right-clicks first, or expects the server's
/// refusal named by <c>fails</c>.</summary>
internal sealed class AssembleStep : ScenarioStep {
  public override string Kind => "assemble";

  public override string What(SetupStep step) =>
    $"{step.Body["code"]} at {step.Body["at"]?.ToString(Formatting.None)}";

  public override string? Refuse(SetupStep step) =>
    (int?)step.Body["steps"] < 0 ? "\"steps\" is 0 or more." : null;

  /// <summary>The face a click lands on, from its letter; up when <paramref name="letter"/> is
  /// null.</summary>
  /// <exception cref="StepFailure">The letter is not one of n, e, s, w, u, d.</exception>
  public static BlockFacing Face(string? letter) =>
    letter == null
      ? BlockFacing.UP
      : SetupLayout.FaceOf(letter)
        ?? throw new StepFailure(
          $"face \"{letter}\" is not one of n, e, s, w, u, d.",
          Suspect.Scenario
        );

  /// <summary>Fixes <paramref name="block"/> into the plot-local <paramref name="local"/> by the
  /// scenario player; on success the block is drawn from the pack and the cell counts as placed.</summary>
  /// <param name="face">The face the click lands on.</param>
  /// <param name="pick">Which candidate to fix.</param>
  /// <param name="code">The refusal's code, or null when the part was fixed.</param>
  /// <returns>Null when the part was fixed; otherwise the refusal's line.</returns>
  /// <exception cref="StepFailure">The cell holds a block other than air at or above y 0 or in the file's stated ground, or lies outside the world.</exception>
  public static string? Fix(
    ScenarioContext ctx,
    ScenarioPlayer player,
    Block block,
    string blockCode,
    BlockPos local,
    BlockFacing face,
    AssemblyPick pick,
    out string? code
  ) {
    if (ctx.Holds(local))
      throw new StepFailure(
        $"{FailureDetail.Cell(local)} holds {ctx.Setup.BlockAt(local).Code}, not air.",
        Suspect.Scenario
      );
    ctx.RequireInWorld(local);
    if (
      !player.Assemble(
        ctx,
        block,
        ctx.Setup.Absolute(local),
        face,
        pick,
        out string? failure,
        out code
      )
    )
      return failure;
    PackLedger.DrawBlock(ctx, blockCode, local, local.ToString(), local);
    ctx.Placed.Add(local.Copy());
    ctx.Touched.Add(local.Copy());
    return null;
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
        "\"assemble\" needs the scenario player.",
        Suspect.Scenario
      );
    string blockCode = (string)o["code"]!;
    Block block = ctx.BlockOf(blockCode);
    BlockPos local = ctx.Position(step, o["at"], "at");
    var pick = new AssemblyPick(
      Arms: (string?)o["arms"],
      Turn: (string?)o["turn"],
      Steps: (int?)o["steps"] ?? 0
    );
    string? failure = Fix(
      ctx,
      player,
      block,
      blockCode,
      local,
      Face((string?)o["face"]),
      pick,
      out string? code
    );
    string? fails = (string?)o["fails"];
    if (fails != null) {
      if (failure == null)
        throw new StepFailure(
          $"{block.Code} was fixed at {FailureDetail.Cell(local)}; the step expected the refusal {fails}.",
          Suspect.Mod
        );
      if (code != fails)
        throw new StepFailure(
          $"{block.Code} at {FailureDetail.Cell(local)} was refused with {failure}; the step expected {fails}.",
          Suspect.Mod
        );
      report.Value = $"refused: {failure}";
      yield break;
    }
    if (failure != null)
      throw new StepFailure(
        $"the player could not fix {block.Code} at {FailureDetail.Cell(local)}: {failure}.",
        Suspect.Mod
      );
    ctx.Player?.RaisePlacedInBeltPath(ctx.Setup.Absolute(local));
    if (step.Id != null)
      ctx.Setup.Place(
        step.Id,
        new SetupPlacement(
          local.Copy(),
          [local.Copy()],
          ctx.Setup.FrameAt(local)
        )
      );
    report.Value =
      $"{ctx.Setup.BlockAt(local).Code} at plot {FailureDetail.Cell(local)}";
  }
}
