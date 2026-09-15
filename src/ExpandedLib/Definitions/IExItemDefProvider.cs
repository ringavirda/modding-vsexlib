using System.Collections.Generic;

namespace ExpandedLib.Definitions;

/// <summary>
/// The item-side sibling of <see cref="IExBlockDefProvider"/>: implemented by a class that authors
/// its own code-first item definitions.
/// </summary>
public interface IExItemDefProvider {
  /// <summary>Builds this class's itemtype definition(s) in <paramref name="domain"/> (the mod id).</summary>
  static abstract IEnumerable<ExItemDef> Definitions(string domain);
}
