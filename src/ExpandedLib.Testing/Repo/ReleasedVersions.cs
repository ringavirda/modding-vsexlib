using System.Collections.Generic;

namespace ExpandedLib.Testing;

/// <summary>
/// The highest version published per modid, hand-edited when a release goes out. Cannot be derived
/// from <c>dist/Releases/</c>, gitignored build output that holds only the newest local build.
/// </summary>
public static class ReleasedVersions {
  /// <summary>modid -> the highest version ever published under it, across every branch.</summary>
  public static IReadOnlyDictionary<string, string> HighestPublished =>
    ReleasedHistory.AllVersions;
}
