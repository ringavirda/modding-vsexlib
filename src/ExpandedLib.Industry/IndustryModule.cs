using ExpandedLib.Catalogues;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry;

/// <summary>Entry point for the family layer. Implements <see cref="IExModule"/> rather than a
/// mod system, since only one dll per mod folder may contain mod systems.</summary>
public sealed class IndustryModule : IExModule, IExDefinitionContributor {
  /// <summary>Registers the refractory tier variant group before any block's
  /// <c>GetHeldItemName</c> can decorate.</summary>
  public void StartPre(ICoreAPI api) =>
    ExBlockNames.AddVariantQualifier("refractory", "exlib:refractory-");

  /// <summary>Registers the three network types this module ships, each with its defaults.</summary>
  public void Start(ICoreAPI api) =>
    RegisterNetworkTypes(api.ModLoader.GetModSystem<BlockNetworkModSystem>());

  /// <summary>The registrations <see cref="Start"/> makes, callable without a mod loader.</summary>
  public static void RegisterNetworkTypes(BlockNetworkModSystem networks) {
    networks.RegisterNetworkType("pipe", () => new PipeNetwork(networks));
    networks.RegisterNetworkType("molten", () => new MoltenNetwork(networks));
    networks.RegisterNetworkType(
      "mpenergy",
      () => new MpEnergyNetwork(networks)
    );
  }

  /// <summary>Emits the generated metal resource item family and registers each with
  /// <see cref="ExDefinitions.RegisterItem(ExItemDef)"/>.</summary>
  public void Contribute(ICoreAPI api) {
    foreach (
      ExItemDef def in MetalFamilyEmitter.Emit(
        AssetCatalogueLoader.GetMany<MetalDef>(api, "config/metals/")
      )
    )
      ExDefinitions.RegisterItem(def);
  }

  /// <summary>Populates <see cref="MetalRegistry"/> from the loaded metal worldproperties and
  /// every domain's <c>config/metals</c>.</summary>
  public void AssetsFinalize(ICoreAPI api) =>
    MetalCatalogueLoader.Load(api).Log(api.Logger);
}
