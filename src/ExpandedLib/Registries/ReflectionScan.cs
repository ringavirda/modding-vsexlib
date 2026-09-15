using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>Shared reflection helper for the attribute-driven registries.</summary>
public static class ReflectionScan {
  /// <summary>Returns every concrete (non-abstract) class in <paramref name="asm"/>, tolerating a
  /// partial load.</summary>
  public static Type[] GetCandidateTypes(Assembly asm) {
    try {
      return asm.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false })
        .ToArray();
    } catch (ReflectionTypeLoadException ex) {
      return ex
        .Types.Where(t => t is { IsClass: true, IsAbstract: false })
        .ToArray()!;
    }
  }

  /// <summary>Returns every concrete class across <paramref name="assemblies"/>, sorted by
  /// assembly full name then type full name.</summary>
  public static Type[] GetCandidateTypes(IEnumerable<Assembly> assemblies) =>
    assemblies
      .SelectMany(GetCandidateTypes)
      .OrderBy(t => t.Assembly.FullName, StringComparer.Ordinal)
      .ThenBy(t => t.FullName, StringComparer.Ordinal)
      .ToArray();

  /// <summary>Validates that <paramref name="type"/> is assignable to <typeparamref name="T"/> and,
  /// if so, activates it through its parameterless constructor.</summary>
  public static bool TryActivate<T>(
    ICoreAPI api,
    string modId,
    Type type,
    out T instance
  )
    where T : class {
    if (!typeof(T).IsAssignableFrom(type)) {
      api.Logger.Warning(
        "[{0}] {1} is marked for registration but does not implement {2}; skipped.",
        modId,
        type.FullName,
        typeof(T).Name
      );
      instance = null!;
      return false;
    }

    instance = (T)Activator.CreateInstance(type)!;
    return true;
  }

  /// <summary>Registers every <typeparamref name="TAttr"/>-decorated <typeparamref name="TInstance"/>
  /// in <paramref name="assembly"/> through the shared find-attribute / activate / register loop.</summary>
  public static void ForEachAttributed<TAttr, TInstance>(
    ICoreAPI api,
    string modId,
    Assembly assembly,
    Action<TAttr, TInstance> register
  )
    where TAttr : Attribute
    where TInstance : class {
    foreach (Type type in GetCandidateTypes(assembly)) {
      var attr = type.GetCustomAttribute<TAttr>();
      if (attr == null)
        continue;

      if (!TryActivate<TInstance>(api, modId, type, out TInstance instance))
        continue;

      register(attr, instance);
    }
  }

  /// <summary>Finds every <typeparamref name="TAttr"/>-decorated class in
  /// <paramref name="assembly"/> and hands back the type itself rather than an activated
  /// instance.</summary>
  public static void ForEachAttributed<TAttr>(
    Assembly assembly,
    Action<TAttr, Type> register
  )
    where TAttr : Attribute {
    foreach (Type type in GetCandidateTypes(assembly)) {
      var attr = type.GetCustomAttribute<TAttr>();
      if (attr == null)
        continue;

      register(attr, type);
    }
  }
}
