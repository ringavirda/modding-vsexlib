using System;

namespace ExpandedLib.Blocks;

/// <summary>The lazy build behind every block entity's or block-entity-behaviour's
/// <c>Persisted</c> property.</summary>
internal static class BlockEntityStateHost {
  /// <summary>Returns <paramref name="backing"/>, building it on first call: a fresh
  /// <see cref="ExBlockState"/>, scanned for <see cref="PersistAttribute"/> members, then handed to
  /// <paramref name="declareState"/> for whatever <paramref name="owner"/> declares by hand.</summary>
  public static ExBlockState GetOrCreate(
    object owner,
    ref ExBlockState? backing,
    Action<ExBlockState> declareState
  ) {
    if (backing != null)
      return backing;
    var state = new ExBlockState();
    backing = state;
    PersistScan.Declare(owner, state);
    declareState(state);
    return state;
  }
}
