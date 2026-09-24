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
  // Carries the handbook unit patch and the snow patch on both sides.
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

    // Applied once per process whichever side starts first; each side holds it until its Dispose.
    _harmony = ExHarmony.PatchOnce(Mod, GetType().Assembly);
  }

  /// <summary>Loads the shared catalogues (liquids, material roles, process routes, process jobs, bay
  /// occupancy) from every domain's <c>config/</c> once the asset-patch pipeline has merged all mods'
  /// JSON, then runs the content checks. A singleplayer client loads none of them: it reads the ones
  /// its own server has just loaded in the same process (<see cref="ExWorldState.ResetsOnLoad"/>).</summary>
  public override void AssetsFinalize(ICoreAPI api) {
    if (ExWorldState.ResetsOnLoad(api))
      LoadCatalogues(api);

    // The content guards - dangling recipe codes, uncovered lang, pinned network nodes and the rest.
    // RunChecksOnLoad opts out; also available on demand with /exmod verify.
    if (ExlibValues.RunChecksOnLoad)
      Checks.ExlibChecks.Log(api.Logger, Checks.ExlibChecks.All(api));
  }

  private static void LoadCatalogues(ICoreAPI api) {
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
  }

  public override void StartClientSide(ICoreClientAPI api) {
    // The library's own display preferences (metric/imperial unit system).
    PreferenceRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // Loads the per-player display-preference store, writing the file on first run.
    ExPreferences.LoadConfig(api);

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

  /// <summary>Releases this side's hold on exlib's Harmony patches, which come off with the last
  /// side's (<see cref="ExHarmony.UnpatchAll(Mod)"/>). <see cref="NoSnowCells"/> is left to the
  /// server's structures and the next world's load start.</summary>
  public override void Dispose() {
    ExHarmony.UnpatchAll(Mod);
    _harmony = null;
    base.Dispose();
  }
}
