using System.Linq;
using ExpandedLib.Industry.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// The hold of every crowbar verb, timed as vanilla's crowbar on a support beam: <see cref="HoldSeconds"/>
/// (0.1 s in Creative mode), acting on release after half a second. Blocks call it from their interact overrides.
/// </summary>
public static class CrowbarHold {
  /// <summary>Seconds a survival player holds the bar before the verb acts.</summary>
  public const float HoldSeconds = 1f;

  private const float CreativeHoldSeconds = 0.1f;
  private const float MinStopSeconds = 0.5f;

  /// <summary>True when <paramref name="stack"/> is a crowbar.</summary>
  public static bool IsCrowbar(ItemStack? stack) =>
    MoltenChisel.IsTool(stack, EnumTool.Crowbar);

  /// <summary>True when the player holds a crowbar in the active hand.</summary>
  public static bool IsHeldBy(IPlayer byPlayer) =>
    IsCrowbar(byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack);

  /// <summary>True when the player holds a crowbar and the land claims at the selection let the player build
  /// or break there.</summary>
  public static bool Claim(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) =>
    IsHeldBy(byPlayer)
    && world.Claims.TryAccess(
      byPlayer,
      blockSel.Position,
      EnumBlockAccessFlags.BuildOrBreak
    );

  /// <summary>Whether the hold goes on: true until <see cref="HoldSeconds"/> have passed, 0.1 s in Creative
  /// mode.</summary>
  public static bool Holding(float secondsUsed, IPlayer byPlayer) =>
    secondsUsed
    < (
      byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative
        ? CreativeHoldSeconds
        : HoldSeconds
    );

  /// <summary>Whether a release after <paramref name="secondsUsed"/> acts: after more than half a second,
  /// or at once in Creative mode.</summary>
  public static bool Released(float secondsUsed, IPlayer byPlayer) =>
    secondsUsed > MinStopSeconds
    || byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative;

  /// <summary>Plays <paramref name="sound"/> at <paramref name="pos"/> to the players in range. Server only.</summary>
  public static void PlayBreak(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound
  ) => ExSounds.Play(world.Api, pos, sound, 0.8f);

  private static ItemStack[]? _crowbarStacks;

  /// <summary>Forgets the resolved crowbar stacks; run when a world starts loading.</summary>
  internal static void ResetForWorld() => _crowbarStacks = null;

  /// <summary>The crowbar interaction hint with the action <paramref name="langCode"/>, advertising every crowbar item.</summary>
  public static WorldInteraction Help(IWorldAccessor world, string langCode) =>
    new() {
      ActionLangCode = langCode,
      MouseButton = EnumMouseButton.Right,
      Itemstacks = _crowbarStacks ??=
        [
          .. world
            .Collectibles.Where(c => c.Tool == EnumTool.Crowbar)
            .Select(c => new ItemStack(c)),
        ],
    };
}
