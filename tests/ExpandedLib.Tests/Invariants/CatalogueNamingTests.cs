using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Testing;
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

  /// <summary>Each of <paramref name="types"/> lacking a public <c>Clear</c> method or a public
  /// <c>Contributors</c> property, static or instance.</summary>
  /// <returns>One message per missing member, in input order.</returns>
  public static IReadOnlyList<string> MissingClearOrContributors(
    IEnumerable<Type> types
  ) {
    const BindingFlags any =
      BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    var missing = new List<string>();
    foreach (Type type in types) {
      if (type.GetMethod("Clear", any) == null)
        missing.Add($"{type.Name} has no public Clear()");
      if (type.GetProperty("Contributors", any) == null)
        missing.Add($"{type.Name} has no public Contributors");
    }
    return missing;
  }

  /// <summary>Each public member <paramref name="types"/> declare whose name starts with
  /// <c>Add</c> or <c>Load</c>; a member marked <see cref="ObsoleteAttribute"/> is skipped.
  /// </summary>
  /// <returns>One message per member, in input order.</returns>
  public static IReadOnlyList<string> AddOrLoadMembers(IEnumerable<Type> types) {
    var named = new List<string>();
    foreach (Type type in types)
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
        if (member.Name.StartsWith("Add", StringComparison.Ordinal))
          named.Add(
            $"{type.Name}.{member.Name} starts with 'Add' - Contribute is the verb for merging a parsed set"
          );
        if (member.Name.StartsWith("Load", StringComparison.Ordinal))
          named.Add(
            $"{type.Name}.{member.Name} starts with 'Load' - reading assets is the loader's job, not the registry's"
          );
      }
    return named;
  }

  [Fact]
  public void Every_catalogue_registry_exposes_Clear_and_Contributors() {
    IReadOnlyList<string> missing = MissingClearOrContributors(
      Premise.NotEmpty(CatalogueRegistryTypes(), "catalogue registries")
    );

    Assert.True(missing.Count == 0, string.Join("\n", missing));
  }

  [Fact]
  public void No_catalogue_registry_exposes_an_Add_or_Load_member() {
    IReadOnlyList<string> named = AddOrLoadMembers(
      Premise.NotEmpty(CatalogueRegistryTypes(), "catalogue registries")
    );

    Assert.True(named.Count == 0, string.Join("\n", named));
  }

  private static class LawfulRegistry {
    public static int Contributors => 0;

    public static void Clear() { }

    [Obsolete("a forwarder")]
    public static void LoadAll() { }
  }

  private sealed class BareRegistry {
    public void AddMetal() { }

    public static void LoadAll() { }
  }

  // Fails when MissingClearOrContributors passes a registry with neither member or names one
  // declaring both.
  [Fact]
  [PlantedDefect(
    typeof(CatalogueNamingTests),
    nameof(MissingClearOrContributors)
  )]
  public void A_registry_without_Clear_or_Contributors_is_named() {
    Assert.Equal(
      [
        "BareRegistry has no public Clear()",
        "BareRegistry has no public Contributors",
      ],
      MissingClearOrContributors([typeof(LawfulRegistry), typeof(BareRegistry)])
    );
  }

  // Fails when AddOrLoadMembers passes an Add or a Load member, or names an obsolete forwarder.
  [Fact]
  [PlantedDefect(typeof(CatalogueNamingTests), nameof(AddOrLoadMembers))]
  public void An_Add_or_Load_member_is_named() {
    IReadOnlyList<string> named = AddOrLoadMembers([
      typeof(LawfulRegistry),
      typeof(BareRegistry),
    ]);

    Assert.Equal(2, named.Count);
    Assert.Contains(
      named,
      n => n.StartsWith("BareRegistry.AddMetal starts with 'Add'")
    );
    Assert.Contains(
      named,
      n => n.StartsWith("BareRegistry.LoadAll starts with 'Load'")
    );
  }
}
