using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries;

/// <summary>The published mods built against exlib 0.7 that cannot load beside this version.</summary>
internal static class IncompatibleMods {
  /// <summary>Mod id to the name players know the mod by.</summary>
  internal static readonly IReadOnlyDictionary<string, string> Known =
    new Dictionary<string, string> {
      ["smex"] = "Steelmaking Expanded",
      ["ppex"] = "Pipes and Power Expanded",
    };

  /// <summary>The one message the log and the chat carry, or <c>null</c> when no known mod is
  /// present.</summary>
  internal static string? Message(IModLoader loader, string exlibVersion) =>
    Message(loader, exlibVersion, DefaultModRoots());

  /// <summary>Overload against an explicit <paramref name="modRoots"/> set.</summary>
  internal static string? Message(
    IModLoader loader,
    string exlibVersion,
    IEnumerable<string> modRoots
  ) {
    string[] roots = modRoots.ToArray();
    List<string> names = Known
      .Where(kv => IsPresent(loader, kv.Key, roots))
      .Select(kv => kv.Value)
      .ToList();
    if (names.Count == 0)
      return null;
    return $"exlib {exlibVersion} does not work with {string.Join(" and ", names)}: "
      + "keep exlib 0.7.2 with them, or replace them with Iron Industry Expanded.";
  }

  // The game's binaries and data Mods folders.
  private static IEnumerable<string> DefaultModRoots() =>
    [GamePaths.BinariesMods, GamePaths.DataPathMods];

  // Falls back to reading the id off the Mods folders: a failed mod is absent from IModLoader.Mods.
  private static bool IsPresent(
    IModLoader loader,
    string modId,
    IEnumerable<string> modRoots
  ) => loader.IsModEnabled(modId) || modRoots.Any(root => OnDisk(root, modId));

  private static bool OnDisk(string modRoot, string modId) {
    if (!Directory.Exists(modRoot))
      return false;
    foreach (string entry in Directory.EnumerateFileSystemEntries(modRoot)) {
      if (ModIdOf(entry) == modId)
        return true;
    }
    return false;
  }

  // The modid a folder or zip mod declares; null for anything else.
  private static string? ModIdOf(string entry) {
    try {
      string? json = Directory.Exists(entry)
        ? ReadFolderModInfo(entry)
        : ReadZipModInfo(entry);
      return json == null ? null : (string?)JObject.Parse(json)["modid"];
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
