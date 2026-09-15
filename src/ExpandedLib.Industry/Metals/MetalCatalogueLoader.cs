using System;
using System.Collections.Generic;
using ExpandedLib.Catalogues;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Metals;

/// <summary>Populates <see cref="MetalRegistry"/> at <c>AssetsFinalize</c> in two passes: a
/// derived baseline, then an overlay of every domain's <c>config/metals/*.json</c>.</summary>
public static class MetalCatalogueLoader {
  /// <summary>Reads the assets, repopulates <see cref="MetalRegistry"/> and runs its code
  /// contributors.</summary>
  public static CatalogueLoadReport Load(ICoreAPI api) {
    MetalRegistry.Clear();
    AssetCatalogueLoader.ReadResult<MetalDef> read =
      AssetCatalogueLoader.Read<MetalDef>(api, "config/metals/");
    var warnings = new List<string>();
    int registered = Populate(
      WorldPropertyMetalCodes(api),
      read.Items,
      warnings.Add,
      read.Sources
    );
    MetalRegistry.Contributors.Invoke(api, api.Logger);

    var errors = new List<string>(read.Errors);
    errors.AddRange(warnings);
    return new CatalogueLoadReport("metals", read.Files, registered, errors);
  }

  /// <summary>The two passes over already-read inputs: a convention baseline, then the
  /// <paramref name="overlays"/>. The caller must clear the registry first.</summary>
  /// <param name="sources">One source location per entry of <paramref name="overlays"/>, same index.
  /// Null reads as "unknown source".</param>
  /// <returns>How many of <paramref name="overlays"/> were registered.</returns>
  internal static int Populate(
    IEnumerable<string> baselineCodes,
    IEnumerable<MetalDef> overlays,
    Action<string>? warn = null,
    IReadOnlyList<string>? sources = null
  ) {
    // Pass 1 - baseline. Pure string work, no item resolution.
    foreach (string raw in baselineCodes) {
      string code = ShortCode(raw);
      if (code.Length == 0)
        continue;
      string moltenItem = new AssetLocation("game", "ingot-" + code).ToString();
      if (MetalRegistry.TryGet(moltenItem, out _))
        continue; // two worldproperties listing the same metal - derive once
      MetalRegistry.Register(
        new MetalDef { Code = code, MoltenItem = moltenItem }
      );
    }

    // Pass 2 - overlay. A def missing either required field cannot be keyed.
    var overlayList = overlays as IReadOnlyList<MetalDef> ?? [.. overlays];
    int registered = 0;
    for (int i = 0; i < overlayList.Count; i++) {
      MetalDef def = overlayList[i];
      if (
        string.IsNullOrEmpty(def.Code) || string.IsNullOrEmpty(def.MoltenItem)
      ) {
        string source =
          sources != null && i < sources.Count ? sources[i] : "unknown source";
        warn?.Invoke(
          source
            + ": skipping metal def missing code/moltenItem (code='"
            + def.Code
            + "', moltenItem='"
            + def.MoltenItem
            + "')"
        );
        continue;
      }
      MetalRegistry.Register(def);
      registered++;
    }
    return registered;
  }

  // Every metal short-code declared in every loaded metal worldproperty (vanilla + mods).
  private static IEnumerable<string> WorldPropertyMetalCodes(ICoreAPI api) {
    var codes = new List<string>();
    foreach (IAsset asset in api.Assets.GetMany("worldproperties/block/metal")) {
      MetalWorldProperty? wp = null;
      try {
        wp = asset.ToObject<MetalWorldProperty>();
      } catch (Exception e) {
        api.Logger.Warning(
          "[exlib] Skipping malformed metal worldproperty {0}: {1}",
          asset.Location,
          e.Message
        );
      }
      if (wp?.Variants == null)
        continue;
      foreach (MetalWorldVariant v in wp.Variants)
        if (!string.IsNullOrEmpty(v.Code))
          codes.Add(v.Code);
    }
    return codes;
  }

  // Worldproperty codes are bare ("copper") or "domain:copper"; the segment past any colon.
  private static string ShortCode(string worldPropCode) {
    if (string.IsNullOrEmpty(worldPropCode))
      return "";
    int colon = worldPropCode.IndexOf(':');
    return colon >= 0 ? worldPropCode[(colon + 1)..] : worldPropCode;
  }

  // Minimal shapes for reading the metal worldproperty; only the variant codes are needed.
  private sealed class MetalWorldProperty {
    public List<MetalWorldVariant>? Variants { get; set; }
  }

  private sealed class MetalWorldVariant {
    public string Code { get; set; } = "";
  }
}
