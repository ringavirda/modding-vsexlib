using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ExpandedLib.Testing;

/// <summary>Guards how tests use the harness: completion forced by reflection, a block double
/// whose behaviours <c>GetBehavior</c> cannot see, a test file's generic helper constrained on a
/// game type, which can stop xUnit discovering the whole assembly, and a guard naming a check it
/// never calls.</summary>
/// <remarks>Each rule reads C# source text with comments blanked; a <c>//</c> inside a string literal
/// blanks the rest of its line.</remarks>
public static class HarnessUse {
  private static readonly Regex ReflectiveWrite = new(
    @"\b(SetProperty|SetField|SetValue)\s*\(|<StructureComplete>k__BackingField",
    RegexOptions.Compiled
  );

  // A PropertyInfo or FieldInfo local bound to StructureComplete by name.
  private static readonly Regex CompletionMemberLocal = new(
    @"\b(?<local>\w+)\s*=(?!=)[^;]*\bGet(?:Property|Field)\s*\([^;]*StructureComplete",
    RegexOptions.Compiled
  );

  // Only a subclass reaches the protected setter.
  private static readonly Regex CompletionSetterWrite = new(
    @"\bStructureComplete\s*=(?![=>])",
    RegexOptions.Compiled
  );

  private static readonly Regex BlockBehaviorsWrite = new(
    @"(?<![\w.])(?:(?<receiver>[\w\[\]]+)!?\s*\.\s*)?BlockBehaviors\s*=(?!=)",
    RegexOptions.Compiled
  );

  private static readonly Regex TestAttribute = new(
    @"\[\s*(Fact|Theory)\b",
    RegexOptions.Compiled
  );

  private static readonly Regex WhereClause = new(
    @"\bwhere\s+(?<param>\w+)\s*:\s*(?<constraints>[^{;]+?)(?=\s*(\bwhere\b|\{|=>|;))",
    RegexOptions.Compiled
  );

  private static readonly Regex GuardOfMark = new(
    @"\bGuardOf\s*\(\s*typeof\s*\(\s*(?<type>[\w.]+)\s*\)\s*,\s*"
      + @"(?:nameof\s*\(\s*(?:[\w.]+\.)?(?<m1>\w+)\s*\)|""(?<m2>\w+)"")\s*\)",
    RegexOptions.Compiled
  );

  private static readonly HashSet<string> GameAssemblies = new(
    StringComparer.Ordinal
  )
  {
    "VintagestoryAPI",
    "VintagestoryLib",
    "VSSurvivalMod",
    "VSEssentials",
    "VSCreativeMod",
  };

  /// <summary>Every statement in <paramref name="sourceFiles"/> that writes a structure's
  /// <c>StructureComplete</c> by reflection (<c>SetProperty</c>, <c>SetField</c>,
  /// <c>SetValue</c>, its backing field by name, or <c>SetValue</c> on a <c>PropertyInfo</c> or
  /// <c>FieldInfo</c> local bound to it by name), or through its setter from a test subclass,
  /// whatever the value.</summary>
  /// <remarks>Completion reached this way skips the monitor that sets it in game; the guard in each
  /// suite holds the files allowed to do it. A reflection local is matched by name across the whole
  /// file.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per statement, <c>file:line: reason</c>; empty when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> CompletionWrites(
    IEnumerable<string> sourceFiles
  ) {
    var offenders = new List<string>();
    foreach (string file in sourceFiles) {
      string text = Uncommented(File.ReadAllText(file));
      string[] locals =
      [
        .. CompletionMemberLocal
          .Matches(text)
          .Select(m => m.Groups["local"].Value)
          .Distinct(),
      ];
      int start = 0;
      foreach (string statement in text.Split(';')) {
        string? reason = null;
        int at = Lead(statement);
        Match? local = locals
          .Select(name =>
            Regex.Match(
              statement,
              @"(?<![\w.])" + Regex.Escape(name) + @"!?\s*\.\s*SetValue\s*\("
            )
          )
          .FirstOrDefault(m => m.Success);
        Match setter = CompletionSetterWrite.Match(statement);
        if (
          statement.Contains("StructureComplete", StringComparison.Ordinal)
          && ReflectiveWrite.IsMatch(statement)
        )
          reason = "StructureComplete written by reflection";
        else if (local != null) {
          reason = "StructureComplete written by reflection through a local";
          at = local.Index;
        } else if (setter.Success) {
          reason = "StructureComplete written through its setter by a subclass";
          at = setter.Index;
        }
        if (reason != null)
          offenders.Add(
            $"{Path.GetFileName(file)}:{LineOf(text, start + at)}: "
              + reason
              + "; build or break the structure instead"
          );
        start += statement.Length + 1;
      }
    }
    return offenders;
  }

  /// <summary>Every assignment to <c>BlockBehaviors</c> in <paramref name="sourceFiles"/> whose
  /// receiver is never assigned <c>CollectibleBehaviors</c> in the same file.
  /// <c>Block.GetBehavior</c> reads <c>CollectibleBehaviors</c>, so a double that fills only
  /// <c>BlockBehaviors</c> answers null to every lookup by type.</summary>
  /// <remarks>Receivers compare as written, ignoring a null-forgiving <c>!</c>; an object
  /// initializer's bare <c>BlockBehaviors =</c> pairs with a bare
  /// <c>CollectibleBehaviors =</c>.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per assignment, <c>file:line: reason</c>; empty when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> HalfBehaviours(
    IEnumerable<string> sourceFiles
  ) {
    var offenders = new List<string>();
    foreach (string file in sourceFiles) {
      string text = Uncommented(File.ReadAllText(file));
      foreach (Match write in BlockBehaviorsWrite.Matches(text)) {
        string receiver = write.Groups["receiver"].Value;
        string pair =
          receiver.Length == 0
            ? @"(?<![\w.])CollectibleBehaviors\s*=(?!=)"
            : @"(?<![\w.])"
              + Regex.Escape(receiver)
              + @"!?\s*\.\s*CollectibleBehaviors\s*=(?!=)";
        if (Regex.IsMatch(text, pair))
          continue;
        string who = receiver.Length == 0 ? "an initializer" : receiver;
        offenders.Add(
          $"{Path.GetFileName(file)}:{LineOf(text, write.Index)}: {who} sets BlockBehaviors "
            + "but not CollectibleBehaviors, which GetBehavior reads"
        );
      }
    }
    return offenders;
  }

  /// <summary>Every <c>where</c> clause in a file of <paramref name="sourceFiles"/> that declares a
  /// <c>[Fact]</c> or <c>[Theory]</c> whose constraint names a game type, or a type deriving from or
  /// implementing one. Such a helper in a test class can make xUnit skip the whole assembly, reporting
  /// a missing game assembly.</summary>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <param name="testAssembly">The assembly the files compile into; constraint names resolve
  /// against it and the assemblies it references. A name that resolves to no type there is not
  /// named.</param>
  /// <returns>One line per clause, <c>file:line: reason</c>; empty when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> GameConstrainedGenerics(
    IEnumerable<string> sourceFiles,
    Assembly testAssembly
  ) {
    ILookup<string, Type> types = VisibleTypes(testAssembly);
    var offenders = new List<string>();
    foreach (string file in sourceFiles) {
      string text = Uncommented(File.ReadAllText(file));
      if (!TestAttribute.IsMatch(text))
        continue;
      foreach (Match clause in WhereClause.Matches(text)) {
        foreach (
          string constraint in TopLevel(clause.Groups["constraints"].Value)
        ) {
          string name = constraint.Split('<')[0].Split('.')[^1].Trim();
          if (!types[name].Any(IsGameType))
            continue;
          offenders.Add(
            $"{Path.GetFileName(file)}:{LineOf(text, clause.Index)}: "
              + $"{clause.Groups["param"].Value} is constrained on the game type {name}; "
              + "write one helper per type"
          );
        }
      }
    }
    return offenders;
  }

  /// <summary>Every <see cref="GuardOfAttribute"/> in <paramref name="sourceFiles"/> whose file
  /// never writes <c>Type.Member</c> for the check it names, outside the attribute itself.</summary>
  /// <remarks>The member counts as called when <c>Type.Member</c> appears anywhere else in the
  /// file, a method group included; the type is compared by its last name segment.</remarks>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per mark, <c>file:line: reason</c>; empty when clean.</returns>
  /// <exception cref="IOException">A file cannot be read, or does not exist.</exception>
  /// <exception cref="UnauthorizedAccessException">A file may not be read.</exception>
  public static IReadOnlyList<string> UncalledGuards(
    IEnumerable<string> sourceFiles
  ) {
    var offenders = new List<string>();
    foreach (string file in sourceFiles) {
      string text = Uncommented(File.ReadAllText(file));
      MatchCollection marks = GuardOfMark.Matches(text);
      if (marks.Count == 0)
        continue;
      var rest = new StringBuilder(text);
      foreach (Match mark in marks)
        for (int i = mark.Index; i < mark.Index + mark.Length; i++)
          if (rest[i] != '\n')
            rest[i] = ' ';
      string body = rest.ToString();
      foreach (Match mark in marks) {
        string type = mark.Groups["type"].Value.Split('.')[^1];
        string member = mark.Groups["m1"].Success
          ? mark.Groups["m1"].Value
          : mark.Groups["m2"].Value;
        string call =
          @"\b"
          + Regex.Escape(type)
          + @"\s*\.\s*"
          + Regex.Escape(member)
          + @"\b";
        if (Regex.IsMatch(body, call))
          continue;
        offenders.Add(
          $"{Path.GetFileName(file)}:{LineOf(text, mark.Index)}: [GuardOf] names "
            + $"{type}.{member}, which the file never calls"
        );
      }
    }
    return offenders;
  }

  private static bool IsGameType(Type type) {
    for (Type? t = type; t != null; t = t.BaseType)
      if (GameAssemblies.Contains(t.Assembly.GetName().Name ?? ""))
        return true;
    try {
      return type.GetInterfaces()
        .Any(i => GameAssemblies.Contains(i.Assembly.GetName().Name ?? ""));
    } catch (TypeLoadException) {
      return false;
    }
  }

  private static ILookup<string, Type> VisibleTypes(Assembly testAssembly) {
    var assemblies = new List<Assembly> { testAssembly };
    foreach (AssemblyName reference in testAssembly.GetReferencedAssemblies()) {
      try {
        assemblies.Add(Assembly.Load(reference));
      } catch (Exception e)
          when (e
              is FileNotFoundException
                or FileLoadException
                or BadImageFormatException
          ) { }
    }
    return assemblies
      .SelectMany(LoadableTypes)
      .ToLookup(
        t => t.IsGenericType ? t.Name.Split('`')[0] : t.Name,
        StringComparer.Ordinal
      );
  }

  private static IEnumerable<Type> LoadableTypes(Assembly assembly) {
    try {
      return assembly.GetTypes();
    } catch (ReflectionTypeLoadException e) {
      return e.Types.Where(t => t != null)!;
    }
  }

  // Comma-separated items outside angle brackets and parentheses, trimmed.
  private static IEnumerable<string> TopLevel(string list) {
    int depth = 0,
      from = 0;
    for (int i = 0; i < list.Length; i++) {
      char c = list[i];
      if (c is '<' or '(')
        depth++;
      else if (c is '>' or ')')
        depth--;
      else if (c == ',' && depth == 0) {
        yield return list[from..i].Trim();
        from = i + 1;
      }
    }
    yield return list[from..].Trim();
  }

  // The text with every // and /* */ comment replaced by spaces, line breaks kept.
  private static string Uncommented(string text) {
    var sb = new StringBuilder(text);
    for (int i = 0; i + 1 < sb.Length; i++) {
      if (sb[i] == '/' && sb[i + 1] == '/') {
        for (; i < sb.Length && sb[i] != '\n'; i++)
          sb[i] = ' ';
      } else if (sb[i] == '/' && sb[i + 1] == '*') {
        for (
          ;
          i < sb.Length
            && !(sb[i] == '*' && i + 1 < sb.Length && sb[i + 1] == '/');
          i++
        )
          if (sb[i] != '\n')
            sb[i] = ' ';
        if (i + 1 < sb.Length) {
          sb[i] = ' ';
          sb[i + 1] = ' ';
        }
      }
    }
    return sb.ToString();
  }

  private static int Lead(string statement) =>
    statement.Length - statement.TrimStart().Length;

  private static int LineOf(string text, int index) =>
    text.Take(Math.Min(index, text.Length)).Count(c => c == '\n') + 1;
}
