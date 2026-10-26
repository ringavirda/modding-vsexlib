using System;

namespace ExpandedLib.Blocks;

/// <summary>Marks a field or auto-property of a block entity as saved state. A load whose tree lacks
/// the member's <see cref="Key"/> (and <see cref="Legacy"/> key) sets the member to the value it held
/// when the block entity's state was first declared, normally its initializer, whether the tree is an
/// older save or an update in which the sender left a null out.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class PersistAttribute(string? key = null) : Attribute {
  /// <summary>The tree attribute name. Defaults to the member name with a leading underscore
  /// stripped.</summary>
  public string? Key { get; } = key;

  /// <summary>An older key read only when <see cref="Key"/> is absent from the tree. Never
  /// written.</summary>
  public string? Legacy { get; init; }
}
