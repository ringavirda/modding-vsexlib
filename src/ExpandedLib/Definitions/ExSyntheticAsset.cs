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
  ) {
    (origin as ExDefinitionOrigin)?.Keep(location, data);
    return new Asset(data, location, origin);
  }
}

/// <summary>
/// The <see cref="IAssetOrigin"/> stamped on injected assets.
/// Injection goes through <c>AssetManager.Add</c>, not origin enumeration. It keeps each asset's
/// bytes, so an asset the server unloads once the world is up (<c>UnloadUnpatchedAssets</c>) loads
/// again when a later reader asks for it.
/// </summary>
internal sealed class ExDefinitionOrigin : IAssetOrigin {
  private readonly Dictionary<AssetLocation, byte[]> _data = [];

  public string OriginPath => "exlib:code-first-definitions";

  /// <summary>Keeps <paramref name="data"/> as the bytes of the asset at
  /// <paramref name="location"/>.</summary>
  internal void Keep(AssetLocation location, byte[] data) =>
    _data[location] = data;

  public void LoadAsset(IAsset asset) {
    if (_data.TryGetValue(asset.Location, out byte[]? data))
      asset.Data = data;
  }

  public bool TryLoadAsset(IAsset asset) {
    LoadAsset(asset);
    return true;
  }

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
