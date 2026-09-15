using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>The versioning contract every spec attribute carries.</summary>
public static class SpecSchema {
  /// <summary>The key a spec declares its version under.</summary>
  public const string Key = "schema";

  /// <summary>The first form, and what an undeclared <c>schema</c> means.</summary>
  public const int First = 1;

  /// <summary>Reads the declared schema of <paramref name="node"/> against the
  /// <paramref name="current"/> one this build writes. A newer form fails with <paramref name="error"/>.</summary>
  public static bool TryRead(
    JsonObject? node,
    int current,
    out int schema,
    out string? error
  ) {
    error = null;
    schema = node?[Key].AsInt(First) ?? First;

    if (schema < First) {
      error = $"'{Key}' must be at least {First} (was {schema})";
      return false;
    }
    if (schema > current) {
      error =
        $"declares {Key} {schema}, and this build reads up to {current}; update the library rather than "
        + "the declaration";
      return false;
    }
    return true;
  }
}
