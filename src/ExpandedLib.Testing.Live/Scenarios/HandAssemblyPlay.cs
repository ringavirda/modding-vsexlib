using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Live;

/// <summary>The scenario player's hand assembly: a held part with its tool in the off hand fixed
/// through the server's own handler, and a fixed piece taken off with the tool in the main hand.
/// The tool is drawn from the case's pack; the fasteners are drawn from it under
/// <c>scope: "all"</c> and granted otherwise.</summary>
internal static class HandAssemblyPlay {
  /// <summary>The refusal code of a click that offers no candidate the pick names.</summary>
  public const string NoCandidate = AssemblyPick.NoCandidate;

  /// <summary>The refusal as a failure line: its code and arguments.</summary>
  public static string Describe(AssemblyResult result) =>
    string.Join(" ", [result.Code, .. result.Args]);

  /// <summary>Holds <paramref name="part"/> with its tool and the fasteners of the chosen candidate,
  /// then fixes it at <paramref name="at"/> through <see cref="HandAssemblyServer.Fix"/> as the
  /// client's packet would. The active slot and the off hand are empty again afterwards.</summary>
  /// <param name="ctx">The running case, whose pack the tool and fasteners come from.</param>
  /// <param name="world">The server world.</param>
  /// <param name="player">The placing player, in survival mode and standing in reach of the cell.</param>
  /// <param name="part">The opted-in block to hold.</param>
  /// <param name="at">The absolute cell the part goes into.</param>
  /// <param name="face">The face the click lands on.</param>
  /// <param name="hit">The hit point on that face.</param>
  /// <param name="pick">Which candidate to fix.</param>
  /// <returns>Null when the part was fixed; otherwise the server's refusal, or <see cref="NoCandidate"/>.</returns>
  /// <exception cref="StepFailure">The block is not opted in, the pack holds no tool or too few fasteners, or the fastener is not a loaded item.</exception>
  public static AssemblyResult? Place(
    ScenarioContext ctx,
    IWorldAccessor world,
    IPlayer player,
    Block part,
    BlockPos at,
    BlockFacing face,
    Vec3d hit,
    AssemblyPick pick
  ) {
    HandAssemblySpec spec =
      HandAssemblySpec.Of(part)
      ?? throw new StepFailure(
        $"{part.Code} does not declare handAssembly.",
        Suspect.Scenario
      );
    ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
    ItemSlot offhand = player.Entity.LeftHandItemSlot;
    slot.Itemstack = new ItemStack(part);
    offhand.Itemstack = PackLedger.Tool(
      ctx,
      $"hand assembly of {part.Code}",
      spec.Tool
    );
    try {
      BlockPos aimed = Aimed(world, part, at, face);
      var selection = new BlockSelection {
        Position = aimed,
        Face = face,
        HitPosition = hit,
      };
      IReadOnlyList<AssemblyCandidate> offered = HandAssemblyCandidates.Of(
        world,
        player,
        part,
        selection
      );
      if (pick.Choose(offered) is not { } chosen)
        return new AssemblyResult {
          Code = NoCandidate,
          Args =
          [
            part.Code.ToString(),
            string.Join(", ", offered.Select(c => c.Block.Code.ToString())),
          ],
        };
      Supply(ctx, world, player, spec, chosen.Joints * spec.PerJoint);
      return HandAssemblyServer.Fix(
        world,
        player,
        new AssemblyFix {
          X = aimed.X,
          Y = aimed.Y,
          Z = aimed.Z,
          Dimension = aimed.dimension,
          Face = face.Index,
          HitX = hit.X,
          HitY = hit.Y,
          HitZ = hit.Z,
          Slot = player.InventoryManager.ActiveHotbarSlotNumber,
          PartCode = part.Code.ToString(),
          CandidateCode = chosen.Block.Code.ToString(),
        }
      );
    } finally {
      PackLedger.Give(ctx, offhand.Itemstack);
      offhand.Itemstack = null;
      slot.Itemstack = null;
    }
  }

  /// <summary>Takes the fixed piece at <paramref name="at"/> off with its tool in the main hand and
  /// the sneak held, as the client's packet would, and gives the pack what came back to the
  /// player.</summary>
  /// <param name="ctx">The running case, whose pack the tool comes from and the returns go to.</param>
  /// <param name="world">The server world.</param>
  /// <param name="player">The player, in survival mode and standing in reach of the cell.</param>
  /// <param name="at">The absolute cell of the piece.</param>
  /// <returns>Null when the piece came off; otherwise the server's refusal.</returns>
  /// <exception cref="StepFailure">The cell's block does not declare handAssembly or the pack holds no tool.</exception>
  public static AssemblyResult? TakeOff(
    ScenarioContext ctx,
    IWorldAccessor world,
    IPlayer player,
    BlockPos at
  ) {
    Block block = world.BlockAccessor.GetBlock(at);
    HandAssemblySpec spec =
      HandAssemblySpec.Of(block)
      ?? throw new StepFailure(
        $"{block.Code} does not declare handAssembly, so nothing takes it off.",
        Suspect.Scenario
      );
    ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
    Dictionary<string, int> before = ExCarried.Totals(player);
    slot.Itemstack = PackLedger.Tool(
      ctx,
      $"hand assembly take-off of {block.Code}",
      spec.Tool
    );
    bool sneaking = player.Entity.Controls.Sneak;
    player.Entity.Controls.Sneak = true;
    try {
      return HandAssemblyServer.TakeOff(
        world,
        player,
        new AssemblyTakeOff {
          X = at.X,
          Y = at.Y,
          Z = at.Z,
          Dimension = at.dimension,
        }
      );
    } finally {
      player.Entity.Controls.Sneak = sneaking;
      PackLedger.Give(ctx, slot.Itemstack);
      slot.Itemstack = null;
      ReturnToPack(ctx, player, before);
    }
  }

  /// <summary>Clicks the <paramref name="face"/> crossing of the fixed piece at <paramref name="at"/>
  /// with its tool in the main hand and no sneak, as the wrench does: the crossing's cap is taken off,
  /// or an uncapped one gets the def's cap from the off hand with the fasteners of one joint. The tool
  /// and cap are drawn from the case's pack and what came back to the player goes to it.</summary>
  /// <param name="ctx">The running case, whose pack the tool, cap and fasteners come from.</param>
  /// <param name="world">The server world.</param>
  /// <param name="player">The player, in survival mode and standing in reach of the cell.</param>
  /// <param name="at">The absolute cell of the piece.</param>
  /// <param name="face">The crossing the click lands on.</param>
  /// <returns>Null when a cap was fitted or taken off; otherwise the server's refusal.</returns>
  /// <exception cref="StepFailure">The cell's block does not declare handAssembly, a fit is asked of a def with no cap, or the pack holds no tool or cap.</exception>
  public static AssemblyResult? Cap(
    ScenarioContext ctx,
    IWorldAccessor world,
    IPlayer player,
    BlockPos at,
    BlockFacing face
  ) {
    Block block = world.BlockAccessor.GetBlock(at);
    HandAssemblySpec spec =
      HandAssemblySpec.Of(block)
      ?? throw new StepFailure(
        $"{block.Code} does not declare handAssembly, so nothing caps it.",
        Suspect.Scenario
      );
    bool capped =
      BEBehaviorAssembled.At(world.BlockAccessor, at)?.CapAt(face) != null;
    if (!capped && spec.Cap == null)
      throw new StepFailure($"{block.Code} declares no cap.", Suspect.Scenario);
    ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
    ItemSlot offhand = player.Entity.LeftHandItemSlot;
    string label = $"hand assembly cap of {block.Code}";
    slot.Itemstack = PackLedger.Tool(ctx, label, spec.Tool);
    if (!capped) {
      offhand.Itemstack = PackLedger.Tool(ctx, label, spec.Cap);
      Supply(ctx, world, player, spec, spec.PerJoint);
    }
    Dictionary<string, int> before = ExCarried.Totals(player);
    bool sneaking = player.Entity.Controls.Sneak;
    player.Entity.Controls.Sneak = false;
    try {
      AssemblyResult? refused = HandAssemblyServer.Cap(
        world,
        player,
        at,
        ScenarioPlayer.Hit(face)
      );
      if (
        refused == null
        && player.WorldData.CurrentGameMode != EnumGameMode.Creative
      )
        slot.Itemstack.Collectible.DamageItem(world, player.Entity, slot);
      return refused;
    } finally {
      player.Entity.Controls.Sneak = sneaking;
      PackLedger.Give(ctx, slot.Itemstack);
      PackLedger.Give(ctx, offhand.Itemstack);
      slot.Itemstack = null;
      offhand.Itemstack = null;
      ReturnToPack(ctx, player, before);
    }
  }

  private static BlockPos Aimed(
    IWorldAccessor world,
    Block part,
    BlockPos at,
    BlockFacing face
  ) {
    BlockPos behind = at.AddCopy(face.Opposite);
    return world.BlockAccessor.GetBlock(behind).IsReplacableBy(part)
      ? at.Copy()
      : behind;
  }

  private static void Supply(
    ScenarioContext ctx,
    IWorldAccessor world,
    IPlayer player,
    HandAssemblySpec spec,
    int cost
  ) {
    int missing = cost - ExCarried.Count(player, spec.Fastener);
    if (missing <= 0)
      return;
    Item fastener =
      world.GetItem(new AssetLocation(spec.Fastener))
      ?? throw new StepFailure(
        $"the fastener {spec.Fastener} is not a loaded item.",
        Suspect.Scenario
      );
    if (ctx.Pack is { DrawsBlocks: true })
      PackLedger.Draw(ctx, spec.Fastener, missing);
    else if (ctx.Granted.Add(spec.Fastener))
      ctx.Line($"granted {missing} {spec.Fastener} for hand assembly");
    if (
      !player.InventoryManager.TryGiveItemstack(
        new ItemStack(fastener, missing),
        true
      )
    )
      throw new StepFailure(
        $"the player has no room for {missing} {spec.Fastener}.",
        Suspect.Scenario
      );
  }

  private static void ReturnToPack(
    ScenarioContext ctx,
    IPlayer player,
    Dictionary<string, int> before
  ) {
    foreach ((string code, int now) in ExCarried.Totals(player)) {
      int rise = now - before.GetValueOrDefault(code);
      if (rise > 0 && ExCarried.Take(player, code, rise) is { } taken)
        ctx.Pack?.Give(code, taken.StackSize);
    }
  }
}
