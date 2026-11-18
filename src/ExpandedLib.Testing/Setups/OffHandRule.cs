#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// The off-hand check of <see cref="TestPlayer.HoldOffHand"/>: a stack the game's off-hand slot would
/// not take is refused unless a mod's <c>tests/live/offhand-allow.txt</c> lists its exact code.
/// </summary>
public static class OffHandRule
{
  /// <summary>The allow list's file name, in a mod's <c>tests/live</c> folder.</summary>
  public const string FileName = SetupOffHand.FileName;

  private static readonly Lazy<IReadOnlyDictionary<string, string>> Union = new(
    Read
  );

  /// <summary>The codes of every manifest mod's allow list; read once.</summary>
  internal static IReadOnlyDictionary<string, string> Allowed => Union.Value;

  /// <summary>Whether the game's off-hand slot takes <paramref name="stack"/>: its storage flags include
  /// Offhand (256).</summary>
  /// <param name="stack">The stack to test.</param>
  /// <returns>True when the slot takes it.</returns>
  public static bool Takes(ItemStack stack) => SetupOffHand.Takes(stack);

  /// <summary>The entries of a mod's allow list that no longer offend.</summary>
  /// <param name="world">The world that resolves the codes.</param>
  /// <param name="modFolder">The mod's folder root; its <c>tests/live/offhand-allow.txt</c> is read.</param>
  /// <returns>One <c>"&lt;code&gt;: &lt;reason&gt;"</c> line per entry that no item or block has the code of,
  /// whose default stack the slot takes, or that no <c>offhand</c> property of a setup or asset JSON and
  /// no code literal in the mod's tests uses; empty without the file.</returns>
  /// <exception cref="InvalidDataException">A JSON file under the mod does not parse.</exception>
  /// <exception cref="ArgumentException">The list is malformed or a code is listed twice.</exception>
  public static IReadOnlyList<string> Stale(
    IWorldAccessor world,
    string modFolder
  )
  {
    string file = Path.Combine(modFolder, "tests", "live", FileName);
    if (!File.Exists(file))
      return [];
    IReadOnlyDictionary<string, string> entries;
    try
    {
      entries = SetupOffHand.Parse(File.ReadAllText(file), file);
    }
    catch (SetupFileException e)
    {
      throw new ArgumentException(e.Message, nameof(modFolder), e);
    }
    HashSet<string> jsonUses = JsonUses(modFolder);
    string[] sources = CodeFiles(modFolder).Select(File.ReadAllText).ToArray();
    var stale = new List<string>();
    foreach (string code in entries.Keys)
    {
      var location = new AssetLocation(code);
      Item? item = world.GetItem(location);
      Block? block = item == null ? world.GetBlock(location) : null;
      if (item == null && block == null)
        stale.Add($"{code}: no item or block has this code");
      else if (
        Takes(item != null ? new ItemStack(item) : new ItemStack(block!))
      )
        stale.Add($"{code}: its default stack already takes the off-hand slot");
      else if (
        !jsonUses.Contains(code)
        && !sources.Any(s =>
          s.Contains($"\"{code}\"", StringComparison.Ordinal)
        )
      )
        stale.Add($"{code}: nothing puts it in the off hand any more");
    }
    return stale;
  }

  private static IReadOnlyDictionary<string, string> Read()
  {
    var union = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (string id in RepoManifest.Mods.Keys)
    {
      string file = Path.Combine(RepoPaths.Mod(id), "tests", "live", FileName);
      if (!File.Exists(file))
        continue;
      foreach (
        (string code, string mechanic) in SetupOffHand.Parse(
          File.ReadAllText(file),
          file
        )
      )
        if (!union.TryAdd(code, mechanic))
          throw new InvalidDataException($"{file}: {code} is listed twice.");
    }
    return union;
  }

  private static HashSet<string> JsonUses(string modFolder)
  {
    var uses = new HashSet<string>(StringComparer.Ordinal);
    foreach (string path in JsonFiles(modFolder))
    {
      JToken root;
      try
      {
        using var reader = new JsonTextReader(
          new StringReader(File.ReadAllText(path))
        )
        {
          DateParseHandling = DateParseHandling.None,
        };
        root = JToken.ReadFrom(reader);
        if (root is not JContainer)
          continue;
      }
      catch (JsonException e)
      {
        throw new InvalidDataException($"{path}: {e.Message}", e);
      }
      foreach (
        JProperty property in ((JContainer)root)
          .Descendants()
          .OfType<JProperty>()
          .Where(p =>
            string.Equals(p.Name, "offhand", StringComparison.OrdinalIgnoreCase)
          )
      )
      {
        JToken value = property.Value;
        if (value.Type == JTokenType.String)
          uses.Add((string)value!);
        else if (value is JObject o)
          foreach (string key in new[] { "code", "stack" })
            if (o[key] is { Type: JTokenType.String } s)
              uses.Add((string)s!);
      }
    }
    return uses;
  }

  private static IEnumerable<string> JsonFiles(string modFolder)
  {
    string live = Path.Combine(modFolder, "tests", "live");
    if (Directory.Exists(live))
      foreach (
        string path in Directory.EnumerateFiles(
          live,
          "*.json",
          SearchOption.AllDirectories
        )
      )
        if (
          !Under(live, path, "recordings") && !Under(live, path, "schematics")
        )
          yield return path;
    string assets = Path.Combine(modFolder, "assets");
    if (Directory.Exists(assets))
      foreach (
        string path in Directory.EnumerateFiles(
          assets,
          "*.json",
          SearchOption.AllDirectories
        )
      )
        yield return path;
  }

  private static IEnumerable<string> CodeFiles(string modFolder)
  {
    string tests = Path.Combine(modFolder, "tests");
    if (!Directory.Exists(tests))
      return [];
    return Directory
      .EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
      .Where(p => !Under(tests, p, "bin") && !Under(tests, p, "obj"));
  }

  private static bool Under(string root, string path, string folder) =>
    Path.GetRelativePath(root, path)
      .Split(Path.DirectorySeparatorChar)
      .Contains(folder);
}
#endif
