using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Helpers;

/// <summary>Helpers for reading values a block entity persisted into its attribute tree.</summary>
public static class ExTree {
  /// <summary>Writes a string array as the tree's own <see cref="StringArrayAttribute"/> rather than as JSON text under a string key.</summary>
  public static void SetStrings(
    this ITreeAttribute tree,
    string key,
    string[] values
  ) => tree[key] = new StringArrayAttribute(values);

  /// <summary>Reads back a <see cref="SetStrings"/> array, or <paramref name="fallback"/> when the key holds nothing of that shape.</summary>
  public static string[]? GetStrings(
    this ITreeAttribute tree,
    string key,
    string[]? fallback = null
  ) => (tree[key] as StringArrayAttribute)?.value ?? fallback;

  /// <summary>Deserializes a JSON string a block entity wrote into its tree, returning <paramref name="fallback"/> instead of throwing when the stored text is missing or malformed.</summary>
  public static T SafeDeserialize<T>(string? json, T fallback) {
    if (string.IsNullOrEmpty(json))
      return fallback;

    try {
      return JsonSerializer.Deserialize<T>(json) ?? fallback;
    } catch (JsonException) {
      return fallback;
    }
  }

  /// <summary>What a block entity lost over a save and reload: every key of
  /// <paramref name="saved"/> that <paramref name="reloaded"/> lacks, holds as another attribute
  /// type, or holds at <paramref name="fresh"/>'s value although <paramref name="saved"/>'s value
  /// differed from it. Subtrees are compared key by key.</summary>
  /// <param name="saved">The live instance's tree, written before the reload.</param>
  /// <param name="reloaded">The tree the reloaded instance writes.</param>
  /// <param name="fresh">The tree a newly constructed instance writes; a key it lacks is not checked
  /// for a return to it.</param>
  /// <returns>One line per difference, the key path joined with <c>/</c>, in <paramref name="saved"/>'s
  /// key order; empty when the reload kept everything.</returns>
  internal static IReadOnlyList<string> Differences(
    ITreeAttribute saved,
    ITreeAttribute reloaded,
    ITreeAttribute fresh
  ) {
    var lines = new List<string>();
    Compare(saved, reloaded, fresh, "", lines);
    return lines;
  }

  private static void Compare(
    ITreeAttribute saved,
    ITreeAttribute reloaded,
    ITreeAttribute? fresh,
    string prefix,
    List<string> lines
  ) {
    foreach (KeyValuePair<string, IAttribute> entry in saved) {
      string path = prefix + entry.Key;
      IAttribute? after = reloaded[entry.Key];
      IAttribute? initial = fresh?[entry.Key];
      if (after == null) {
        lines.Add($"{path}: missing after the reload");
        continue;
      }
      if (after.GetType() != entry.Value.GetType()) {
        lines.Add(
          $"{path}: saved as {entry.Value.GetType().Name}, reloaded as {after.GetType().Name}"
        );
        continue;
      }
      if (entry.Value is ITreeAttribute subtree) {
        Compare(
          subtree,
          (ITreeAttribute)after,
          initial as ITreeAttribute,
          path + "/",
          lines
        );
        continue;
      }
      if (
        initial != null
        && !SameValue(entry.Value, initial)
        && SameValue(after, initial)
      )
        lines.Add($"{path}: back at a fresh instance's value after the reload");
    }
  }

  private static bool SameValue(IAttribute a, IAttribute b) =>
    a.GetType() == b.GetType() && Bytes(a).SequenceEqual(Bytes(b));

  private static byte[] Bytes(IAttribute attribute) {
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream))
      attribute.ToBytes(writer);
    return stream.ToArray();
  }
}
