#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
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

  /// <summary>The feet position the last <c>stand</c> step set, or null when none ran.</summary>
  private Vec3d? _stood;

  /// <summary>Runs a hand assembly gesture on <paramref name="at"/> with the player where the live
  /// runner puts it: at the last <c>stand</c> step's spot when one ran, else at (x + 0.5, y, z + 2.5)
  /// of the cell. The player's position is restored afterwards.</summary>
  private AssemblyResult? StandingBeside(
    BlockPos at,
    Func<AssemblyResult?> gesture
  )
  {
    Vec3d was = Player.Entity.Pos.XYZ;
    Vec3d spot = _stood ?? new Vec3d(at.X + 0.5, at.Y, at.Z + 2.5);
    Player.Entity.Pos.SetPos(spot.X, spot.Y, spot.Z);
    try
    {
      return gesture();
    }
    finally
    {
      Player.Entity.Pos.SetPos(was.X, was.Y, was.Z);
    }
  }

  /// <summary>The step's <c>hit</c>, sixteenths across the aimed cell, as blocks from its corner; null when it names none.</summary>
  private static Vec3d? HitOf(SetupStep step) =>
    step.Body["hit"] is JArray point
      ? new Vec3d(
        (double)point[0] / 16,
        (double)point[1] / 16,
        (double)point[2] / 16
      )
      : null;

  /// <summary>The <c>takeoff</c> step: the player takes the fixed piece off with its tool in the
  /// main hand and the sneak held, and the pack gets back what came to the player.</summary>
  private void TakeOffStep(SetupStep step)
  {
    BlockPos at = At(step, "at");
    Vec3d? hit = HitOf(step);
    IBlockAccessor accessor = World.World.BlockAccessor;
    BlockPos principal = HandFitAim.PrincipalOf(accessor, at);
    Block block = World.GetBlock(principal);
    HandAssemblySpec spec =
      HandAssemblySpec.Of(block)
      ?? throw Fail(
        step.Path,
        $"{block.Code} at {Describe(at)} does not declare handAssembly, so nothing takes it off."
      );
    if (
      block is IHandFitCellTarget target
      && target.FittedAt(
        accessor,
        principal,
        new HandFitPoint(at, hit ?? new Vec3d(0.5, 0.5, 0.5))
      )
        is { Tool: { } tool }
    )
      spec = spec with { Tool = tool };
    Dictionary<string, int> before = ExCarried.Totals(Player.Player);
    Player.Hold(ToolFor(step.Path, spec));
    bool sneaking = Player.Sneaking;
    Player.Sneaking = true;
    AssemblyResult? refused;
    try
    {
      refused = StandingBeside(
        at,
        () =>
          HandAssemblyServer.TakeOff(
            World.World,
            Player.Player,
            new AssemblyTakeOff
            {
              X = at.X,
              Y = at.Y,
              Z = at.Z,
              Dimension = at.dimension,
              HitX = hit?.X ?? 0.5,
              HitY = hit?.Y ?? 0.5,
              HitZ = hit?.Z ?? 0.5,
            }
          )
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

  /// <summary>The <c>cap</c> step: on an uncapped end of a fixed piece the player fits the def's cap
  /// held in the main hand with the wrench in the off hand, as a sneak click sends it; on a
  /// capped end the player clicks it with the wrench in the main hand and takes the cap off. The
  /// step's <c>fails</c> names the refusal the server must answer.</summary>
  private void CapStep(SetupStep step)
  {
    BlockPos at = At(step, "at");
    Block block = World.GetBlock(at);
    HandAssemblySpec spec =
      HandAssemblySpec.Of(block)
      ?? throw Fail(
        step.Path,
        $"{block.Code} at {Describe(at)} does not declare handAssembly."
      );
    BlockFacing face = FaceOf(step, (string?)step.Body["face"]);
    bool capped =
      BEBehaviorAssembled.At(World.World.BlockAccessor, at)?.CapAt(face)
      != null;
    if (!capped && spec.Cap == null)
      throw Fail(step.Path, $"{block.Code} at {Describe(at)} declares no cap.");
    ItemStack wrenchStack = ToolFor(step.Path, spec);
    ItemStack? cap = null;
    if (capped)
      Player.Hold(wrenchStack);
    else
    {
      cap = ToolFor(step.Path, spec with { Tool = spec.Cap! });
      Player.Hold(cap);
      Player.HoldOffHand(wrenchStack);
      Supply(step.Path, spec, spec.PerJoint);
    }
    Dictionary<string, int> before = ExCarried.Totals(Player.Player);
    bool sneaking = Player.Sneaking;
    Player.Sneaking = false;
    AssemblyResult? refused;
    try
    {
      refused = StandingBeside(
        at,
        () =>
          capped
            ? HandAssemblyServer.Cap(
              World.World,
              Player.Player,
              at,
              new Vec3d(
                0.5 + 0.5 * face.Normali.X,
                0.5 + 0.5 * face.Normali.Y,
                0.5 + 0.5 * face.Normali.Z
              )
            )
            : HandAssemblyServer.FitCap(
              World.World,
              Player.Player,
              new AssemblyCap
              {
                X = at.X,
                Y = at.Y,
                Z = at.Z,
                Dimension = at.dimension,
                Face = face.Index,
                Slot = Player.Player.InventoryManager.ActiveHotbarSlotNumber,
                CapCode = cap!.Collectible.Code.ToString(),
              }
            )
      );
      ItemSlot wrench = Player.ActiveSlot;
      if (capped && !HandAssemblyGate.IsCreative(Player.Player))
        wrench.Itemstack.Collectible.DamageItem(
          World.World,
          Player.Player.Entity,
          wrench
        );
    }
    finally
    {
      Player.Sneaking = sneaking;
      Give(Player.ActiveSlot.Itemstack);
      Give(Player.OffHandSlot.Itemstack);
      Player.Hold(null);
      Player.HoldOffHand(null);
      foreach ((string code, int now) in ExCarried.Totals(Player.Player))
      {
        int rise = now - before.GetValueOrDefault(code);
        if (rise > 0 && ExCarried.Take(Player.Player, code, rise) is { } taken)
          _pack?.Give(code, taken.StackSize);
      }
    }
    string? fails = (string?)step.Body["fails"];
    if (fails != null)
    {
      if (refused == null)
        throw Fail(
          step.Path,
          $"{block.Code} at {Describe(at)} took the click on face {face.Code}; the step expected the refusal {fails}."
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
        $"the player could not cap {block.Code} at {Describe(at)} on face {face.Code}: {Refusal(refused)}."
      );
  }

  /// <summary>The <c>fit</c> step: the player fits the step's <c>stack</c> onto the placed block at <c>at</c>
  /// as a sneak click sends it, aimed at the step's <c>hit</c> (sixteenths across the cell; the centre without
  /// it), with the principal's fasteners and the plan's companions drawn from the pack.</summary>
  private void FitStep(SetupStep step)
  {
    BlockPos at = At(step, "at");
    Vec3d? hit = HitOf(step);
    Block block = World.GetBlock(
      HandFitAim.PrincipalOf(World.World.BlockAccessor, at)
    );
    HandAssemblySpec spec =
      HandAssemblySpec.Of(block)
      ?? throw Fail(
        step.Path,
        $"{block.Code} at {Describe(at)} does not declare handAssembly."
      );
    ItemStack item = StackOf(step.Body, step.Path);
    Draw(step.Path, item);
    Player.Hold(item);
    Player.HoldOffHand(ToolFor(step.Path, spec));
    HandFitPlan? plan = HandFitAim
      .OfCell(World.World.BlockAccessor, at, hit, item, out _)
      ?.Plan;
    Supply(step.Path, spec, plan?.Fasteners ?? spec.PerFit);
    foreach (HandFitCompanion companion in plan?.Companions ?? [])
      SupplyCompanion(step.Path, companion, item);
    Dictionary<string, int> before = ExCarried.Totals(Player.Player);
    bool sneaking = Player.Sneaking;
    Player.Sneaking = true;
    AssemblyResult? refused;
    try
    {
      refused = StandingBeside(
        at,
        () =>
          HandAssemblyServer.Fit(
            World.World,
            Player.Player,
            new AssemblyFit
            {
              X = at.X,
              Y = at.Y,
              Z = at.Z,
              Dimension = at.dimension,
              Slot = Player.Player.InventoryManager.ActiveHotbarSlotNumber,
              ItemCode = item.Collectible.Code.ToString(),
              HitX = hit?.X ?? 0.5,
              HitY = hit?.Y ?? 0.5,
              HitZ = hit?.Z ?? 0.5,
            }
          )
      );
    }
    finally
    {
      Player.Sneaking = sneaking;
      Give(Player.ActiveSlot.Itemstack);
      Give(Player.OffHandSlot.Itemstack);
      Player.Hold(null);
      Player.HoldOffHand(null);
      foreach ((string code, int now) in ExCarried.Totals(Player.Player))
      {
        int rise = now - before.GetValueOrDefault(code);
        if (rise > 0 && ExCarried.Take(Player.Player, code, rise) is { } taken)
          _pack?.Give(code, taken.StackSize);
      }
    }
    string? fails = (string?)step.Body["fails"];
    if (fails != null)
    {
      if (refused == null)
        throw Fail(
          step.Path,
          $"{block.Code} at {Describe(at)} took the fit; the step expected the refusal {fails}."
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
        $"the player could not fit {item.Collectible.Code} onto {block.Code} at {Describe(at)}: {Refusal(refused)}."
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
  /// <returns>Null when the part was fixed; otherwise the server's refusal, the part's own refusal when it
  /// filtered every placement (<see cref="HandAssemblyCandidates.EmptyRefusal"/>), or one with code
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
      var selection = new BlockSelection
      {
        Position = aimed,
        Face = face,
        HitPosition = hit,
      };
      IReadOnlyList<AssemblyCandidate> offered = HandAssemblyCandidates.Of(
        World.World,
        Player.Player,
        part,
        selection
      );
      if (
        offered.Count == 0
        && HandAssemblyCandidates.EmptyRefusal(
          World.World,
          Player.Player,
          part,
          selection
        )
          is { } named
      )
        return HandAssemblyServer.Refusal(named);
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
      return StandingBeside(
        aimed,
        () =>
          HandAssemblyServer.Fix(
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
          )
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

  private void Supply(string where, HandAssemblySpec spec, int cost) =>
    SupplyItem(
      where,
      spec.Fastener,
      cost - ExCarried.Count(Player.Player, spec.Fastener)
    );

  /// <summary>Puts into the pack what a fit's companion needs beyond what is carried, counting the held
  /// item when the companion's code matches it.</summary>
  private void SupplyCompanion(
    string where,
    HandFitCompanion companion,
    ItemStack held
  )
  {
    if (companion.Code.Contains('*'))
      throw Fail(
        where,
        $"the fit's companion {companion.Code} is a wildcard; a setup can supply only a named item."
      );
    int need =
      companion.Count
      + (companion.Code == held.Collectible.Code.ToString() ? 1 : 0);
    SupplyItem(
      where,
      companion.Code,
      need - ExCarried.Count(Player.Player, companion.Code)
    );
  }

  private void SupplyItem(string where, string code, int missing)
  {
    if (missing <= 0)
      return;
    Item fastener =
      World.World.GetItem(new AssetLocation(code))
      ?? throw Fail(where, $"{code} is not a loaded item.");
    if (_pack is { DrawsBlocks: true })
      Draw(where, code, missing);
    else if (_granted.Add(code))
      _standIns.Add($"granted {missing} {code} for hand assembly");
    if (
      !Player.Player.InventoryManager.TryGiveItemstack(
        new ItemStack(fastener, missing),
        true
      )
    )
      throw Fail(where, $"the player has no room for {missing} {code}.");
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
