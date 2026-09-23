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
/// Guards machine sounds: a repeating one-shot asked to repeat faster than its clip lasts, a loaded
/// loop that outlives its block, and a sound played or loaded past <see cref="ExSounds"/>, which
/// escapes <see cref="ExSounds.MachineVolume"/>.
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

  private static readonly Regex DirectCall = new(
    @"\.(PlaySound\w*|LoadSound)\s*\(",
    RegexOptions.Compiled
  );

  // Path suffix, '/'-separated, to the reason the file may play or load a sound itself.
  private static readonly Dictionary<string, string> DirectCallAllowed = new() {
    ["ExpandedLib.Industry/Helpers/ExSounds.cs"] =
      "the helpers every other sound goes through; they apply MachineVolume and the sound type",
  };

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
        int line = text.Take(call.Index).Count(c => c == '\n') + 1;
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

  /// <summary>Every direct <c>PlaySound*</c> or <c>LoadSound</c> call in
  /// <paramref name="sourceFiles"/> outside the files the guard allows (<see cref="ExSounds"/>
  /// itself); such a sound skips <see cref="ExSounds.MachineVolume"/> and the machine sound type.
  /// Lines that start with <c>//</c> are skipped.</summary>
  /// <param name="sourceFiles">C# files to read; each is read whole.</param>
  /// <returns>One line per call, <c>file:line: reason</c>; empty when clean.</returns>
  public static IReadOnlyList<string> DirectSounds(
    IEnumerable<string> sourceFiles
  ) {
    var offenders = new List<string>();
    foreach (string file in sourceFiles) {
      string path = file.Replace('\\', '/');
      if (
        DirectCallAllowed.Keys.Any(k =>
          path.EndsWith("/" + k, StringComparison.Ordinal)
        )
      )
        continue;
      string[] lines = File.ReadAllLines(file);
      for (int i = 0; i < lines.Length; i++) {
        if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
          continue;
        foreach (Match call in DirectCall.Matches(lines[i]))
          offenders.Add(
            $"{Path.GetFileName(file)}:{i + 1}: {call.Groups[1].Value} called directly; use ExSounds"
          );
      }
    }
    return offenders;
  }

  /// <summary>Every type in <paramref name="assembly"/> that holds an <see cref="ILoadedSound"/>
  /// field itself (only <see cref="ExSoundLoop"/> may), and every type holding an
  /// <see cref="ExSoundLoop"/> field whose own <c>OnBlockRemoved()</c> or <c>OnBlockUnloaded()</c>
  /// override is missing or never calls <see cref="ExSoundLoop.Dispose"/> on that field, directly or
  /// through a method of the same type.</summary>
  /// <remarks>Only <c>field.Dispose()</c> and <c>field?.Dispose()</c> count; a loop copied to a local
  /// first, or disposed through another type, is named.</remarks>
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
          else if (!Disposes(method, field, type, 3))
            offenders.Add(
              $"{type.FullName}.{field.Name}: {hook}() never disposes it"
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
    typeof(ExSounds)
      .GetField(name, BindingFlags.Public | BindingFlags.Static)
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

  // Whether method's IL calls ExSoundLoop.Dispose on field, directly or through owner's own methods
  // up to depth levels.
  private static bool Disposes(
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
      if (op is not (0x7B or 0x28 or 0x6F))
        continue;
      int token = BitConverter.ToInt32(il, i + 1);
      try {
        if (
          op == 0x7B
          && module.ResolveField(token) == field
          && DisposeAt(il, module, ReceiverUse(il, i + 5))
        )
          return true;
        if (
          op != 0x7B
          && depth > 0
          && module.ResolveMethod(token) is { } callee
          && callee.DeclaringType == owner
          && callee != method
          && Disposes(callee, field, owner, depth - 1)
        )
          return true;
      } catch (Exception e)
          when (e is ArgumentException or BadImageFormatException) { }
    }
    return false;
  }

  // Where the value a ldfld leaves at `next` is consumed: next itself, or the branch target of the
  // `dup; brtrue` a null-conditional call compiles to.
  private static int ReceiverUse(byte[] il, int next) =>
    next + 2 < il.Length && il[next] == 0x25 && il[next + 1] == 0x2D
      ? next + 3 + (sbyte)il[next + 2]
      : next;

  private static bool DisposeAt(byte[] il, Module module, int at) =>
    at >= 0
    && at + 4 < il.Length
    && il[at] is 0x28 or 0x6F
    && module.ResolveMethod(BitConverter.ToInt32(il, at + 1))
      is { Name: nameof(ExSoundLoop.Dispose) } called
    && called.DeclaringType == typeof(ExSoundLoop);
}
