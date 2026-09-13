using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;

namespace Grains;

/// <summary>
/// The loaded grain catalogue. Populated by <see cref="GrainsModule.AssetsFinalize"/> on both sides
/// identically, so the mill reads it on the server with no round trip; empty until then.
/// </summary>
public static class GrainCatalogue {
  private static List<GrainDef> _all = [];

  /// <summary>Every entry loaded so far, in asset order.</summary>
  public static IReadOnlyList<GrainDef> All => _all;

  /// <summary>Reads every <c>config/grains/</c> entry across every domain. Runs at
  /// <c>AssetsFinalize</c>, after the patch pipeline has merged the raw JSON.</summary>
  public static void Load(ICoreAPI api) =>
    _all = AssetCatalogueLoader.GetMany<GrainDef>(api, "config/grains/");

  /// <summary>The entry whose grain or sack is <paramref name="itemCode"/>, or <c>null</c>.</summary>
  public static GrainDef? ForItem(string? itemCode) =>
    itemCode == null ? null
    : _all.FirstOrDefault(g => g.Grain == itemCode || $"grains:sack-{g.Code}" == itemCode);

  /// <summary>Test seam: replaces the catalogue without an asset read.</summary>
  internal static void Set(IEnumerable<GrainDef> entries) => _all = [.. entries];
}
