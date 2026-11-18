using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace ExpandedLib.Assembling;

/// <summary>Stops the vanilla placement path from setting an opted-in block in survival, and sets its first candidate in creative.</summary>
internal static class HandAssemblyGate {
  /// <summary>Refuses a survival placement of an opted-in block outside the assembly handler, telling the player which tool the block needs.</summary>
  /// <param name="world">The world the block would be placed in.</param>
  /// <param name="player">The placing player.</param>
  /// <param name="itemstack">The held stack; a stack of a block with no <c>handAssembly</c> attribute is not refused.</param>
  /// <param name="failureCode">Set to <c>__ignore__</c> on a refusal, as the player is already told.</param>
  /// <returns>True when the placement is refused.</returns>
  public static bool Refuses(
    IWorldAccessor world,
    IPlayer player,
    ItemStack itemstack,
    ref string failureCode
  ) {
    if (
      itemstack.Block is not { } block
      || HandAssemblySpec.Of(block) is not { } spec
      || IsCreative(player)
    )
      return false;

    string code = HandAssemblyServer.NeedsTool;
    string? langCode = HandAssemblyServer.LanguageOf(player);
    string message = HandAssemblyServer.Line(
      langCode,
      code,
      HandAssemblyServer.ToolNoun(world, spec.Tool, langCode)
    );
    if (player is IServerPlayer serverPlayer)
      serverPlayer.SendIngameError(code, message);
    else if (world.Api is ICoreClientAPI capi)
      capi.TriggerIngameError(block, code, message);
    failureCode = "__ignore__";
    return true;
  }

  /// <summary>In creative, places the first candidate of the aim, fixed, with no fastener recorded.</summary>
  /// <param name="world">The world placed in.</param>
  /// <param name="player">The placing player.</param>
  /// <param name="itemstack">The held stack.</param>
  /// <param name="selection">The aim.</param>
  /// <param name="failureCode">Set to <c>exlib-handassembly-notoffered</c> when the aim offers no placement.</param>
  /// <param name="result">Whether the block was placed; meaningful only when this returns true.</param>
  /// <returns>True when the gate decided the placement (creative and an opted-in block), false when the caller places as it always did.</returns>
  public static bool PlacesFirst(
    IWorldAccessor world,
    IPlayer player,
    ItemStack itemstack,
    BlockSelection selection,
    ref string failureCode,
    out bool result
  ) {
    result = false;
    if (
      itemstack.Block is not { } block
      || HandAssemblySpec.Of(block) == null
      || !IsCreative(player)
    )
      return false;

    result = HandAssemblyServer.PlaceFirst(
      world,
      player,
      block,
      itemstack,
      selection,
      ref failureCode
    );
    return true;
  }

  internal static bool IsCreative(IPlayer player) =>
    player.WorldData.CurrentGameMode == EnumGameMode.Creative;
}
