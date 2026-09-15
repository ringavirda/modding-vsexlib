using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ExpandedLib.Testing;

/// <summary>
/// Checks that every <c>[Collection("...")]</c> name has a matching
/// <c>[CollectionDefinition("...", ...)]</c> in the same assembly.
/// </summary>
public static class StaticStateCollection {
  /// <summary>
  /// Asserts that every <c>[Collection("...")]</c> name in <paramref name="assembly"/> has a matching
  /// <c>[CollectionDefinition("...", ...)]</c> in the same assembly.
  /// </summary>
  /// <exception cref="InvalidOperationException">At least one collection name is used but never
  /// defined.</exception>
  public static void EveryCollectionNameHasADefinition(Assembly assembly) {
    var used = new HashSet<string>(StringComparer.Ordinal);
    var defined = new HashSet<string>(StringComparer.Ordinal);

    foreach (Type type in SafeTypes(assembly)) {
      foreach (string name in AttributeNames<CollectionAttribute>(type))
        used.Add(name);
      foreach (
        string name in AttributeNames<CollectionDefinitionAttribute>(type)
      )
        defined.Add(name);
    }

    var undefined = used.Except(defined)
      .OrderBy(n => n, StringComparer.Ordinal)
      .ToList();
    if (undefined.Count > 0)
      throw new InvalidOperationException(
        $"{undefined.Count} collection name(s) are used by [Collection(...)] but never declared by a "
          + $"[CollectionDefinition(...)] in {assembly.GetName().Name}, so each one silently runs as "
          + "its own unsynchronised collection: "
          + string.Join(", ", undefined)
      );
  }

  // Reads through CustomAttributeData, immune to the attribute's property shape across package
  // versions.
  private static IEnumerable<string> AttributeNames<TAttribute>(Type type)
    where TAttribute : Attribute =>
    CustomAttributeData
      .GetCustomAttributes(type)
      .Where(a => a.AttributeType == typeof(TAttribute))
      .Select(a => (string)a.ConstructorArguments[0].Value!);

  private static IEnumerable<Type> SafeTypes(Assembly assembly) {
    try {
      return assembly.GetTypes();
    } catch (ReflectionTypeLoadException ex) {
      return ex.Types.Where(t => t is not null).Select(t => t!);
    }
  }
}
