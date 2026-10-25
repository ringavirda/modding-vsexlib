namespace ExpandedLib.Industry.Molten;

/// <summary>
/// A block entity that reports the temperature of the hottest metal it holds on its top face,
/// whatever holds it: its own cell, a parked ladle or a parked mould. <see cref="MoltenBurn"/> reads it
/// in place of the entity's cells when it is implemented.
/// </summary>
public interface IMoltenSurface {
  /// <summary>Temperature (deg C) of the hottest metal held, liquid or set; null when it holds none.</summary>
  float? SurfaceTemperature { get; }
}
