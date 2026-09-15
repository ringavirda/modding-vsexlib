using System.Collections.Generic;

namespace ExpandedLib.Definitions;

/// <summary>
/// Implemented by a block class that authors its own code-first definitions.
/// A class may return several defs when one C# class backs several blocktype assets.
/// </summary>
public interface IExBlockDefProvider {
  /// <summary>Builds this class's blocktype definitions in <paramref name="domain"/> (the mod id).</summary>
  static abstract IEnumerable<ExBlockDef> Definitions(string domain);
}
