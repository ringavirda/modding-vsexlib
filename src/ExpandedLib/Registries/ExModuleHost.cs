using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Registries;

/// <summary>One driver instance's modules, run through the same phases and order as the host's
/// own <see cref="ModSystem"/>.</summary>
public sealed class ExModuleHost {
  private readonly Mod _mod;
  private readonly ExModuleSet _set;
  private readonly List<(
    ExModuleInfo Info,
    List<IExModule> Instances
  )> _resolved;

  // The Harmony ids this host's Start holds and nothing has released yet.
  private readonly List<string> _heldHarmonyIds = [];

  /// <summary>Builds the host for <paramref name="mod"/> from its discovered, enabled module set
  /// against <paramref name="api"/>'s world, instantiating every entry point.</summary>
  public ExModuleHost(Mod mod, ICoreAPI api)
    : this(mod, ExModules.For(api, mod.Info.ModID)) { }

  /// <summary>As <see cref="ExModuleHost(Mod, ICoreAPI)"/>, against a hand-built <paramref name="set"/>.</summary>
  internal ExModuleHost(Mod mod, ExModuleSet set) {
    _mod = mod;
    _set = set;
    _resolved = [.. set.Modules.Select(info => (info, Instantiate(info)))];
  }

  /// <summary>This host's modules, in the dependency order <see cref="ExModules.For"/> gave them.</summary>
  public IReadOnlyList<ExModuleInfo> Modules => _set.Modules;

  /// <summary>Logs <see cref="ExModuleSet.Errors"/>, then runs every module's
  /// <see cref="IExModule.StartPre"/>, module then entry-point order.</summary>
  public void StartPre(ICoreAPI api) {
    foreach (string error in _set.Errors)
      _mod.Logger.Error(error);
    Drive(m => m.StartPre(api));
  }

  /// <summary>Per module, loads config and registers classes and checks before its entry points'
  /// <see cref="IExModule.Start"/>.</summary>
  public void Start(ICoreAPI api) {
    foreach ((ExModuleInfo info, List<IExModule> instances) in _resolved) {
      ExConfig.LoadAll(api, info.Assembly);
      EntityRegistry.RegisterAll(api, _mod, info.Assembly);
      Checks.ExCheckRegistry.RegisterAll(api, _mod, info.Assembly);
      if (info.PatchHarmony) {
        ExHarmony.PatchOnce(info.HarmonyId, info.Assembly);
        _heldHarmonyIds.Add(info.HarmonyId);
      }
      foreach (IExModule module in instances)
        Isolate(module, m => m.Start(api));
    }
  }

  /// <summary>Per module, <see cref="CommandRegistry.RegisterAll"/> before its entry points'
  /// <see cref="IExModule.StartServerSide"/>.</summary>
  /// <remarks>An entry point that throws is logged and skipped. When a registration throws, releases
  /// every Harmony hold <see cref="Start"/> took, then rethrows the exception unchanged: the game
  /// drops the system driving a side start that throws and never disposes it.</remarks>
  public void StartServerSide(ICoreServerAPI api) {
    try {
      foreach ((ExModuleInfo info, List<IExModule> instances) in _resolved) {
        CommandRegistry.RegisterAll(api, _mod, info.Assembly);
        foreach (IExModule module in instances)
          Isolate(module, m => m.StartServerSide(api));
      }
    } catch {
      ReleaseHarmony();
      throw;
    }
  }

  /// <summary>Per module, <see cref="PreferenceRegistry.RegisterAll"/> then
  /// <see cref="CommandRegistry.RegisterAll"/> before its entry points'
  /// <see cref="IExModule.StartClientSide"/>.</summary>
  /// <remarks>Releases the Harmony holds when a registration throws, as
  /// <see cref="StartServerSide"/> does.</remarks>
  public void StartClientSide(ICoreClientAPI api) {
    try {
      foreach ((ExModuleInfo info, List<IExModule> instances) in _resolved) {
        PreferenceRegistry.RegisterAll(api, _mod, info.Assembly);
        CommandRegistry.RegisterAll(api, _mod, info.Assembly);
        foreach (IExModule module in instances)
          Isolate(module, m => m.StartClientSide(api));
      }
    } catch {
      ReleaseHarmony();
      throw;
    }
  }

  /// <summary>Runs every module's <see cref="IExModule.AssetsLoaded"/>.</summary>
  public void AssetsLoaded(ICoreAPI api) => Drive(m => m.AssetsLoaded(api));

  /// <summary>Runs every module's <see cref="IExModule.AssetsFinalize"/>.</summary>
  public void AssetsFinalize(ICoreAPI api) => Drive(m => m.AssetsFinalize(api));

  /// <summary>Runs every entry point's <see cref="IExModule.Dispose"/>, then releases each Harmony
  /// hold this host's <see cref="Start"/> took for a module that opted in
  /// (<see cref="ExHarmony.UnpatchAll(string)"/>); a host never started, already disposed, or whose
  /// side start threw releases none.</summary>
  public void Dispose() {
    Drive(m => m.Dispose());
    ReleaseHarmony();
  }

  /// <summary>Releases each Harmony hold <see cref="Start"/> took that nothing has released yet
  /// (<see cref="ExHarmony.UnpatchAll(string)"/>); a second call releases none. Runs no module
  /// hook.</summary>
  internal void ReleaseHarmony() {
    foreach (string id in _heldHarmonyIds)
      ExHarmony.UnpatchAll(id);
    _heldHarmonyIds.Clear();
  }

  private List<IExModule> Instantiate(ExModuleInfo info) {
    var instances = new List<IExModule>();
    foreach (Type type in info.EntryPoints) {
      try {
        instances.Add((IExModule)Activator.CreateInstance(type)!);
      } catch (Exception e) {
        _mod.Logger.Error(
          "Module entry point {0} could not be constructed; skipped.",
          type.FullName
        );
        _mod.Logger.Error(e);
      }
    }
    return instances;
  }

  private void Drive(Action<IExModule> phase) {
    foreach ((_, List<IExModule> instances) in _resolved)
      foreach (IExModule module in instances)
        Isolate(module, phase);
  }

  private void Isolate(IExModule module, Action<IExModule> phase) {
    try {
      phase(module);
    } catch (Exception e) {
      _mod.Logger.Error(
        "Module {0} threw; the rest of the host continues without what it does.",
        module.GetType().FullName
      );
      _mod.Logger.Error(e);
    }
  }
}
