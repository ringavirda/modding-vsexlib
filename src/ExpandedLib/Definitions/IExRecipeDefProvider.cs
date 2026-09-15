using System.Collections.Generic;

namespace ExpandedLib.Definitions;

/// <summary>
/// The recipe-side sibling of <see cref="IExBlockDefProvider"/> and <see cref="IExItemDefProvider"/>:
/// implemented by a class that authors its own code-first recipe files.
/// </summary>
public interface IExRecipeDefProvider {
  /// <summary>Builds this class's recipe file(s) in <paramref name="domain"/> (the mod id).</summary>
  static abstract IEnumerable<ExRecipeDef> Definitions(string domain);
}
