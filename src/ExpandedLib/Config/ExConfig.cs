using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;

namespace ExpandedLib.Config;

/// <summary>Loads every generated config accessor in an assembly by reflection. Pairs with
/// <c>ExConfigGenerator</c>, which stamps every accessor it emits with
/// <see cref="ExConfigAccessorAttribute"/>.</summary>
public static class ExConfig {
  private const string LoadMethodName = "Load";

  /// <summary>Finds every type in <paramref name="assembly"/> carrying
  /// <see cref="ExConfigAccessorAttribute"/> and invokes its static <c>Load(ICoreAPI)</c>; call once
  /// during mod startup. Returns the number of accessors loaded.</summary>
  public static int LoadAll(ICoreAPI api, Assembly assembly) {
    var accessors = GetCandidateTypes(assembly)
      .Where(t => t.GetCustomAttribute<ExConfigAccessorAttribute>() != null)
      .ToArray();

    int loaded = 0;
    foreach (Type accessor in accessors) {
      MethodInfo? load = accessor.GetMethod(
        LoadMethodName,
        BindingFlags.Public | BindingFlags.Static
      );
      if (load == null) {
        api.Logger.Warning(
          "[exlib] ExConfig.LoadAll: {0} carries [ExConfigAccessor] but has no static Load(ICoreAPI); skipped.",
          accessor.FullName
        );
        continue;
      }

      load.Invoke(null, [api]);
      loaded++;
    }

    if (loaded > 0)
      api.Logger.Notification(
        "[exlib] ExConfig.LoadAll: loaded {0}.",
        string.Join(", ", accessors.Select(t => t.Name))
      );

    return loaded;
  }

  // A generated accessor is a static class, compiled abstract+sealed, which
  // ReflectionScan.GetCandidateTypes excludes; this scans independently.
  private static Type[] GetCandidateTypes(Assembly asm) {
    try {
      return asm.GetTypes().Where(t => t.IsClass).ToArray();
    } catch (ReflectionTypeLoadException ex) {
      return ex.Types.Where(t => t is { IsClass: true }).ToArray()!;
    }
  }
}
