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
/// they reference, and ExpandedLib itself.
/// </summary>
internal sealed class ClassKeys {
  private readonly Lazy<Dictionary<string, Type>> _blocks;
  private readonly Lazy<Dictionary<string, Type>> _behaviors;

  internal ClassKeys(IEnumerable<(string Domain, Assembly Assembly)> sources) {
    (string Domain, Assembly Assembly)[] all = [.. sources];
    _blocks = new(() => IndexBlocks(all));
    _behaviors = new(() => IndexBehaviors(all));
  }

  /// <summary>The block type registered under <paramref name="classKey"/> by
  /// <see cref="EntityRegistry.KeyFor"/> in any of the domains, or null.</summary>
  internal Type? Block(string classKey) =>
    _blocks.Value.GetValueOrDefault(classKey);

  /// <summary>The behaviour type whose registered code equals the part of <paramref name="key"/>
  /// after its mod-id prefix, or null.</summary>
  internal Type? BlockEntityBehavior(string key) =>
    _behaviors.Value.GetValueOrDefault(key[(key.LastIndexOf('.') + 1)..]);

  private static Dictionary<string, Type> IndexBlocks(
    (string Domain, Assembly Assembly)[] sources
  ) {
    var index = new Dictionary<string, Type>(StringComparer.Ordinal);
    foreach (Type type in Types(sources, typeof(Block)))
      foreach ((string domain, _) in sources)
        index.TryAdd(EntityRegistry.KeyFor(domain, type), type);
    return index;
  }

  private static Dictionary<string, Type> IndexBehaviors(
    (string Domain, Assembly Assembly)[] sources
  ) {
    var index = new Dictionary<string, Type>(StringComparer.Ordinal);
    foreach (Type type in Types(sources, typeof(BlockEntityBehavior)))
      index.TryAdd(
        type.GetCustomAttribute<BlockEntityBehaviorRegisterAttribute>()?.Code
          ?? type.Name,
        type
      );
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
