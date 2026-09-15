using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ExpandedLib.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Testing;

/// <summary>Golden-file oracle for a block entity's save shape: <see cref="Of"/> reads back the keys
/// a fresh instance writes, and <see cref="AssertGolden"/> checks that list against a committed golden.</summary>
public static class TreeKeys {
  /// <summary>The keys <paramref name="be"/> writes into <c>ToTreeAttributes</c>, sorted, each
  /// qualified with its attribute type ("temp:float"); a nested tree's keys are listed as
  /// "parent/child".</summary>
  public static IReadOnlyList<string> Of(BlockEntity be) {
    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);
    var keys = new List<string>();
    Collect(tree, "", keys);
    keys.Sort(StringComparer.Ordinal);
    return keys;
  }

  private static void Collect(
    ITreeAttribute tree,
    string prefix,
    List<string> into
  ) {
    foreach (KeyValuePair<string, IAttribute> entry in tree) {
      string path = prefix + entry.Key;
      if (entry.Value is ITreeAttribute sub)
        Collect(sub, path + "/", into);
      else
        into.Add($"{path}:{TypeTag(entry.Value)}");
    }
  }

  // The attribute's own class name, minus the "Attribute" suffix and lowercased.
  private static string TypeTag(IAttribute attr) {
    string name = attr.GetType().Name;
    return (
      name.EndsWith("Attribute") ? name[..^"Attribute".Length] : name
    ).ToLowerInvariant();
  }

  /// <summary>Asserts that <paramref name="be"/>'s current <see cref="Of"/> matches the committed
  /// golden at <c>mods/&lt;mod&gt;/tests/goldens/&lt;domain&gt;/treekeys/&lt;ClassName&gt;.txt</c>, one
  /// key per line.</summary>
  public static void AssertGolden(BlockEntity be, string domain) {
    string className = be.GetType().Name;
    string file = DefinitionGoldens.SolutionRelative(
      $"mods/{domain}/tests/goldens/{domain}/treekeys/{className}.txt"
    );
    IReadOnlyList<string> actual = Of(be);

    if (DefinitionGoldens.WriteRequested) {
      Directory.CreateDirectory(Path.GetDirectoryName(file)!);
      File.WriteAllLines(file, actual);
      return;
    }

    if (!File.Exists(file))
      throw new InvalidOperationException($"missing tree-key golden: {file}");
    string[] expected = File.ReadAllLines(file);
    if (!expected.SequenceEqual(actual))
      throw new InvalidOperationException(
        $"{className} tree keys diverged from its golden {file}:\n"
          + $"expected: {string.Join(", ", expected)}\n"
          + $"actual:   {string.Join(", ", actual)}"
      );
  }

  /// <summary>Guards the "call <c>base.DeclareState</c> first" convention: for each type in
  /// <paramref name="be"/>'s hierarchy that overrides <c>DeclareState</c>, asserts every key that
  /// level declares also shows up in the instance's real, built <c>Persisted</c> state.</summary>
  public static void AssertDeclaresBaseKeys(BlockEntity be) {
    ExBlockState real = RealStateOf(be);
    var realKeys = new HashSet<string>(real.Keys, StringComparer.Ordinal);

    for (
      Type? level = be.GetType();
      level != null && level != typeof(object);
      level = level.BaseType
    ) {
      MethodInfo? method = level.GetMethod(
        "DeclareState",
        BindingFlags.Instance
          | BindingFlags.Public
          | BindingFlags.NonPublic
          | BindingFlags.DeclaredOnly,
        null,
        [typeof(ExBlockState)],
        null
      );
      if (method == null || method.IsAbstract)
        continue;

      var isolated = new ExBlockState();
      InvokeNonVirtual(method, be, isolated);

      string[] missing = [.. isolated.Keys.Where(k => !realKeys.Contains(k))];
      if (missing.Length > 0)
        throw new InvalidOperationException(
          $"{level.Name}.DeclareState declares key(s) {string.Join(", ", missing)} that "
            + $"{be.GetType().Name}'s built state does not have - an override between {level.Name} "
            + $"and {be.GetType().Name} skipped its base.DeclareState(state) call."
        );
    }
  }

  // The real, built Persisted state, found by name: each Persisted base declares its own property.
  private static ExBlockState RealStateOf(BlockEntity be) {
    PropertyInfo? prop = be.GetType()
      .GetProperty(
        "Persisted",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
      );
    if (prop?.GetValue(be) is not ExBlockState state)
      throw new InvalidOperationException(
        $"{be.GetType().Name} has no accessible \"Persisted\" property to check against."
      );
    return state;
  }

  // Calls method on instance without virtual dispatch, via an IL trampoline emitting `call`
  // (not `callvirt`); MethodInfo.Invoke always dispatches to the most-derived override.
  private static void InvokeNonVirtual(
    MethodInfo method,
    object instance,
    ExBlockState state
  ) {
    var trampoline = new DynamicMethod(
      "NonVirtual_" + method.DeclaringType!.Name + "_DeclareState",
      null,
      [typeof(object), typeof(ExBlockState)],
      method.DeclaringType,
      skipVisibility: true
    );
    ILGenerator il = trampoline.GetILGenerator();
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Castclass, method.DeclaringType);
    il.Emit(OpCodes.Ldarg_1);
    il.Emit(OpCodes.Call, method);
    il.Emit(OpCodes.Ret);
    var call =
      (Action<object, ExBlockState>)
        trampoline.CreateDelegate(typeof(Action<object, ExBlockState>));
    call(instance, state);
  }
}
