#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private readonly HashSet<string> _granted = [];

  /// <summary>The <c>assemble</c> step: the player fixes the part into the cell by hand assembly,
  /// or the step's <c>fails</c> names the refusal the server must answer.</summary>
  private void AssembleStep(SetupStep step)
  {
    string code = (string)step.Body["code"]!;
    Block block = Resolve(code, step.Path);
    BlockPos at = At(step, "at");
    var pick = new AssemblyPick(
      Arms: (string?)step.Body["arms"],
      Turn: (string?)step.Body["turn"],
      Steps: (int?)step.Body["steps"] ?? 0
    );
    AssemblyResult? refused = Assemble(
      step.Path,
      block,
      at,
      FaceOf(step, (string?)step.Body["face"]),
      pick
    );
    string? fails = (string?)step.Body["fails"];
    if (fails != null)
    {
      if (refused == null)
        throw Fail(
          step.Path,
          $"{block.Code} was fixed at {Describe(at)}; the step expected the refusal {fails}."
        );
      if (refused.Code != fails)
        throw Fail(
          step.Path,
          $"{block.Code} at {Describe(at)} was refused with {Refusal(refused)}; the step expected {fails}."
        );
      return;
    }
    if (refused != null)
      throw Fail(
        step.Path,
        $"the player could not fix {block.Code} at {Describe(at)}: {Refusal(refused)}."
      );
    if (step.Id != null)
      Record(step.Id, at, World.GetBlock(at));
    RaisePlacedInBeltPath(at);
  }

  /// <summary>The <c>takeoff</c> step: the player takes the fixed piece off with its tool in the
  /// main hand and the sneak held, and the pack gets back what came to the player.</summary>
  private void TakeOffStep(SetupStep step)
  {
    BlockPos at = At(step, "at");
    Block block = World.GetBlock(at);
    HandAssemblySpec spec =
      HandAssemblySpec.Of(block)
      ?? throw Fail(
        step.Path,
        $"{block.Code} at {Describe(at)} does not declare handAssembly, so nothing takes it off."
      );
    Dictionary<string, int> before = ExCarried.Totals(Player.Player);
    Player.Hold(ToolFor(step.Path, spec));
    bool sneaking = Player.Sneaking;
    Player.Sneaking = true;
    AssemblyResult? refused;
    try
    {
      refused = HandAssemblyServer.TakeOff(
        World.World,
        Player.Player,
        new AssemblyTakeOff
        {
          X = at.X,
          Y = at.Y,
          Z = at.Z,
          Dimension = at.dimension,
        }
      );
    }
    finally
    {
      Player.Sneaking = sneaking;
      Give(Player.ActiveSlot.Itemstack);
      Player.Hold(null);
      foreach ((string code, int now) in ExCarried.Totals(Player.Player))
      {
        int rise = now - before.GetValueOrDefault(code);
        if (rise > 0 && ExCarried.Take(Player.Player, code, rise) is { } taken)
          _pack?.Give(code, taken.StackSize);
      }
    }
    if (refused != null)
      throw Fail(
        step.Path,
        $"the player could not take {block.Code} off at {Describe(at)}: {Refusal(refused)}."
      );
  }

  /// <summary>Lays <paramref name="block"/> into <paramref name="at"/> through
  /// <see cref="Assemble"/> as a <c>place</c> of an opted-in block and a run cell do, and draws it
  /// from the pack once the server has fixed it.</summary>
  private void PutByHand(
    Block block,
    BlockPos at,
    string where,
    AssemblyPick pick,
    BlockFacing face,
    string? kind = null,
    bool draws = true
  )
  {
    Block held = World.GetBlock(at);
    if (held.Id != 0)
      throw Fail(where, $"cell {Describe(at)} holds {held.Code}, not air.");
    _laid.Add((at.X, at.Y, at.Z));
    HashSet<long> before = World.TickListenerIds();
    if (Assemble(where, block, at, face, pick) is { } refused)
      throw Fail(
        where,
        $"the player could not fix {block.Code} at {Describe(at)}: {Refusal(refused)}."
      );
    if (draws)
      DrawBlock(where, kind ?? block.Code.ToString(), at, Describe(at), at);
    _listeners[at.Copy()] = [.. World.TickListenerIds().Except(before)];
  }

  /// <summary>Fixes <paramref name="part"/> into <paramref name="at"/> through
  /// <see cref="HandAssemblyServer.Fix"/> as the client's packet would: the part in the main hand,
  /// the tool in the off hand, the fasteners of the chosen candidate in the pack's draw or granted.
  /// The hands are empty afterwards.</summary>
  /// <returns>Null when the part was fixed; otherwise the server's refusal, or one with code
  /// <see cref="AssemblyPick.NoCandidate"/> when the pick leaves no candidate.</returns>
  private AssemblyResult? Assemble(
    string where,
    Block part,
    BlockPos at,
    BlockFacing face,
    AssemblyPick pick
  )
  {
    HandAssemblySpec spec =
      HandAssemblySpec.Of(part)
      ?? throw Fail(where, $"{part.Code} does not declare handAssembly.");
    Player.Hold(new ItemStack(part));
    Player.HoldOffHand(ToolFor(where, spec));
    try
    {
      BlockPos aimed = World
        .GetBlock(at.AddCopy(face.Opposite))
        .IsReplacableBy(part)
        ? at.Copy()
        : at.AddCopy(face.Opposite);
      Vec3d hit = new(
        0.5 + 0.5 * face.Normali.X,
        0.5 + 0.5 * face.Normali.Y,
        0.5 + 0.5 * face.Normali.Z
      );
      IReadOnlyList<AssemblyCandidate> offered = HandAssemblyCandidates.Of(
        World.World,
        Player.Player,
        part,
        new BlockSelection
        {
          Position = aimed,
          Face = face,
          HitPosition = hit,
        }
      );
      if (pick.Choose(offered) is not { } chosen)
        return new AssemblyResult
        {
          Code = AssemblyPick.NoCandidate,
          Args =
          [
            part.Code.ToString(),
            string.Join(", ", offered.Select(c => c.Block.Code.ToString())),
          ],
        };
      Supply(where, spec, chosen.Joints * spec.PerJoint);
      return HandAssemblyServer.Fix(
        World.World,
        Player.Player,
        new AssemblyFix
        {
          X = aimed.X,
          Y = aimed.Y,
          Z = aimed.Z,
          Dimension = aimed.dimension,
          Face = face.Index,
          HitX = hit.X,
          HitY = hit.Y,
          HitZ = hit.Z,
          Slot = Player.ActiveNumber,
          PartCode = part.Code.ToString(),
          CandidateCode = chosen.Block.Code.ToString(),
        }
      );
    }
    finally
    {
      Give(Player.OffHandSlot.Itemstack);
      Player.HoldOffHand(null);
      Player.Hold(null);
    }
  }

  private ItemStack ToolFor(string where, HandAssemblySpec spec)
  {
    ItemStack tool;
    string? drawn;
    try
    {
      tool = SetupTools.Held(World.World, spec.Tool, _pack, out drawn);
    }
    catch (InvalidOperationException e)
    {
      throw Fail(where, e.Message);
    }
    if (drawn != null)
      Draw(where, drawn, 1);
    return tool;
  }

  private void Supply(string where, HandAssemblySpec spec, int cost)
  {
    int missing = cost - ExCarried.Count(Player.Player, spec.Fastener);
    if (missing <= 0)
      return;
    Item fastener =
      World.World.GetItem(new AssetLocation(spec.Fastener))
      ?? throw Fail(
        where,
        $"the fastener {spec.Fastener} is not a loaded item."
      );
    if (_pack is { DrawsBlocks: true })
      Draw(where, spec.Fastener, missing);
    else if (_granted.Add(spec.Fastener))
      _standIns.Add($"granted {missing} {spec.Fastener} for hand assembly");
    if (
      !Player.Player.InventoryManager.TryGiveItemstack(
        new ItemStack(fastener, missing),
        true
      )
    )
      throw Fail(
        where,
        $"the player has no room for {missing} {spec.Fastener}."
      );
  }

  private BlockFacing FaceOf(SetupStep step, string? letter) =>
    letter == null
      ? BlockFacing.UP
      : SetupLayout.FaceOf(letter)
        ?? throw Fail(
          step.Path,
          $"face \"{letter}\" is not one of n, e, s, w, u, d."
        );

  private static string Refusal(AssemblyResult result) =>
    string.Join(" ", [result.Code, .. result.Args]);
}
#endif
