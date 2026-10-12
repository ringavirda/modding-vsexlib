using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>The log sink of the registration tables, wired at startup; null before it, and every
/// table is then silent.</summary>
internal static class RegistryLog {
  internal static ILogger? Logger { get; set; }

  /// <summary>Warns that <paramref name="incoming"/> took <paramref name="key"/> of
  /// <paramref name="table"/> from <paramref name="previous"/>.</summary>
  internal static void Replaced(
    string table,
    string key,
    string previous,
    string incoming
  ) =>
    Logger?.Warning(
      "[exlib] {0}: '{1}' registered by {2} is replaced by {3}.",
      table,
      key,
      previous,
      incoming
    );
}
