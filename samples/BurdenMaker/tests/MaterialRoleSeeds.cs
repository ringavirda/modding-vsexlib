using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Materials;

namespace BurdenMaker.Tests;

/// <summary>
/// Seeds <see cref="MaterialRoleRegistry"/> with the sample's default material-role assignments: the
/// headless equivalent of loading <c>assets/burdenmaker/config/materialroles.json</c>, which the test
/// harness has no asset pipeline to read. Mirrors that file entry for entry. Call once from the test
/// assembly's module initializer. Idempotent.
/// </summary>
public static class MaterialRoleSeeds {
  private static bool _seeded;

  /// <summary>Idempotently registers the sample's default roles (clearing first for a clean slate).</summary>
  public static void SeedDefaults() {
    if (_seeded)
      return;
    _seeded = true;

    MaterialRoleRegistry.Clear();
    MaterialRoleRegistry.Register(
      new MaterialRoleDef { Role = Roles.Flux, Code = "game:lime" }
    );
    MaterialRoleRegistry.Register(
      new MaterialRoleDef { Role = Roles.IronOre, PathPrefix = "crushed-iron" }
    );
  }
}
