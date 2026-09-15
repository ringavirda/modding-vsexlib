using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Helpers;

/// <summary>Cross-mod hook for asking whether a tool-mold type is currently disabled by a config
/// gate, without the asker referencing the mod that owns the molds.</summary>
public static class ExMoldGate {
  private static System.Func<AssetLocation?, bool>? _isDisabled;

  /// <summary>Registers the predicate that decides whether a mold <c>code</c> is config-disabled.</summary>
  public static void RegisterIsDisabled(
    System.Func<AssetLocation?, bool> isDisabled
  ) => _isDisabled = isDisabled;

  /// <summary>Whether the tool mold identified by <paramref name="code"/> is currently disabled.</summary>
  public static bool IsToolMoldDisabled(AssetLocation? code) =>
    _isDisabled?.Invoke(code) ?? false;
}
