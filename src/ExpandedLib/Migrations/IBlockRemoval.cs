using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Migrations;

/// <summary>
/// Declares block codes purged from the world: deleted where they sit, and stripped from any container
/// or player inventory holding them as an item stack. Implementations need a public parameterless
/// constructor; <see cref="BlockMigrationModSystem"/> discovers every one and applies the removals.
/// </summary>
public interface IBlockRemoval {
  /// <summary>Short human-readable name, used only for log output.</summary>
  string Name { get; }

  /// <summary>Full, domain-qualified codes of blocks to delete from the world and from inventories;
  /// codes absent in this world are skipped, so returning a superset is safe.</summary>
  IEnumerable<AssetLocation> GetRemovals(ICoreServerAPI api);
}
