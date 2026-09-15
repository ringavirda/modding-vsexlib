using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// The per-cell contract the <see cref="MoltenNetwork"/> flow driver needs from each molten-canal
/// block entity: stored metal, capacity, the flow-blocking latches, and the flow-source and
/// drain-fitting capability flags.
/// </summary>
public interface IMoltenCell {
  /// <summary>Units of liquid metal held by this cell, or of solidified metal once latched.</summary>
  int CellAmount { get; }

  /// <summary>Full code of the metal in this cell, e.g. "game:ingot-iron"; empty when empty.</summary>
  string CellMetalType { get; }

  /// <summary>This cell's metal temperature ( deg C).</summary>
  float CellTemperature { get; }

  /// <summary>This cell's metal capacity, in units.</summary>
  int MaxUnitCapacity { get; }

  /// <summary>Whether this cell is clay-sealed: a manual valve severing flow at its position.</summary>
  bool Sealed { get; }

  /// <summary>Whether this cell's metal has solidified, blocking flow until chiselled or broken.</summary>
  bool Solidified { get; }

  /// <summary>True for a cell that seeds the flow (the canal start); the distance-from-start BFS roots here.</summary>
  bool IsFlowSource { get; }

  /// <summary>True for a drain fitting (tap, mold pedestal) that takes a sub-minimum final transfer.</summary>
  bool AcceptsSubMinimumFlow { get; }

  /// <summary>Rebuilds the server temperature carrier after a world load; only type and temperature persist.</summary>
  void EnsureMetalStack(IWorldAccessor world);

  /// <summary>Pushes up to <paramref name="amount"/> units of <paramref name="metalType"/> into this cell, temperature-averaging with same-type metal present; returns the amount accepted.</summary>
  int PushMetalRaw(
    int amount,
    string metalType,
    float temperature,
    IWorldAccessor world
  );

  /// <summary>Removes up to <paramref name="amount"/> liquid units from this cell; returns the amount drained.</summary>
  int DrainMetal(int amount);

  /// <summary>Per-tick thermal update: refreshes the displayed temperature from the time-based decay and latches solidification once the metal drops below its melting point.</summary>
  void UpdateThermal(IWorldAccessor world);
}
