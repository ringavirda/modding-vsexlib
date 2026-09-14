using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Materials;
using ExpandedLib.Testing;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Xunit;

namespace BurdenMaker.Tests;

/// <summary>
/// Checks the claim <see cref="MaterialRoleSeeds"/> makes: that it mirrors
/// <c>assets/burdenmaker/config/materialroles.json</c> entry for entry. Nothing else in the suite loads
/// or compares that file - every other test gates on roles registered in C# - so a wrong role token or
/// prefix in the shipped JSON would otherwise be invisible and the ore hopper would silently refuse
/// everything in game.
/// </summary>
public class MaterialRoleSeedTests {
  /// <summary>The role tokens to compare across: the <see cref="Roles"/> constants unioned with the
  /// shipped file's own roles, since a mod may invent a role string. The union makes this a set
  /// comparison rather than a spot check.</summary>
  private static IEnumerable<string> RoleTokens(
    IEnumerable<MaterialRoleDef> shipped
  ) =>
    typeof(Roles)
      .GetFields(BindingFlags.Public | BindingFlags.Static)
      .Where(f => f.IsLiteral && f.FieldType == typeof(string))
      .Select(f => (string)f.GetRawConstantValue()!)
      .Concat(shipped.Select(d => d.Role))
      .Select(r => r.ToLowerInvariant())
      .Distinct(StringComparer.Ordinal);

  /// <summary>One role assignment as a comparable line. Codes are domain-normalised (the registry
  /// matches that way, so <c>lime</c> and <c>game:lime</c> are the same row), and a missing value is
  /// spelled <c>-</c> rather than defaulted - "no value" is a distinct fact from "value 1".</summary>
  private static string Row(MaterialRoleDef def) =>
    string.Join(
      " | ",
      def.Role.ToLowerInvariant(),
      def.Code is { Length: > 0 } c ? new AssetLocation(c).ToString() : "",
      def.PathPrefix ?? "",
      def.Value.HasValue
        ? def.Value.Value.ToString("0.####", CultureInfo.InvariantCulture)
        : "-"
    );

  [Fact]
  public void Seed_matches_the_shipped_materialroles_json() {
    // MaterialRoleSeeds exists because the harness has no asset pipeline, and claims to mirror
    // assets/burdenmaker/config/materialroles.json entry for entry. Read from the source tree, not
    // from a copied-to-output asset: there is no copy step for this file.
    string path = Path.Combine(
      RepoPaths.Assets("burdenmaker"),
      "config",
      "materialroles.json"
    );
    Assert.True(File.Exists(path), $"missing shipped material roles at {path}");

    List<MaterialRoleDef> shipped =
      JsonConvert
        .DeserializeObject<MaterialRoleCatalogue>(File.ReadAllText(path))
        ?.Materials
      ?? throw new InvalidOperationException($"no 'materials' array in {path}");

    List<string> fromAsset = shipped
      .Where(def => string.IsNullOrEmpty(def.RequiresMod))
      .Select(Row)
      .OrderBy(r => r, StringComparer.Ordinal)
      .ToList();

    List<string> fromSeed = RoleTokens(shipped)
      .SelectMany(MaterialRoleRegistry.OfRole)
      .Select(Row)
      .OrderBy(r => r, StringComparer.Ordinal)
      .ToList();

    // Set equality in both directions: a one-way "every asset row is seeded" check would miss a seed
    // row that no longer ships.
    Assert.Equal(fromAsset, fromSeed);
  }
}
