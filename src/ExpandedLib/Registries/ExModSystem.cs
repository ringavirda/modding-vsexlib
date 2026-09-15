using System.Reflection;
using ExpandedLib.Config;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Registries;

/// <summary>The zero-line registration rung: a mod deriving this gets its config,
/// attribute-marked classes, commands and preferences registered with no explicit calls.</summary>
public abstract class ExModSystem : ModSystem {
  /// <summary>The assembly scanned by every registry call below.</summary>
  protected virtual Assembly Assembly => GetType().Assembly;

  /// <summary>When true, patches and unpatches this assembly's Harmony classes. Off by default.</summary>
  protected virtual bool PatchHarmony => false;

  /// <summary>The consumer-facing end of exlib's pinned order.</summary>
  public override double ExecuteOrder() => 0.1;

  // Lazy: a phase called on its own must work without StartPre having run first.
  private ExModuleHost? _modules;

  /// <summary>This mod's own modules, built against whichever phase's <paramref name="api"/> runs
  /// first.</summary>
  private ExModuleHost Modules(ICoreAPI api) =>
    _modules ??= new ExModuleHost(Mod, api);

  /// <summary>Runs every own module's <see cref="IExModule.StartPre"/>, then calls
  /// <see cref="OnStartPre"/>.</summary>
  public override void StartPre(ICoreAPI api) {
    Modules(api).StartPre(api);
    OnStartPre(api);
  }

  /// <summary>Loads config, registers attribute-marked classes and content checks, starts every
  /// own module, then calls <see cref="OnStart"/>.</summary>
  public override void Start(ICoreAPI api) {
    ExConfig.LoadAll(api, Assembly);
    EntityRegistry.RegisterAll(api, Mod, Assembly);
    Checks.ExCheckRegistry.RegisterAll(api, Mod, Assembly);
    if (PatchHarmony)
      ExHarmony.PatchOnce(Mod, Assembly);
    Modules(api).Start(api);
    OnStart(api);
  }

  /// <summary>Runs every own module's <see cref="IExModule.AssetsLoaded"/>, then calls
  /// <see cref="OnAssetsLoaded"/>.</summary>
  public override void AssetsLoaded(ICoreAPI api) {
    Modules(api).AssetsLoaded(api);
    OnAssetsLoaded(api);
  }

  /// <summary>Registers every command class on the server, runs every own module's server start,
  /// then calls <see cref="OnStartServerSide"/>.</summary>
  public override void StartServerSide(ICoreServerAPI api) {
    CommandRegistry.RegisterAll(api, Mod, Assembly);
    Modules(api).StartServerSide(api);
    OnStartServerSide(api);
  }

  /// <summary>Registers preferences then commands on the client, runs every own module's client
  /// start, then calls <see cref="OnStartClientSide"/>.</summary>
  public override void StartClientSide(ICoreClientAPI api) {
    PreferenceRegistry.RegisterAll(api, Mod, Assembly);
    CommandRegistry.RegisterAll(api, Mod, Assembly);
    Modules(api).StartClientSide(api);
    OnStartClientSide(api);
  }

  /// <summary>Runs every own module's <see cref="IExModule.AssetsFinalize"/>, then calls
  /// <see cref="OnAssetsFinalize"/>.</summary>
  public override void AssetsFinalize(ICoreAPI api) {
    Modules(api).AssetsFinalize(api);
    OnAssetsFinalize(api);
  }

  /// <summary>Disposes this mod's modules, unpatches Harmony when <see cref="PatchHarmony"/> is
  /// true, and clears the module host.</summary>
  public override void Dispose() {
    _modules?.Dispose();
    if (PatchHarmony)
      ExHarmony.UnpatchAll(Mod);
    _modules = null;
    base.Dispose();
  }

  /// <summary>Runs before any registration, in <see cref="StartPre"/>. Empty by default.</summary>
  protected virtual void OnStartPre(ICoreAPI api) { }

  /// <summary>Runs in <see cref="AssetsLoaded"/>, after every companion module's own. Empty by
  /// default.</summary>
  protected virtual void OnAssetsLoaded(ICoreAPI api) { }

  /// <summary>Runs after config and entity registration in <see cref="Start"/>. Empty by default.</summary>
  protected virtual void OnStart(ICoreAPI api) { }

  /// <summary>Runs after command registration in <see cref="StartServerSide"/>. Empty by default.</summary>
  protected virtual void OnStartServerSide(ICoreServerAPI api) { }

  /// <summary>Runs after preference and command registration in <see cref="StartClientSide"/>. Empty
  /// by default.</summary>
  protected virtual void OnStartClientSide(ICoreClientAPI api) { }

  /// <summary>Runs in <see cref="AssetsFinalize"/>. Empty by default.</summary>
  protected virtual void OnAssetsFinalize(ICoreAPI api) { }
}
