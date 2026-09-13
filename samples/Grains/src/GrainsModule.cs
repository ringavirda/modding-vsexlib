using ExpandedLib.Catalogues;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace Grains;

/// <summary>
/// Entry point for the sample module. Ships as its own mod folder, next to <c>grains.dll</c>,
/// carrying no <see cref="ModSystem"/> of its own - exlib's <c>ExModuleModSystem</c> discovers this
/// class through the assembly's <c>[assembly: ExModule]</c> and drives it through the same phases a
/// <see cref="ModSystem"/> would get, proving a Code mod needs none to extend exlib.
/// </summary>
public sealed class GrainsModule : IExModule, IExDefinitionContributor {
  /// <summary>
  /// Emits one generated sack item per <c>config/grains/</c> entry and registers each with
  /// <see cref="ExDefinitions.RegisterItem(ExItemDef)"/>, so the definition system's injection at
  /// <c>AssetsLoaded</c> 0.04 builds them like any other code-first item.
  /// </summary>
  public void Contribute(ICoreAPI api) {
    foreach (
      ExItemDef def in GrainSackItems.Emit(
        "grains",
        AssetCatalogueLoader.GetMany<GrainDef>(api, "config/grains/")
      )
    )
      ExDefinitions.RegisterItem(def);
  }

  /// <summary>Populates <see cref="GrainCatalogue"/> from every domain's <c>config/grains</c>, so
  /// <see cref="BlockBehaviorGrainInfo"/> and the mill core have something to read once the world is up.</summary>
  public void AssetsFinalize(ICoreAPI api) => GrainCatalogue.Load(api);
}
