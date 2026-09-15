using System.Collections.Generic;

namespace ExpandedLib.Catalogues;

/// <summary>
/// One material-role assignment: the data a machine reads to classify a flux, fuel, ore, scrap or
/// charge item. Matches an item by <see cref="Code"/> or by <see cref="PathPrefix"/>; at least one
/// of the two must be set.
/// </summary>
public class MaterialRoleDef {
  /// <summary>The role this def grants, one of the <c>Roles</c> constants in the family layer's
  /// <c>Materials</c> namespace.</summary>
  public string Role { get; set; } = "";

  /// <summary>Exact item code to match ("game:lime"), domain-normalised. Null matches by
  /// <see cref="PathPrefix"/> only.</summary>
  public string? Code { get; set; }

  /// <summary>Matches any item whose <c>Code.Path</c> starts with this, domain-blind. Null matches by
  /// <see cref="Code"/> only.</summary>
  public string? PathPrefix { get; set; }

  /// <summary>Per-role scalar, for example the carbon value of one fuel item. Null makes
  /// <see cref="MaterialRoleRegistry.ValueOf"/> return the caller's fallback.</summary>
  public float? Value { get; set; }

  /// <summary>Mod id this assignment waits on, or null to apply always. A def naming a mod that is
  /// not loaded is skipped silently.</summary>
  public string? RequiresMod { get; set; }
}

/// <summary>The <c>config/materialroles.json</c> file shape: one <c>materials</c> array of role
/// entries, like <see cref="LiquidCatalogue"/>.</summary>
public class MaterialRoleCatalogue {
  /// <summary>The role assignments this file contributes.</summary>
  public List<MaterialRoleDef>? Materials { get; set; }
}
