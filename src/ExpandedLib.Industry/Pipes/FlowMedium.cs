namespace ExpandedLib.Industry.Pipes;

/// <summary>What a pipe run carries, set by its sources. An ambient run holds air and has no source and no content of its own.</summary>
internal enum FlowMedium {
  Ambient,
  Water,
  Air,
  Flue,
  Fuel,
}
