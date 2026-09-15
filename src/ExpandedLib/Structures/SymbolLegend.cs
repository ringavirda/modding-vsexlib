using System;
using System.Collections.Generic;

namespace ExpandedLib.Structures;

/// <summary>
/// What happens when a legend maps a symbol that is already mapped. <see cref="Throw"/> suits a
/// code-first layout, where restating a glyph is a mistake; <see cref="Replace"/> suits a filler
/// footprint or a test scene, where re-registering a symbol is normal.
/// </summary>
public enum DuplicatePolicy {
  Throw,
  Replace,
}

/// <summary>
/// Maps a grid's symbols to whatever a caller resolves them into, with one duplicate-mapping rule and
/// an unused-symbol check. Not itself aware of what a symbol means; a <see cref="CellGrid"/> only says
/// which symbols were drawn.
/// </summary>
public sealed class SymbolLegend<TPayload> {
  private readonly DuplicatePolicy _policy;
  private readonly Dictionary<char, TPayload> _payloadOf = new();

  /// <summary>Starts an empty legend with the given <see cref="DuplicatePolicy"/>.</summary>
  public SymbolLegend(DuplicatePolicy policy) => _policy = policy;

  /// <summary>
  /// Maps <paramref name="symbol"/> to <paramref name="payload"/>. Under <see cref="DuplicatePolicy.Throw"/>,
  /// mapping a symbol twice throws <see cref="InvalidOperationException"/>; under
  /// <see cref="DuplicatePolicy.Replace"/> the later mapping wins.
  /// </summary>
  public SymbolLegend<TPayload> Map(char symbol, TPayload payload) {
    if (_policy == DuplicatePolicy.Throw && _payloadOf.ContainsKey(symbol))
      throw new InvalidOperationException(
        $"Layout legend maps symbol '{symbol}' twice; a glyph maps to one entry."
      );
    _payloadOf[symbol] = payload;
    return this;
  }

  /// <summary>The payload mapped to <paramref name="symbol"/>, or false when the legend has none.</summary>
  public bool TryGet(char symbol, out TPayload payload) =>
    _payloadOf.TryGetValue(symbol, out payload!);

  /// <summary>Every mapped symbol <paramref name="grid"/> never drew.</summary>
  public IReadOnlyList<char> Unused(CellGrid grid) {
    var unused = new List<char>();
    foreach (char symbol in _payloadOf.Keys)
      if (!grid.Drawn(symbol))
        unused.Add(symbol);
    return unused;
  }
}
