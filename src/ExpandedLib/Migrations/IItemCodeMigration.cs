using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Migrations;

/// <summary>
/// The item counterpart of <see cref="IBlockCodeMigration"/>: declares how item codes from an older
/// version of a mod are rewritten to their current equivalents. Implementations need a public
/// parameterless constructor; <see cref="BlockMigrationModSystem"/> discovers every one and rewrites
/// matching item stacks wherever they are held.
/// </summary>
public interface IItemCodeMigration {
  /// <summary>Short human-readable name, used only for log output.</summary>
  string Name { get; }

  /// <summary>
  /// <c>(oldCode, newCode)</c> pairs of full, domain-qualified item codes; each new code must be a
  /// currently registered item. Pairs whose old or new code is absent in this world are skipped.
  /// </summary>
  IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  );
}
