using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Checks;
using ExpandedLib.Config;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>Returns exlib's process-wide registries to their fresh-process state when a world
/// starts loading, so nothing of the previous world in the same process survives into the
/// next.</summary>
public static class ExWorldState {
  /// <summary>Whether a world starting to load on <paramref name="api"/>'s side empties the
  /// process-wide state the previous world left: always on the server, and on a client only when it
  /// joins a remote server.</summary>
  /// <remarks>A singleplayer client loads after its own server, in the same process, and shares the
  /// state that server has just filled, so it keeps it. Known from <c>StartPre</c> on: the game sets
  /// the client's singleplayer flag before any mod loads.</remarks>
  /// <param name="api">The api of the side whose mods are starting.</param>
  /// <returns>True on the server and on a client of a remote server; false on a singleplayer
  /// client.</returns>
  public static bool ResetsOnLoad(ICoreAPI api) =>
    api.Side == EnumAppSide.Server
    || api is ICoreClientAPI { IsSinglePlayer: false };

  /// <summary>When <see cref="ResetsOnLoad"/> holds, empties every per-world registry exlib owns;
  /// <see cref="ExLiquids"/> keeps its four built-in media. Runs first in exlib's own driver's
  /// <c>StartPre</c>, before any mod's <c>Start</c> registers.</summary>
  internal static void ResetOnLoad(ICoreAPI api) {
    if (!ResetsOnLoad(api))
      return;

    ExDefinitions.Clear();
    EntityRegistry.ResetForWorld();
    ExCheckRegistry.Clear();
    ExConfigProfiles.ResetForWorld();
    ExRecipeProfiles.ResetForWorld();
    ExPreferences.ResetForWorld();
    ExRccSettings.ResetForWorld();
    ExBlockNames.ResetForWorld();
    BlockNetworkNode.ResetForWorld();
    NoSnowCells.Clear();
    StructureFillers.ResetForWorld();

    ExLiquids.ResetForWorld();
    MaterialRoleRegistry.ResetForWorld();
    ProcessRouteRegistry.ResetForWorld();
    ProcessJobRegistry.ResetForWorld();
    BayOccupancyRegistry.ResetForWorld();
  }
}
