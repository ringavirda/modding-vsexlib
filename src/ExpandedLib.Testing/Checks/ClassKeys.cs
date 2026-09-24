using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// Resolves block class keys and block-entity behaviour keys by reflection, for a check source with
/// no game class registry. Scans the given assemblies, the <c>[assembly: ExDomain]</c> assemblies
/// they reference, and ExpandedLib itself. A type is keyed by <see cref="EntityRegistry.KeyFor"/>
/// under each domain its own assembly is given under, so an assembly that declares no
/// <c>ExDomain</c> and was never registered logs that method's warning once per type and domain.
/// </summary>
internal sealed class ClassKeys {
  private readonly Lazy<Dictionary<string, Type>> _blocks;
  private readonly Lazy<Dictionary<string, Type>> _behaviors;
  private readonly Lazy<Dictionary<string, Type>> _bareBehaviors;

  internal ClassKeys(IEnumerable<(string Domain, Assembly Assembly)> sources) {
    (string Domain, Assembly Assembly)[] all = [.. sources];
    _blocks = new(() => Index(all, typeof(Block)));
    _behaviors = new(() => Index(all, typeof(BlockEntityBehavior)));
    _bareBehaviors = new(() => IndexBare(all));
  }

  /// <summary>The block type registered under <paramref name="classKey"/> by
  /// <see cref="EntityRegistry.KeyFor"/> in any of the domains, or null.</summary>
  internal Type? Block(string classKey) =>
    _blocks.Value.GetValueOrDefault(classKey);

  /// <summary>The behaviour type registered under <paramref name="key"/> by
  /// <see cref="EntityRegistry.KeyFor"/>, or, for a key with no mod-id prefix, the type with no
  /// register attribute whose class name is the key; null when none.</summary>
  internal Type? BlockEntityBehavior(string key) =>
    _behaviors.Value.GetValueOrDefault(key)
    ?? (key.Contains('.') ? null : _bareBehaviors.Value.GetValueOrDefault(key));

  private static Dictionary<string, Type> Index(
    (string Domain, Assembly Assembly)[] sources,
    Type baseType
  ) {
    var index = new Dictionary<string, Type>(StringComparer.Ordinal);
    foreach (Type type in Types(sources, baseType)) {
      string[] own =
      [
        .. sources
          .Where(s => s.Assembly == type.Assembly)
          .Select(s => s.Domain),
      ];
      // A referenced assembly declares its own domain, which KeyFor reads before the fallback.
      foreach (string domain in own.Length > 0 ? own : [""])
        index.TryAdd(EntityRegistry.KeyFor(domain, type), type);
    }
    return index;
  }

  private static Dictionary<string, Type> IndexBare(
    (string Domain, Assembly Assembly)[] sources
  ) {
    var index = new Dictionary<string, Type>(StringComparer.Ordinal);
    foreach (Type type in Types(sources, typeof(BlockEntityBehavior)))
      if (!type.GetCustomAttributes().OfType<RegisterAttribute>().Any())
        index.TryAdd(type.Name, type);
    return index;
  }

  private static IEnumerable<Type> Types(
    (string Domain, Assembly Assembly)[] sources,
    Type baseType
  ) {
    var scanned = new HashSet<Assembly> { typeof(BlockNetworkNode).Assembly };
    foreach ((_, Assembly asm) in sources) {
      scanned.Add(asm);
      foreach (AssemblyName name in asm.GetReferencedAssemblies())
        if (
          TryLoad(name) is { } referenced
          && referenced.GetCustomAttribute<ExDomainAttribute>() != null
        )
          scanned.Add(referenced);
    }
    return scanned
      .SelectMany(ReflectionScan.GetCandidateTypes)
      .Where(baseType.IsAssignableFrom);
  }

  private static Assembly? TryLoad(AssemblyName name) {
    try {
      return Assembly.Load(name);
    } catch (Exception e)
        when (e
            is System.IO.FileNotFoundException
              or System.IO.FileLoadException
              or BadImageFormatException
        ) {
      return null;
    }
  }
}
