using System;
using System.Linq;
using System.Reflection;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Metals;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The naming law every catalogue registry follows: <c>Register</c> declares from code,
/// <c>Contribute</c> merges a parsed set, <c>Load</c> reads assets in, <c>Clear</c> empties, and
/// <c>Contributors</c> survives a reload.
/// </summary>
public class CatalogueNamingTests {
  private static Type[] CatalogueRegistryTypes() {
    Assembly asm = typeof(ExpandedLibModSystem).Assembly;
    Type[] byNamespace =
    [
      .. asm.GetTypes()
        .Where(t =>
          t.IsPublic
          && t.Name.EndsWith("Registry", StringComparison.Ordinal)
          && (t.Namespace ?? "").StartsWith(
            "ExpandedLib.Catalogues",
            StringComparison.Ordinal
          )
        ),
    ];
    return [.. byNamespace, typeof(MetalRegistry), typeof(ExLiquids)];
  }

  [Fact]
  public void Every_catalogue_registry_exposes_Clear_and_Contributors() {
    foreach (Type type in CatalogueRegistryTypes()) {
      Assert.True(
        type.GetMethod(
          "Clear",
          BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance
        ) != null,
        $"{type.Name} has no public Clear()"
      );
      Assert.True(
        type.GetProperty(
          "Contributors",
          BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance
        ) != null,
        $"{type.Name} has no public Contributors"
      );
    }
  }

  [Fact]
  public void No_catalogue_registry_exposes_an_Add_or_Load_member() {
    foreach (Type type in CatalogueRegistryTypes())
      foreach (
        MemberInfo member in type.GetMembers(
          BindingFlags.Public
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly
        )
      ) {
        // Skips the [Obsolete] forwarder; deprecating it enforces the law without hiding it.
        if (member.GetCustomAttribute<ObsoleteAttribute>() != null)
          continue;
        Assert.False(
          member.Name.StartsWith("Add", StringComparison.Ordinal),
          $"{type.Name}.{member.Name} starts with 'Add' - Contribute is the verb for merging a parsed set"
        );
        Assert.False(
          member.Name.StartsWith("Load", StringComparison.Ordinal),
          $"{type.Name}.{member.Name} starts with 'Load' - reading assets is the loader's job, not the registry's"
        );
      }
  }
}
