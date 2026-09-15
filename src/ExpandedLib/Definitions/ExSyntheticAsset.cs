using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace ExpandedLib.Definitions;

/// <summary>
/// Builds the in-memory <see cref="Asset"/> a code-first definition is injected as.
/// Must be the concrete engine type; a custom <see cref="IAsset"/> throws during asset loading.
/// </summary>
internal static class ExSyntheticAsset {
  /// <summary>Constructs a real engine <see cref="Asset"/> carrying <paramref name="data"/> at
  /// <paramref name="location"/>, stamped with <paramref name="origin"/>.</summary>
  public static IAsset Create(
    AssetLocation location,
    byte[] data,
    IAssetOrigin origin
  ) => new Asset(data, location, origin);
}

/// <summary>
/// The <see cref="IAssetOrigin"/> stamped on injected assets.
/// Load hooks are never called; injection goes through <c>AssetManager.Add</c>, not origin enumeration.
/// </summary>
internal sealed class ExDefinitionOrigin : IAssetOrigin {
  public string OriginPath => "exlib:code-first-definitions";

  public void LoadAsset(IAsset asset) { }

  public bool TryLoadAsset(IAsset asset) => true;

  public List<IAsset> GetAssets(
    AssetCategory category,
    bool shouldLoad = true
  ) => [];

  public List<IAsset> GetAssets(
    AssetLocation baseLocation,
    bool shouldLoad = true
  ) => [];

  // A false return skips gameplay-affecting categories such as blocktypes and itemtypes.
  public bool IsAllowedToAffectGameplay() => true;
}
