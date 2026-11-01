using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Casting;

/// <summary>Recognition and load-time validation of mold patterns.</summary>
public static class CastingPatterns {
  /// <summary>Whether <paramref name="collectible"/> is a mold pattern: any collectible, of any domain and
  /// code, whose attributes carry a <see cref="MoldSpec.AttributeKey"/> entry.</summary>
  public static bool IsPattern(CollectibleObject? collectible) =>
    collectible?.Attributes?[MoldSpec.AttributeKey] is { Exists: true };

  /// <summary>
  /// Checks every station entry of every pattern in <paramref name="collectibles"/> against
  /// <see cref="MoldSpec"/>. Returns one <c>code: error</c> message per malformed pattern, empty when
  /// all are valid and also empty, without reading <paramref name="collectibles"/>, when no station kind is
  /// registered.
  /// </summary>
  /// <param name="collectibles">The loaded collectibles; null entries are skipped.</param>
  /// <param name="stations">The kinds station entries may name; <see cref="CastingStationRegistry.Shared"/> when null.</param>
  public static List<string> Validate(
    IEnumerable<CollectibleObject?> collectibles,
    CastingStationRegistry? stations = null
  ) {
    var errors = new List<string>();
    stations ??= CastingStationRegistry.Shared;
    if (stations.Count == 0)
      return errors;
    foreach (CollectibleObject? c in collectibles) {
      if (!IsPattern(c))
        continue;
      if (
        !MoldSpec.TryParseAll(
          c!.Attributes[MoldSpec.AttributeKey],
          out _,
          out string? error,
          stations
        )
      )
        errors.Add($"{c.Code}: {error}");
    }
    return errors;
  }
}
