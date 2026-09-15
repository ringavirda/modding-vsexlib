using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Vintagestory.ServerMods.NoObf;

namespace ExpandedLib.Definitions;

/// <summary>
/// The top-level keys the game's object loader reads for a block or item type, taken by reflection
/// from the loader's target types so the set follows the installed game version.
/// </summary>
public static class KnownRootKeys {
  /// <summary>Every top-level key the loader reads for a <c>blocktypes/</c> asset, taken from
  /// <c>BlockType</c> and its bases <c>CollectibleType</c>/<c>RegistryObjectType</c>.</summary>
  public static IReadOnlySet<string> Block { get; } = KeysOf(typeof(BlockType));

  /// <summary>Every top-level key the loader reads for an <c>itemtypes/</c> asset, taken from
  /// <c>ItemType</c> and its bases <c>CollectibleType</c>/<c>RegistryObjectType</c>.</summary>
  public static IReadOnlySet<string> Item { get; } = KeysOf(typeof(ItemType));

  /// <summary>Whether <paramref name="key"/> is a root key the block loader reads.</summary>
  public static bool IsKnownBlockKey(string key) => Block.Contains(key);

  /// <summary>Whether <paramref name="key"/> is a root key the item loader reads.</summary>
  public static bool IsKnownItemKey(string key) => Item.Contains(key);

  // Walks every instance field and property, public or private, of `type` and its bases with
  // DeclaredOnly, keyed by JsonProperty(name) or the lower-cased member name, case-insensitive.
  private static IReadOnlySet<string> KeysOf(Type type) {
    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    const BindingFlags flags =
      BindingFlags.Public
      | BindingFlags.NonPublic
      | BindingFlags.Instance
      | BindingFlags.DeclaredOnly;

    for (Type? level = type; level != null; level = level.BaseType) {
      foreach (FieldInfo field in level.GetFields(flags))
        keys.Add(
          KeyName(field.Name, field.GetCustomAttribute<JsonPropertyAttribute>())
        );
      foreach (PropertyInfo property in level.GetProperties(flags))
        keys.Add(
          KeyName(
            property.Name,
            property.GetCustomAttribute<JsonPropertyAttribute>()
          )
        );
    }

    return keys;
  }

  private static string KeyName(
    string memberName,
    JsonPropertyAttribute? attribute
  ) =>
    !string.IsNullOrEmpty(attribute?.PropertyName)
      ? attribute!.PropertyName!
      : char.ToLowerInvariant(memberName[0]) + memberName[1..];
}
