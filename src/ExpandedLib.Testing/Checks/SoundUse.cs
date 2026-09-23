using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ExpandedLib.Industry.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards the two ways a machine's sound piles up in the game's voice limit: a repeating one-shot
/// (<see cref="ExSounds.PlayThrottled"/>, <see cref="ExSounds.PlayLoop"/>) asked to repeat faster
/// than its clip lasts, and a loaded loop that outlives its block.
/// </summary>
public static class SoundUse {
  private static readonly Regex RepeatCall = new(
    @"ExSounds\.(PlayThrottled|PlayLoop)\s*\(",
    RegexOptions.Compiled
  );

  private static readonly Regex CatalogueSound = new(
    @"^ExSounds\.(\w+)$",
    RegexOptions.Compiled
  );

  private static readonly Regex ClipLengthOf = new(
    @"^ExSounds\.ClipLengthMs\(\s*ExSounds\.(\w+)\s*\)$",
    RegexOptions.Compiled
  );

  /// <summary>Every <see cref="ExSounds.PlayThrottled"/> or <see cref="ExSounds.PlayLoop"/> call in
  /// <paramref name="sourceFiles"/> whose sound is not a catalogue constant with a known clip
  /// length, or whose interval is neither an integer literal of at least that length nor
  /// <c>ExSounds.ClipLengthMs</c> of the same sound.</summary>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per offending call, <c>file:line: reason</c>; empty when clean.</returns>
  public static IReadOnlyList<string> ShortRepeats(
    IEnumerable<string> sourceFiles
  ) {
    var offenders = new List<string>();
    foreach (string file in sourceFiles) {
      string text = File.ReadAllText(file);
      foreach (Match call in RepeatCall.Matches(text)) {
        int line = text.AsSpan(0, call.Index).Count('\n') + 1;
        string where = $"{Path.GetFileName(file)}:{line}";
        List<string>? args = Arguments(text, call.Index + call.Length);
        if (args == null || args.Count < 5) {
          offenders.Add($"{where}: {call.Groups[1].Value} call not parsed");
          continue;
        }
        string? reason = RepeatFault(args[2], args[4]);
        if (reason != null)
          offenders.Add($"{where}: {reason}");
      }
    }
    return offenders;
  }

  /// <summary>Every type in <paramref name="assembly"/> that holds an <see cref="ILoadedSound"/>
  /// field itself (only <see cref="ExSoundLoop"/> may), and every type holding an
  /// <see cref="ExSoundLoop"/> field whose own <c>OnBlockRemoved()</c> or <c>OnBlockUnloaded()</c>
  /// override is missing or never reaches that field, directly or through a method of the same
  /// type.</summary>
  /// <returns>One line per offending type and field; empty when clean.</returns>
  public static IReadOnlyList<string> UndisposedLoops(Assembly assembly) {
    var offenders = new List<string>();
    foreach (Type type in LoadableTypes(assembly)) {
      if (type == typeof(ExSoundLoop))
        continue;
      foreach (FieldInfo field in OwnFields(type)) {
        if (typeof(ILoadedSound).IsAssignableFrom(field.FieldType)) {
          offenders.Add(
            $"{type.FullName}.{field.Name}: holds an ILoadedSound; hold an ExSoundLoop"
          );
          continue;
        }
        if (field.FieldType != typeof(ExSoundLoop))
          continue;
        foreach (string hook in new[] { "OnBlockRemoved", "OnBlockUnloaded" }) {
          MethodInfo? method = type.GetMethod(
            hook,
            BindingFlags.Instance
              | BindingFlags.Public
              | BindingFlags.NonPublic
              | BindingFlags.DeclaredOnly,
            Type.EmptyTypes
          );
          if (method == null)
            offenders.Add(
              $"{type.FullName}.{field.Name}: no {hook}() override disposes it"
            );
          else if (!Reaches(method, field, type, 3))
            offenders.Add(
              $"{type.FullName}.{field.Name}: {hook}() never reaches it"
            );
        }
      }
    }
    return offenders;
  }

  private static string? RepeatFault(string soundArg, string intervalArg) {
    Match sound = CatalogueSound.Match(soundArg);
    if (!sound.Success)
      return $"sound '{soundArg}' is not an ExSounds constant";
    long clip = ClipLength(sound.Groups[1].Value);
    if (clip <= 0)
      return $"ExSounds.{sound.Groups[1].Value} has no clip length";
    Match named = ClipLengthOf.Match(intervalArg);
    if (named.Success)
      return named.Groups[1].Value == sound.Groups[1].Value
        ? null
        : $"interval is the clip length of ExSounds.{named.Groups[1].Value}, not of the sound played";
    if (!long.TryParse(intervalArg.Replace("_", ""), out long interval))
      return $"interval '{intervalArg}' is neither a literal nor ExSounds.ClipLengthMs";
    return interval >= clip
      ? null
      : $"ExSounds.{sound.Groups[1].Value} repeats every {interval} ms but lasts {clip} ms";
  }

  private static long ClipLength(string name) =>
    typeof(ExSounds).GetField(name, BindingFlags.Public | BindingFlags.Static)
      ?.GetValue(null)
      is AssetLocation location
      ? ExSounds.ClipLengthMs(location)
      : 0;

  // The top-level comma-separated arguments from just past an opening parenthesis, or null when
  // it never closes.
  private static List<string>? Arguments(string text, int start) {
    var args = new List<string>();
    int depth = 0,
      from = start;
    for (int i = start; i < text.Length; i++) {
      char c = text[i];
      if (c == '(' || c == '[' || c == '{')
        depth++;
      else if ((c == ')' || c == ']' || c == '}') && depth > 0)
        depth--;
      else if (c == ')' || (c == ',' && depth == 0)) {
        args.Add(Clean(text[from..i]));
        from = i + 1;
        if (c == ')')
          return args;
      }
    }
    return null;
  }

  private static string Clean(string arg) {
    string trimmed = Regex.Replace(arg, @"//[^\n]*", "").Trim();
    return Regex.Replace(trimmed, @"\s+", " ");
  }

  private static IEnumerable<Type> LoadableTypes(Assembly assembly) {
    try {
      return assembly.GetTypes();
    } catch (ReflectionTypeLoadException e) {
      return e.Types.Where(t => t != null)!;
    }
  }

  private static IEnumerable<FieldInfo> OwnFields(Type type) {
    try {
      return type.GetFields(
        BindingFlags.Instance
          | BindingFlags.Public
          | BindingFlags.NonPublic
          | BindingFlags.DeclaredOnly
      );
    } catch (TypeLoadException) {
      return [];
    }
  }

  // Whether method's IL loads or stores field, following calls into owner's own methods up to
  // depth levels.
  private static bool Reaches(
    MethodBase method,
    FieldInfo field,
    Type owner,
    int depth
  ) {
    byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
    if (il == null)
      return false;
    Module module = method.Module;
    for (int i = 0; i + 4 < il.Length; i++) {
      byte op = il[i];
      bool fieldOp = op is 0x7B or 0x7C or 0x7D;
      bool callOp = op is 0x28 or 0x6F;
      if (!fieldOp && !callOp)
        continue;
      int token = BitConverter.ToInt32(il, i + 1);
      try {
        if (fieldOp && module.ResolveField(token) == field)
          return true;
        if (
          callOp
          && depth > 0
          && module.ResolveMethod(token) is { } callee
          && callee.DeclaringType == owner
          && callee != method
          && Reaches(callee, field, owner, depth - 1)
        )
          return true;
      } catch (Exception e)
        when (e is ArgumentException or BadImageFormatException) { }
    }
    return false;
  }
}
