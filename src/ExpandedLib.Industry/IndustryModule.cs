using ExpandedLib.Catalogues;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Industry;

/// <summary>Entry point for the family layer. Implements <see cref="IExModule"/> rather than a
/// mod system, since only one dll per mod folder may contain mod systems.</summary>
public sealed class IndustryModule : IExModule, IExDefinitionContributor {
  // The channel is process-wide; in singleplayer the client's instance shares it with the server's.
  private bool _openedServerChannel;

  /// <summary>When <see cref="ExWorldState.ResetsOnLoad"/> holds, returns the family layer's
  /// registries (metals, pipe tier ratings, the sound channel, the mold gate, the chisel list, the
  /// molten temperature formatter) to their fresh-process state; then registers the refractory tier variant group before any block's
  /// <c>GetHeldItemName</c> can decorate.</summary>
  public void StartPre(ICoreAPI api) {
    if (ExWorldState.ResetsOnLoad(api)) {
      MetalRegistry.ResetForWorld();
      BlockPipe.ResetForWorld();
      ExSounds.StopServer();
      ExMoldGate.ResetForWorld();
      MoltenChisel.ResetForWorld();
      MoltenMetal.ResetForWorld();
    }
    ExBlockNames.AddVariantQualifier("refractory", "exlib:refractory-");
  }

  /// <summary>Registers the three network types this module ships, each with its defaults.</summary>
  public void Start(ICoreAPI api) =>
    RegisterNetworkTypes(api.ModLoader.GetModSystem<BlockNetworkModSystem>());

  /// <summary>Opens the server end of <see cref="ExSounds"/>' channel.</summary>
  public void StartServerSide(ICoreServerAPI api) {
    ExSounds.StartServer(api);
    _openedServerChannel = true;
  }

  /// <summary>Opens the client end of <see cref="ExSounds"/>' channel.</summary>
  public void StartClientSide(ICoreClientAPI api) => ExSounds.StartClient(api);

  /// <summary>Closes the server end of <see cref="ExSounds"/>' channel when this instance opened it;
  /// a singleplayer client's instance leaves its server's channel open.</summary>
  public void Dispose() {
    if (_openedServerChannel)
      ExSounds.StopServer();
  }

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
  /// every domain's <c>config/metals</c>. A singleplayer client loads nothing: it reads the metals its
  /// own server has just loaded in the same process (<see cref="ExWorldState.ResetsOnLoad"/>).</summary>
  public void AssetsFinalize(ICoreAPI api) {
    if (ExWorldState.ResetsOnLoad(api))
      MetalCatalogueLoader.Load(api).Log(api.Logger);
  }
}
