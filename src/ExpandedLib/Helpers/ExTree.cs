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
}
