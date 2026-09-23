using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries;

/// <summary>The published mods whose releases built against exlib 0.7 cannot load beside this
/// version.</summary>
internal static class IncompatibleMods {
  /// <summary>Mod id to the name players know the mod by and the first release that loads beside
  /// this version.</summary>
  internal static readonly IReadOnlyDictionary<
    string,
    (string Name, string MinVersion)
  > Known = new Dictionary<string, (string, string)> {
    ["smex"] = ("Steelmaking Expanded", "0.10.0"),
    ["ppex"] = ("Pipes and Power Expanded", "0.7.0"),
  };

  /// <summary>The one message the log and the chat carry, or <c>null</c> when no known mod is
  /// present below its <see cref="Known"/> version.</summary>
  internal static string? Message(IModLoader loader, string exlibVersion) =>
    Message(loader, exlibVersion, DefaultModRoots());

  /// <summary>Overload against an explicit <paramref name="modRoots"/> set. A mod the loader has
  /// enabled is judged by its loaded version alone; one it has not (a failed load) by the newest
  /// copy of it in the roots.</summary>
  internal static string? Message(
    IModLoader loader,
    string exlibVersion,
    IEnumerable<string> modRoots
  ) {
    string[] roots = modRoots.ToArray();
    List<string> needed = Known
      .Where(kv => IsOutdated(loader, kv.Key, kv.Value.MinVersion, roots))
      .Select(kv => $"{kv.Value.Name} {kv.Value.MinVersion} or later")
      .ToList();
    if (needed.Count == 0)
      return null;
    return $"exlib {exlibVersion} needs {string.Join(" and ", needed)}: "
      + "update them, or keep exlib 0.7.2 with the versions installed.";
  }

  // The game's binaries and data Mods folders.
  private static IEnumerable<string> DefaultModRoots() =>
    [GamePaths.BinariesMods, GamePaths.DataPathMods];

  // Falls back to the Mods folders: a failed mod is absent from IModLoader.Mods.
  private static bool IsOutdated(
    IModLoader loader,
    string modId,
    string minVersion,
    string[] modRoots
  ) {
    if (loader.IsModEnabled(modId))
      return !IsAtLeast(loader.GetMod(modId)?.Info?.Version, minVersion);
    List<string?> onDisk = modRoots
      .SelectMany(DiskModInfos)
      .Where(info => info.ModId == modId)
      .Select(info => info.Version)
      .ToList();
    return onDisk.Count > 0 && !onDisk.Any(v => IsAtLeast(v, minVersion));
  }

  // A missing version counts as below the minimum; GameVersion reads an unparsable part as 0.
  private static bool IsAtLeast(string? version, string minVersion) =>
    !string.IsNullOrEmpty(version)
    && GameVersion.IsAtLeastVersion(version, minVersion);

  private static IEnumerable<(string? ModId, string? Version)> DiskModInfos(
    string modRoot
  ) {
    if (!Directory.Exists(modRoot))
      yield break;
    foreach (string entry in Directory.EnumerateFileSystemEntries(modRoot)) {
      JObject? info = ModInfoOf(entry);
      if (info != null)
        yield return ((string?)info["modid"], (string?)info["version"]);
    }
  }

  // The modinfo a folder or zip mod declares; null for anything else.
  private static JObject? ModInfoOf(string entry) {
    try {
      string? json = Directory.Exists(entry)
        ? ReadFolderModInfo(entry)
        : ReadZipModInfo(entry);
      return json == null ? null : JObject.Parse(json);
    } catch {
      return null;
    }
  }

  private static string? ReadFolderModInfo(string folder) {
    string path = Path.Combine(folder, "modinfo.json");
    return File.Exists(path) ? File.ReadAllText(path) : null;
  }

  private static string? ReadZipModInfo(string zipPath) {
    if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
      return null;
    using ZipArchive zip = ZipFile.OpenRead(zipPath);
    ZipArchiveEntry? entry = zip.GetEntry("modinfo.json");
    if (entry == null)
      return null;
    using StreamReader reader = new(entry.Open());
    return reader.ReadToEnd();
  }
}
