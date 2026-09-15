using System.ComponentModel;
using ExpandedLib.Catalogues;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ExpandedLib;

/// <summary>
/// Entry point for the shared Expanded Lib mod (<c>exlib</c>): registers the library's own blocks,
/// block entities and behaviours, and owns the client-side display-preferences store and commands.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ExpandedLibModSystem : ModSystem {
  // Client-side Harmony instance for the handbook unit patch.
  private Harmony? _harmony;

  // The message named at StartPre and repeated to every joining player; null when nothing clashes.
  private string? _incompatible;

  // Between the module driver's 0.03 and consumers' inherited 0.1.
  public override double ExecuteOrder() => 0.06;

  public override void StartPre(ICoreAPI api) {
    _incompatible = IncompatibleMods.Message(api.ModLoader, Mod.Info.Version);
    if (_incompatible != null)
      Mod.Logger.Error(_incompatible);
  }

  public override void Start(ICoreAPI api) {
    // Registers [BlockRegister]/[BlockEntityRegister]/[BlockBehaviorRegister] classes under the exlib domain.
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // The shared filler block dependent mods' mega-blocks reserve footprint cells with.
    StructureFillers.FillerCode = new AssetLocation(
      ExlibBlocks.Structurefiller.Code
    );
  }

  /// <summary>Populates the shared liquid catalogue from every domain's <c>config/liquids</c>, once
  /// the asset-patch pipeline has merged all mods' JSON.</summary>
  public override void AssetsFinalize(ICoreAPI api) {
    LiquidCatalogueLoader.Load(api).Log(api.Logger);
    // The material-role catalogue (flux/fuel/ore/scrap/charge) and its mod-gated contributors;
    // must run once the metal and liquid registries have loaded.
    MaterialRoleLoader.Load(api).Log(api.Logger);

    // The merged process-stage catalogue, read post-patch.
    ProcessRouteLoader.Load(api).Log(api.Logger);

    // The terminal half of the same contract: every machine's job table.
    ProcessJobLoader.Load(api).Log(api.Logger);

    // What each store's items occupy; also each store's whitelist.
    BayOccupancyLoader.Load(api).Log(api.Logger);

    // The content guards - dangling recipe codes, uncovered lang, pinned network nodes and the rest.
    // RunChecksOnLoad opts out; also available on demand with /exmod verify.
    if (ExlibValues.RunChecksOnLoad)
      Checks.ExlibChecks.Log(api.Logger, Checks.ExlibChecks.All(api));
  }

  public override void StartClientSide(ICoreClientAPI api) {
    // The library's own display preferences (metric/imperial unit system).
    PreferenceRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // Loads the per-player display-preference store, writing the file on first run.
    ExPreferences.LoadConfig(api);

    // The handbook unit-conversion patch; applied once regardless of dependent mod count.
    _harmony = ExHarmony.PatchOnce(Mod, GetType().Assembly);

    // Applies the local player's saved choices once the world and player are ready.
    api.Event.LevelFinalize += () =>
      ExPreferences.ApplyForPlayer(api.World.Player.PlayerUID);

    // The library's own client commands: the shared .exmod root and its network-highlight sub-command.
    CommandRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // Applies every dependent mod's selected recipe-cost level to the live recipes.
    ExRecipeProfiles.ApplyAll(api);
  }

  public override void StartServerSide(ICoreServerAPI api) {
    // The server-side counterpart: the universal exmod root, plus the /exmod recipes <mod> <level> switch.
    CommandRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // Applies every registered mod's selected recipe-cost level to the live, host-authoritative recipes.
    ExRecipeProfiles.ApplyAll(api);

    // Repeats the StartPre finding to every joining player.
    if (_incompatible is { } message)
      api.Event.PlayerJoin += player =>
        player.SendMessage(
          GlobalConstants.GeneralChatGroup,
          message,
          EnumChatType.Notification
        );
  }

  public override void Dispose() {
    ExHarmony.UnpatchAll(Mod);
    _harmony = null;
    base.Dispose();
  }
}
