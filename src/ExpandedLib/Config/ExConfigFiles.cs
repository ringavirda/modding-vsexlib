using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Config;

/// <summary>Shared helpers for the on-disk config files under the game's <c>ModConfig</c> folder.
/// The generic <see cref="ExConfigRegister{TConfig}"/> store instead folds legacy files into the
/// shared <see cref="ExConfigDocument"/> via <see cref="ExConfigDocument.FoldLegacy"/>.</summary>
internal static class ExConfigFiles {
  /// <summary>Renames a legacy file under <c>ModConfig</c> to <paramref name="fileName"/> if it does
  /// not yet exist (first match wins); an IO failure is logged and swallowed.</summary>
  public static void RenameLegacy(
    ICoreAPI api,
    string modId,
    string fileName,
    IReadOnlyList<string> legacyFileNames
  ) {
    if (legacyFileNames == null || legacyFileNames.Count == 0)
      return;

    try {
      string dir = GamePaths.ModConfig;
      string target = Path.Combine(dir, fileName);
      if (File.Exists(target))
        return; // new file already present - leave any legacy file untouched.

      foreach (var legacy in legacyFileNames) {
        if (string.IsNullOrWhiteSpace(legacy))
          continue;
        string source = Path.Combine(dir, legacy);
        if (!File.Exists(source))
          continue;

        File.Move(source, target);
        api.Logger.Notification(
          "[{0}] Renamed legacy config '{1}' to '{2}'.",
          modId,
          legacy,
          fileName
        );
        return;
      }
    } catch (Exception e) {
      api.Logger.Warning(
        "[{0}] Could not migrate a legacy config file to '{1}'. {2}",
        modId,
        fileName,
        e
      );
    }
  }
}
