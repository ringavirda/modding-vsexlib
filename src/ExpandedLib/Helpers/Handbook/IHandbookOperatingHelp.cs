using Vintagestory.API.Client;

namespace ExpandedLib.Helpers;

/// <summary>Implemented by a block, or one of its block behaviours, to state in its handbook page
/// how the player operates it. Found through <c>block.GetCollectibleInterface</c>.</summary>
public interface IHandbookOperatingHelp {
  /// <summary>The controls the Operating section of the block's handbook page lists, one line per
  /// interaction, in order. Called on the client each time the page is composed, with no world position
  /// or player, so it returns the interactions the block can ever offer, not those of a placed
  /// instance's state.</summary>
  /// <param name="capi">The client api of the player reading the page.</param>
  /// <returns>The controls to list: the action text, the mouse button and the hotkeys of each
  /// interaction are read, its item stacks are not. Empty for no Operating section.</returns>
  HandbookControl[] GetHandbookOperatingHelp(ICoreClientAPI capi);
}

/// <summary>One control of a machine and the interactions the player performs on it, as the
/// Operating section of its handbook page lists them.</summary>
/// <param name="NameLangCode">The lang code of the control's shown name, which prefixes each of
/// its lines. Null when the control has no settled name: its lines carry no prefix.</param>
/// <param name="Interactions">The interactions of the control, listed in order.</param>
public sealed record HandbookControl(
  string? NameLangCode,
  WorldInteraction[] Interactions
);
