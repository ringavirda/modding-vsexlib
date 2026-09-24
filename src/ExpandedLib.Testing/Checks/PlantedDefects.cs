using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ExpandedLib.Testing;

/// <summary>
/// Whether each check proves it can fail: a rule is proven by a test marked
/// <see cref="PlantedDefectAttribute"/>, a helper is marked <see cref="CheckHelperAttribute"/>, and
/// anything else is unplanted.
/// </summary>
public static class PlantedDefects {
  /// <summary>What <see cref="Survey"/> found, each list as <c>Type.Member</c> in file order.
  /// </summary>
  /// <param name="Proven">Members a planted-defect test names.</param>
  /// <param name="Helpers">Members marked <see cref="CheckHelperAttribute"/> and named by no
  /// planted-defect test.</param>
  /// <param name="Unplanted">Members neither proven nor marked, then one line per
  /// <see cref="PlantedDefectAttribute"/> that names no public static member of a surveyed type,
  /// sits on a method that is not a test, or per helper marked without a reason.</param>
  public sealed record Census(
    IReadOnlyList<string> Proven,
    IReadOnlyList<string> Helpers,
    IReadOnlyList<string> Unplanted
  );

  /// <summary>Sorts the public static members (methods, overloads once; properties; fields) of
  /// the check types in <paramref name="sourceDirectory"/> into proven, helpers and unplanted, in
  /// file-name order, then declaration order.</summary>
  /// <param name="sourceDirectory">Its files name the check types: the public top-level types of
  /// <paramref name="checks"/> of those names. Not searched recursively.</param>
  /// <param name="checks">The assembly declaring the check types.</param>
  /// <param name="tests">The assembly whose tests carry <see cref="PlantedDefectAttribute"/>.</param>
  /// <param name="filePattern">Which files name check types.</param>
  /// <param name="member">When set, only members of this name are surveyed.</param>
  /// <returns>The census; <c>Unplanted</c> is empty when every member is proven or a helper.</returns>
  /// <exception cref="DirectoryNotFoundException"><paramref name="sourceDirectory"/> does not
  /// exist.</exception>
  /// <exception cref="InvalidOperationException">No file names a check type.</exception>
  public static Census Survey(
    string sourceDirectory,
    Assembly checks,
    Assembly tests,
    string filePattern = "*.cs",
    string? member = null
  ) {
    ILookup<string, Type> byName = checks
      .GetExportedTypes()
      .Where(t => t.DeclaringType == null)
      .ToLookup(t => t.Name, StringComparer.Ordinal);
    List<Type> types =
    [
      .. Directory
        .EnumerateFiles(sourceDirectory, filePattern)
        .Select(Path.GetFileNameWithoutExtension)
        .Order(StringComparer.Ordinal)
        .SelectMany(name => byName[name!]),
    ];
    if (types.Count == 0)
      throw new InvalidOperationException(
        $"no file in {sourceDirectory} matching {filePattern} names a public type of "
          + $"{checks.GetName().Name} - nothing to survey"
      );

    var planted = new List<(MethodInfo Test, PlantedDefectAttribute Mark)>();
    foreach (Type type in LoadableTypes(tests))
      foreach (
        MethodInfo method in type.GetMethods(
          BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly
        )
      )
        foreach (
          PlantedDefectAttribute mark in method.GetCustomAttributes<PlantedDefectAttribute>()
        )
          planted.Add((method, mark));

    var proven = new List<string>();
    var helpers = new List<string>();
    var unplanted = new List<string>();
    foreach (Type type in types)
      foreach (
        IGrouping<string, MemberInfo> named in StaticMembers(type)
          .Where(m => member == null || m.Name == member)
          .GroupBy(m => m.Name)
      ) {
        string key = $"{type.Name}.{named.Key}";
        CheckHelperAttribute? helper = named
          .Select(m => m.GetCustomAttribute<CheckHelperAttribute>())
          .FirstOrDefault(h => h != null);
        if (
          planted.Any(p =>
            p.Mark.Check == type && p.Mark.Member == named.Key && IsTest(p.Test)
          )
        )
          proven.Add(key);
        else if (helper != null) {
          helpers.Add(key);
          if (string.IsNullOrWhiteSpace(helper.Reason))
            unplanted.Add($"{key}: marked [CheckHelper] without a reason");
        } else
          unplanted.Add(key);
      }

    foreach (var (test, mark) in planted) {
      if (!types.Contains(mark.Check))
        continue;
      string key = $"{mark.Check.Name}.{mark.Member}";
      string at = $"{test.DeclaringType?.Name}.{test.Name}";
      if (!IsTest(test))
        unplanted.Add(
          $"{key}: [PlantedDefect] on {at}, which is not a [Fact] or [Theory]"
        );
      else if (!StaticMembers(mark.Check).Any(m => m.Name == mark.Member))
        unplanted.Add(
          $"{key}: [PlantedDefect] on {at} names no public static member"
        );
    }
    return new Census(proven, helpers, unplanted);
  }

  internal static bool IsTest(MethodInfo method) =>
    method.GetCustomAttributes<FactAttribute>(inherit: true).Any();

  internal static IEnumerable<Type> LoadableTypes(Assembly assembly) {
    try {
      return assembly.GetTypes();
    } catch (ReflectionTypeLoadException e) {
      return e.Types.Where(t => t != null)!;
    }
  }

  // Public static methods (property accessors and operators excluded), properties and fields.
  private static IEnumerable<MemberInfo> StaticMembers(Type type) =>
    type.GetMembers(
        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
      )
      .Where(m =>
        m switch {
          MethodInfo method => !method.IsSpecialName,
          PropertyInfo or FieldInfo => true,
          _ => false,
        }
      )
      .Where(m => !m.Name.Contains('<'))
      .OrderBy(m => m.MetadataToken);
}
