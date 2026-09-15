using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Registries;

/// <summary>An entry point of a module, run through the phases of its host's own
/// <see cref="ModSystem"/>. Every method has an empty default.</summary>
public interface IExModule {
  /// <summary>Runs in the host's <c>StartPre</c>, before any registration.</summary>
  void StartPre(ICoreAPI api) { }

  /// <summary>Runs in the host's <c>Start</c>, after this module's registered classes have been
  /// registered for it.</summary>
  void Start(ICoreAPI api) { }

  /// <summary>Runs in the host's <c>StartServerSide</c>, after this module's server commands have
  /// been registered.</summary>
  void StartServerSide(ICoreServerAPI api) { }

  /// <summary>Runs in the host's <c>StartClientSide</c>, after this module's preferences and client
  /// commands have been registered.</summary>
  void StartClientSide(ICoreClientAPI api) { }

  /// <summary>Runs in the host's <c>AssetsLoaded</c>, at the host's own <c>ExecuteOrder</c>.</summary>
  void AssetsLoaded(ICoreAPI api) { }

  /// <summary>Runs in the host's <c>AssetsFinalize</c>, after the asset-patch pipeline has merged
  /// every mod's JSON.</summary>
  void AssetsFinalize(ICoreAPI api) { }

  /// <summary>Runs when the host disposes.</summary>
  void Dispose() { }
}
