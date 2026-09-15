using System.ComponentModel;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Registries;

/// <summary>Drives exlib's own framework modules, and boots exlib's own loggers and tunables
/// ahead of anything that might log or register.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ExModuleModSystem : ModSystem {
  private ExModuleHost? _host;

  private ExModuleHost Host(ICoreAPI api) =>
    _host ??= new ExModuleHost(Mod, api);

  public override double ExecuteOrder() => 0.03;

  public override void StartPre(ICoreAPI api) {
    Definitions.ExDefinitions.Logger = api.Logger;
    EntityRegistry.Logger = api.Logger;

    ExlibValues.Load(api);

    SetFlags(api);

    ExModuleHost host = Host(api);
    api.Logger.Notification(
      "[exlib] modules hosted by {0}: {1}",
      Mod.Info.ModID,
      host.Modules.Count > 0
        ? string.Join(", ", host.Modules.Select(m => m.Id))
        : "none"
    );
    host.StartPre(api);
  }

  public override void Start(ICoreAPI api) {
    SetFlags(api);
    Host(api).Start(api);
  }

  public override void StartServerSide(ICoreServerAPI api) =>
    Host(api).StartServerSide(api);

  public override void StartClientSide(ICoreClientAPI api) =>
    Host(api).StartClientSide(api);

  public override void AssetsLoaded(ICoreAPI api) =>
    Host(api).AssetsLoaded(api);

  public override void AssetsFinalize(ICoreAPI api) =>
    Host(api).AssetsFinalize(api);

  public override void Dispose() {
    _host?.Dispose();
    _host = null;
    base.Dispose();
  }

  // Sets exlib:module:<id> for every enabled module discovered anywhere in the process.
  private static void SetFlags(ICoreAPI api) {
    var config = api.World?.Config;
    if (config == null)
      return;
    foreach (ExModuleInfo module in ExModules.Enabled(api))
      config.SetBool(ExModules.FlagKey(module.Id), true);
  }
}
