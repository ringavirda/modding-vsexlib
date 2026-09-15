namespace ExpandedLib.Config;

/// <summary>Declares that upgrading a mod into or past <see cref="ToVersion"/> resets the named
/// config properties to their coded defaults. <see cref="FromVersion"/> narrows a migration to a
/// single version transition.</summary>
public sealed class ExConfigMigration {
  /// <summary>The mod version this reset is tied to (e.g. <c>"0.9.2"</c>). The reset fires when the
  /// player first runs a build at or past this version having last saved below it.</summary>
  public required string ToVersion { get; init; }

  /// <summary>Optional lower bound: only fires when the file's stamped version is at or above this.
  /// Null fires for any older version, including unstamped pre-versioning files.</summary>
  public string? FromVersion { get; init; }

  /// <summary>Config property names to reset to their defaults; use <c>nameof</c>. Null or empty
  /// resets the whole config.</summary>
  public string[]? ResetFields { get; init; }
}
