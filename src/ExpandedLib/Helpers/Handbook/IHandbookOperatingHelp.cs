using Vintagestory.API.Client;

namespace ExpandedLib.Helpers;

/// <summary>Implemented by a block, or one of its block behaviours, to state in its handbook page
/// how the player operates it. Found through <c>block.GetCollectibleInterface</c>.</summary>
public interface IHandbookOperatingHelp {
  /// <summary>The interactions the Operating section of the block's handbook page lists, one line
  /// each, in order. Called on the client each time the page is composed, with no world position
  /// or player, so it returns the interactions the block can ever offer, not those of a placed
  /// instance's state.</summary>
  /// <param name="capi">The client api of the player reading the page.</param>
  /// <returns>The interactions to list: the action text, the mouse button and the hotkeys of each
  /// are read, its item stacks are not. Empty for no Operating section.</returns>
  WorldInteraction[] GetHandbookOperatingHelp(ICoreClientAPI capi);
}
