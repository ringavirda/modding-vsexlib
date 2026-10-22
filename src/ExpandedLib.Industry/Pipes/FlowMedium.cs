namespace ExpandedLib.Industry.Pipes;

/// <summary>What a pipe run carries, set by its sources. An ambient run holds air and has no source and no content of its own.</summary>
public enum FlowMedium {
  /// <summary>No source and no content of its own.</summary>
  Ambient,

  /// <summary>Steam and liquid water, one medium.</summary>
  Water,

  /// <summary>Blast or other air.</summary>
  Air,

  /// <summary>Flue gas.</summary>
  Flue,

  /// <summary>Fuel gas, mixed by grade.</summary>
  Fuel,
}
