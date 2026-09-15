using System.Collections.Generic;

namespace ExpandedLib.Config;

/// <summary>Non-generic view over a config store that lists, reads and sets a mod's tunables by
/// name without knowing the concrete config type.</summary>
public interface IExConfigAccess {
  /// <summary>The owning mod id.</summary>
  string ModId { get; }

  /// <summary>The config file this store reads/writes, for display.</summary>
  string FileName { get; }

  /// <summary>Names of every editable simple-typed value, in declaration order.</summary>
  IReadOnlyList<string> ValueNames { get; }

  /// <summary>Looks up a value by name (case-insensitive) and formats its current value.</summary>
  /// <returns><c>false</c> if no such value.</returns>
  bool TryGet(string name, out string canonicalName, out string value);

  /// <summary>Parses <paramref name="raw"/> into the named value's type, validates it, sets it on
  /// the live config and persists the file.</summary>
  ExConfigEditResult Set(string name, string raw);

  /// <summary>Serializes this store's live config to JSON, exactly as it would be written to disk.</summary>
  string ExportJson();

  /// <summary>Deserializes <paramref name="json"/> into a fresh config, applies the same range
  /// clamping <see cref="ExConfigRegister{TConfig}.Load"/> applies, and makes it the live config.
  /// Never writes to disk.</summary>
  void ImportJson(string json);
}

/// <summary>Outcome category of an <see cref="IExConfigAccess.Set"/> attempt.</summary>
public enum ExConfigEditStatus {
  /// <summary>The value was parsed, validated, set and persisted.</summary>
  Ok,

  /// <summary>No editable value by that name exists in the config.</summary>
  UnknownValue,

  /// <summary>The supplied text could not be parsed into the value's type.</summary>
  ParseFailed,

  /// <summary>The parsed value is out of range (a NaN/infinite/negative number).</summary>
  OutOfRange,
}

/// <summary>The result of an attempted config edit, with enough detail for the command to report it.</summary>
public sealed class ExConfigEditResult {
  /// <summary>What happened.</summary>
  public required ExConfigEditStatus Status { get; init; }

  /// <summary>The config's canonical name for the value (when resolved), else the supplied name.</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>The value before the edit (when the value was resolved).</summary>
  public string? OldValue { get; init; }

  /// <summary>The value after a successful edit.</summary>
  public string? NewValue { get; init; }

  /// <summary>For <see cref="ExConfigEditStatus.ParseFailed"/>: a short name of the expected input
  /// (e.g. <c>"number"</c>, <c>"true/false"</c>).</summary>
  public string? Expected { get; init; }

  /// <summary>For <see cref="ExConfigEditStatus.OutOfRange"/>: the accepted range, <c>"0..1"</c> for a
  /// bounded range or <c>"0+"</c> for a floor only.</summary>
  public string? Range { get; init; }
}
