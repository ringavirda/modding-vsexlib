using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>The process-wide tables vanilla's collectibles fill from <c>OnLoaded</c>, such as
/// <c>BlockBehaviorWrenchOrientable.VariantsByType</c>, which several worlds standing up on
/// threads write at once.</summary>
internal static class GameStatics {
  private static readonly object LoadGate = new();

  /// <summary>Runs <paramref name="collectible"/>'s <c>OnLoaded</c> with no other world's running
  /// beside it.</summary>
  internal static void Load(CollectibleObject collectible, ICoreAPI api) {
    lock (LoadGate)
      collectible.OnLoaded(api);
  }
}
