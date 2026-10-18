using System;
using System.Collections.Generic;

namespace ExpandedLib.Registries;

/// <summary>A process-wide, case-insensitive registry of items keyed by a string code derived from
/// each item.</summary>
/// <typeparam name="T">The registered item type.</typeparam>
public sealed class ExKeyedRegistry<T> {
  private readonly Dictionary<string, T> _items = new(
    StringComparer.OrdinalIgnoreCase
  );
  private readonly Dictionary<string, string> _owners = new(
    StringComparer.OrdinalIgnoreCase
  );
  private readonly Func<T, string> _key;
  private readonly Func<T, string> _owner;
  private readonly string _table;

  /// <param name="keySelector">Derives the registry code from an item (e.g. <c>c =&gt; c.ModId</c>).</param>
  public ExKeyedRegistry(Func<T, string> keySelector)
    : this(keySelector, null, null) { }

  /// <param name="keySelector">Derives the registry code from an item (e.g. <c>c =&gt; c.ModId</c>).</param>
  /// <param name="table">Names the registry in the warning a replacement logs; the item type's name
  /// when null.</param>
  /// <param name="owner">Describes an item's registrant in that warning; the item's
  /// <c>ToString()</c> when null.</param>
  public ExKeyedRegistry(
    Func<T, string> keySelector,
    string? table = null,
    Func<T, string>? owner = null
  ) {
    _key = keySelector;
    _table = table ?? typeof(T).Name;
    _owner = owner ?? (i => i?.ToString() ?? typeof(T).Name);
  }

  /// <summary>Whether a replacement stays unlogged, for a table that replaces by design.</summary>
  internal bool Quiet { get; init; }

  /// <summary>Registers an item under its derived code. An item already under that code is
  /// replaced and a Warning naming both registrants is logged, unless the same registrant registers
  /// the key again.</summary>
  public void Register(T item) => Register(item, _owner(item));

  /// <summary>Registers an item under its derived code with <paramref name="owner"/> as its
  /// registrant, named in the Warning a later replacement logs.</summary>
  internal void Register(T item, string owner) {
    string key = _key(item);
    if (
      !Quiet
      && _owners.TryGetValue(key, out string? previous)
      && previous != owner
    )
      RegistryLog.Replaced(_table, key, previous, owner);
    _items[key] = item;
    _owners[key] = owner;
  }

  /// <summary>Registers an item under its derived code, replacing any item there without a
  /// Warning. The item has no registrant, so a later replacement is silent.</summary>
  internal void Update(T item) {
    string key = _key(item);
    _items[key] = item;
    _owners.Remove(key);
  }

  /// <summary>Drops every registered item.</summary>
  public void Clear() {
    _items.Clear();
    _owners.Clear();
  }

  /// <summary>Drops one registered item by code, if present.</summary>
  internal bool Remove(string code) {
    _owners.Remove(code);
    return _items.Remove(code);
  }

  /// <summary>Looks up an item by code (case-insensitive); <c>false</c> when none is registered.</summary>
  public bool TryGet(string code, out T item) =>
    _items.TryGetValue(code, out item!);

  /// <summary>The registered codes.</summary>
  public IReadOnlyCollection<string> Codes => _items.Keys;

  /// <summary>The registered items.</summary>
  public IReadOnlyCollection<T> Values => _items.Values;
}
