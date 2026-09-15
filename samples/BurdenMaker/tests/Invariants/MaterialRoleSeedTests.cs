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

/// <summary>Checks that <see cref="MaterialRoleSeeds"/> mirrors
/// <c>assets/burdenmaker/config/materialroles.json</c> entry for entry.</summary>
public class MaterialRoleSeedTests {
  /// <summary>The role tokens to compare across: <see cref="Roles"/> constants unioned with the
  /// shipped file's own roles.</summary>
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

  /// <summary>One role assignment as a comparable line; codes are domain-normalised, a missing
  /// value is spelled <c>-</c>.</summary>
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
    // Read from the source tree; there is no copy-to-output step for this file.
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

    // Set equality in both directions.
    Assert.Equal(fromAsset, fromSeed);
  }
}
