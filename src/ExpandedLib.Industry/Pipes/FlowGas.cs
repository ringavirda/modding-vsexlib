namespace ExpandedLib.Industry.Pipes;

/// <summary>Gas an opening or coupling passes; sets its choked coefficient and isentropic exponent.</summary>
public enum FlowGas {
  /// <summary>Steam: Napier's coefficient, exponent 1.3.</summary>
  Steam,

  /// <summary>Air and flue gas: exponent 1.4.</summary>
  Air,
}
