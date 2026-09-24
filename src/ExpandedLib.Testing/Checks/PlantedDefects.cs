using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
  /// sits on a method that is not a test, or per helper marked without a reason, then one
  /// <c>File: reason</c> line per file whose name is no type's.</param>
  public sealed record Census(
    IReadOnlyList<string> Proven,
    IReadOnlyList<string> Helpers,
    IReadOnlyList<string> Unplanted
  );

  private static readonly Regex PublicTypeDeclaration = new(
    @"^[ \t]*public\s+(?:(?:static|sealed|abstract|partial|readonly|unsafe|ref|new)\s+)*"
      + @"(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+(?<name>\w+)",
    RegexOptions.Compiled | RegexOptions.Multiline
  );

  /// <summary>Sorts the public static members (methods, overloads once; properties; fields) of
  /// the check types in <paramref name="sourceDirectory"/> into proven, helpers and unplanted, in
  /// file-name order, then declaration order.</summary>
  /// <param name="sourceDirectory">Its files hold the check types: the public top-level types of
  /// <paramref name="checks"/> that a file is named after or declares as <c>public</c> in its text.
  /// A file named after no top-level type of <paramref name="checks"/>, public or not, is reported.
  /// Not searched recursively.</param>
  /// <param name="checks">The assembly declaring the check types.</param>
  /// <param name="tests">The assembly whose tests carry <see cref="PlantedDefectAttribute"/>.</param>
  /// <param name="filePattern">Which files hold check types.</param>
  /// <param name="member">When set, only members of this name are surveyed.</param>
  /// <returns>The census; <c>Unplanted</c> is empty when every member is proven or a helper and
  /// every file is named after a type.</returns>
  /// <exception cref="DirectoryNotFoundException"><paramref name="sourceDirectory"/> does not
  /// exist.</exception>
  /// <exception cref="IOException">A file cannot be read.</exception>
  /// <exception cref="InvalidOperationException">No file names or declares a check type.
  /// </exception>
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
    HashSet<string> topLevel =
    [
      .. LoadableTypes(checks)
        .Where(t => t.DeclaringType == null)
        .Select(t => t.Name),
    ];
    var types = new List<Type>();
    var misnamed = new List<string>();
    foreach (
      string file in Directory
        .EnumerateFiles(sourceDirectory, filePattern)
        .Order(StringComparer.Ordinal)
    ) {
      string name = Path.GetFileNameWithoutExtension(file);
      if (!topLevel.Contains(name))
        misnamed.Add(
          $"{name}: the file names no type in {checks.GetName().Name}"
        );
      IEnumerable<string> declared = PublicTypeDeclaration
        .Matches(File.ReadAllText(file))
        .Select(m => m.Groups["name"].Value);
      foreach (string typeName in declared.Prepend(name))
        foreach (Type type in byName[typeName])
          if (!types.Contains(type))
            types.Add(type);
    }
    if (types.Count == 0)
      throw new InvalidOperationException(
        $"no file in {sourceDirectory} matching {filePattern} names or declares a public type of "
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
    unplanted.AddRange(misnamed);
    return new Census(proven, helpers, unplanted);
  }

  /// <summary>Every guard class in <paramref name="invariantsDirectory"/> that proves nothing: it
  /// neither carries a valid <see cref="GuardOfAttribute"/> nor declares a public static member a
  /// <see cref="PlantedDefectAttribute"/> test in <paramref name="suite"/> names.</summary>
  /// <remarks>A guard class is the top-level type of <paramref name="suite"/> named by a
  /// <c>*.cs</c> file in the directory; other types in the file are fixtures. A
  /// <see cref="GuardOfAttribute"/> naming no public static member proves nothing and is
  /// reported.</remarks>
  /// <param name="suite">The test assembly the folder compiles into.</param>
  /// <param name="invariantsDirectory">The suite's <c>Invariants</c> folder; not searched
  /// recursively.</param>
  /// <returns>One line per unproven class, its bare name, or <c>Name: reason</c> for a stray mark
  /// or a file that names no type, in file-name order; empty when every guard is proven.</returns>
  /// <exception cref="DirectoryNotFoundException"><paramref name="invariantsDirectory"/> does not
  /// exist.</exception>
  /// <exception cref="InvalidOperationException">The directory holds no <c>*.cs</c> file.
  /// </exception>
  public static IReadOnlyList<string> Unproven(
    Assembly suite,
    string invariantsDirectory
  ) {
    string[] names =
    [
      .. Directory
        .EnumerateFiles(invariantsDirectory, "*.cs")
        .Select(f => Path.GetFileNameWithoutExtension(f)!)
        .Order(StringComparer.Ordinal),
    ];
    if (names.Length == 0)
      throw new InvalidOperationException(
        $"no *.cs file in {invariantsDirectory} - no guard to prove"
      );

    List<Type> all = [.. LoadableTypes(suite)];
    ILookup<string, Type> byName = all.Where(t => t.DeclaringType == null)
      .ToLookup(t => t.Name, StringComparer.Ordinal);
    var planted = all.SelectMany(t =>
        t.GetMethods(
          BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly
        )
      )
      .Where(IsTest)
      .SelectMany(m => m.GetCustomAttributes<PlantedDefectAttribute>())
      .ToList();

    var unproven = new List<string>();
    foreach (string name in names) {
      Type? guard = byName[name].FirstOrDefault();
      if (guard == null) {
        unproven.Add(
          $"{name}: the file names no type in {suite.GetName().Name}"
        );
        continue;
      }
      GuardOfAttribute[] marks =
      [
        .. guard.GetCustomAttributes<GuardOfAttribute>(),
      ];
      string[] stray =
      [
        .. marks
          .Where(g => !StaticMembers(g.Check).Any(m => m.Name == g.Member))
          .Select(g => $"{g.Check.Name}.{g.Member}"),
      ];
      if (stray.Length > 0)
        unproven.Add(
          $"{name}: [GuardOf] names {string.Join(", ", stray)}, no public static member"
        );
      else if (
        marks.Length == 0
        && !planted.Any(p =>
          p.Check == guard && StaticMembers(guard).Any(m => m.Name == p.Member)
        )
      )
        unproven.Add(name);
    }
    return unproven;
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
