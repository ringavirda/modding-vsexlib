using System;
using System.Collections.Generic;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Catalogues;

/// <summary>Populates <see cref="MaterialRoleRegistry"/> from every domain's
/// <c>config/materialroles.json</c> and the registered code contributors.</summary>
public static class MaterialRoleLoader {
  /// <summary>Reads the assets, repopulates <see cref="MaterialRoleRegistry"/> and runs its code
  /// contributors.</summary>
  public static CatalogueLoadReport Load(ICoreAPI api) {
    MaterialRoleRegistry.Clear();
    AssetCatalogueLoader.ReadResult<MaterialRoleCatalogue> read =
      AssetCatalogueLoader.Read<MaterialRoleCatalogue>(
        api,
        "config/materialroles.json"
      );
    var warnings = new List<string>();
    int registered = Overlay(
      read.Items,
      warnings.Add,
      api.ModLoader.IsModEnabled,
      read.Sources
    );
    // Must run after the clear and the JSON overlay.
    MaterialRoleRegistry.InvokeContributors(api);

    var errors = new List<string>(read.Errors);
    errors.AddRange(warnings);
    return new CatalogueLoadReport(
      "materialroles",
      read.Files,
      registered,
      errors
    );
  }

  /// <summary>
  /// Registers every valid def from already-read catalogues, skipping and warning on a def with no
  /// role or with neither code nor path prefix. The caller must have cleared the registry first.
  /// </summary>
  /// <param name="modPresent">Answers whether a mod id is loaded, gating
  /// <see cref="MaterialRoleDef.RequiresMod"/>. Null answers "nothing is loaded".</param>
  /// <param name="sources">One source location per entry of <paramref name="catalogues"/>, same
  /// index, named in a skip warning. Null reads as "unknown source".</param>
  /// <returns>How many defs across every catalogue were registered.</returns>
  internal static int Overlay(
    IEnumerable<MaterialRoleCatalogue> catalogues,
    Action<string>? warn = null,
    // Qualified: Vintagestory.API.Common declares its own Func<,>, ambiguous unqualified.
    System.Func<string, bool>? modPresent = null,
    IReadOnlyList<string>? sources = null
  ) {
    var catalogueList =
      catalogues as IReadOnlyList<MaterialRoleCatalogue> ?? [.. catalogues];
    int registered = 0;
    for (int i = 0; i < catalogueList.Count; i++) {
      MaterialRoleCatalogue cat = catalogueList[i];
      if (cat.Materials == null)
        continue;
      string source =
        sources != null && i < sources.Count ? sources[i] : "unknown source";
      foreach (MaterialRoleDef def in cat.Materials) {
        if (string.IsNullOrEmpty(def.Role)) {
          warn?.Invoke(source + ": skipping material role def with no role");
          continue;
        }
        if (
          string.IsNullOrEmpty(def.Code) && string.IsNullOrEmpty(def.PathPrefix)
        ) {
          warn?.Invoke(
            source
              + ": skipping material role def '"
              + def.Role
              + "' with neither code nor pathPrefix"
          );
          continue;
        }
        if (
          !string.IsNullOrEmpty(def.RequiresMod)
          && !(modPresent?.Invoke(def.RequiresMod!) ?? false)
        )
          continue;
        MaterialRoleRegistry.Register(def);
        registered++;
      }
    }
    return registered;
  }
}
