using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Creative;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary><c>power</c> for an <c>mpshaft</c> run: the creative mpenergy source beside the cell at
/// <c>at</c>, set to <c>torque</c> (N*m at rest) and <c>speed</c> (rad/s at which it delivers nothing). The
/// source is placed on <c>face</c>, or on the first face in the order n, e, s, w, u, d where the cell
/// has a crossing and the cell across is air; a source already beside the cell is only set.</summary>
internal sealed class PowerStep : ScenarioStep {
  public override string Kind => "power";

  public override string What(SetupStep step) =>
    $"{step.Body["torque"]} N*m to {step.Body["speed"]} rad/s";

  public override string? Refuse(SetupStep step) {
    var o = (JObject)step.Body;
    if ((string?)o["network"] != "mpshaft")
      return "only an \"mpshaft\" run is driven by the creative mpenergy source.";
    if (!IsNumber(o["torque"]) || (float)o["torque"]! < 0f)
      return "\"torque\" is a number, 0 or more.";
    if (!IsNumber(o["speed"]) || (float)o["speed"]! <= 0f)
      return "\"speed\" is above 0 rad/s.";
    if (o["face"] != null && Face((string?)o["face"]) == null)
      return $"\"{o["face"]}\" is not a face.";
    return null;
  }

  private static bool IsNumber(JToken? token) =>
    token is { Type: JTokenType.Integer or JTokenType.Float };

  private static BlockFacing? Face(string? token) =>
    token == null ? null : ExOrientation.FacingFromSide(token);

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    BlockPos cell = ctx.Position(step, o["at"], "at");
    BlockFacing? asked = Face((string?)o["face"]);
    BlockPos? source = Source(ctx, cell, asked);
    bool placed = source == null;
    if (source == null) {
      BlockFacing face = asked ?? OpenFace(ctx, cell);
      if (asked != null && !HasCrossing(Member(ctx, cell), ctx, cell, asked))
        throw new StepFailure(
          $"{FailureDetail.Cell(cell)} has no crossing on face {asked.Code}.",
          Suspect.Scenario
        );
      source = Beside(cell, face);
      if (!ctx.IsAir(source))
        throw new StepFailure(
          $"{FailureDetail.Cell(source)} holds {ctx.Setup.BlockAt(source).Code}, not air.",
          Suspect.Scenario
        );
      ctx.SetBlock(ctx.BlockOf(BlockCreativeMpEnergy.Code), source);
      yield return true;
    }
    SetStep.WriteTree(
      ctx,
      source,
      new JObject { ["torque"] = o["torque"], ["speed"] = o["speed"] }
    );
    report.Value =
      $"{(placed ? "placed" : "set")} {FailureDetail.Cell(source)}: {What(step)}";
  }

  private static BlockPos Beside(BlockPos cell, BlockFacing face) =>
    new(
      cell.X + face.Normali.X,
      cell.Y + face.Normali.Y,
      cell.Z + face.Normali.Z
    );

  // The creative source across the asked face, or across any face when none was asked.
  private static BlockPos? Source(
    ScenarioContext ctx,
    BlockPos cell,
    BlockFacing? asked
  ) {
    IEnumerable<BlockFacing> faces =
      asked != null ? [asked] : BlockFacing.ALLFACES;
    return faces
      .Select(f => Beside(cell, f))
      .FirstOrDefault(p =>
        ctx.Setup.BlockAt(p).Code?.ToString() == BlockCreativeMpEnergy.Code
      );
  }

  private static INetworkMember Member(ScenarioContext ctx, BlockPos cell) =>
    NetworkMembership.Resolve(
      ctx.World.BlockAccessor,
      ctx.Setup.Absolute(cell),
      "mpshaft"
    )
    ?? throw new StepFailure(
      $"{FailureDetail.Cell(cell)} is not a member of an mpshaft run.",
      Suspect.Scenario
    );

  private static bool HasCrossing(
    INetworkMember member,
    ScenarioContext ctx,
    BlockPos cell,
    BlockFacing face
  ) =>
    member
      .Crossings(
        ctx.World.BlockAccessor,
        NodeKey.Centre(ctx.Setup.Absolute(cell))
      )
      .Contains(new NodeCrossing(face));

  private static BlockFacing OpenFace(ScenarioContext ctx, BlockPos cell) {
    INetworkMember member = Member(ctx, cell);
    foreach (BlockFacing face in Order)
      if (HasCrossing(member, ctx, cell, face) && ctx.IsAir(Beside(cell, face)))
        return face;
    throw new StepFailure(
      $"{FailureDetail.Cell(cell)} has no open face to put the source on.",
      Suspect.Scenario
    );
  }

  private static readonly BlockFacing[] Order =
  [
    BlockFacing.NORTH,
    BlockFacing.EAST,
    BlockFacing.SOUTH,
    BlockFacing.WEST,
    BlockFacing.UP,
    BlockFacing.DOWN,
  ];
}
