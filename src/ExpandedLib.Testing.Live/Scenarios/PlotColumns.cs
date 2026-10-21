using Vintagestory.API.Server;

namespace ExpandedLib.Testing.Live;

/// <summary>The plot's chunk columns, which <c>reload</c> unloads and loads back.</summary>
internal interface IPlotColumns {
  /// <summary>Whether every column is loaded; false from <see cref="Unload"/> until the load
  /// that follows <see cref="Load"/> completes.</summary>
  bool Loaded { get; }

  /// <summary>Whether no column is held in memory any more; the server finishes an
  /// <see cref="Unload"/> some ticks after the call.</summary>
  bool Unloaded { get; }

  /// <summary>Unloads every column, which runs <c>OnBlockUnloaded</c> and saves the chunks.</summary>
  void Unload();

  /// <summary>Starts loading and keeping every column again.</summary>
  void Load();
}

/// <summary><see cref="IPlotColumns"/> over a plot on the running server.</summary>
internal sealed class ServerPlotColumns(ICoreServerAPI api, ScenarioPlot plot)
  : IPlotColumns {
  public bool Loaded => plot.Loaded;

  public bool Unloaded => plot.Unloaded(api);

  public void Unload() => plot.Unload(api);

  public void Load() => plot.Load(api);
}
