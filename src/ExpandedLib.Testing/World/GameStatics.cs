using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>The process-wide tables vanilla's collectibles fill from <c>OnLoaded</c>, such as
/// <c>BlockBehaviorWrenchOrientable.VariantsByType</c>, which several worlds standing up on
/// threads write at once.</summary>
internal static class GameStatics {
  private static readonly object LoadGate = new();

  /// <summary>Runs <paramref name="collectible"/>'s <c>OnLoaded</c> with no other call through here
  /// running beside it. A load that does not come through here, such as
  /// <c>RecipeLoading</c>'s <c>OnLoadedNative</c> for the collectibles not yet loaded, is not held
  /// back.</summary>
  internal static void Load(CollectibleObject collectible, ICoreAPI api) {
    lock (LoadGate)
      collectible.OnLoaded(api);
  }
}
