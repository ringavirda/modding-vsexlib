using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace ExpandedLib.Registries;

/// <summary>Reflection-driven class registration for mods built on ExpandedLib.</summary>
public static class EntityRegistry {
  /// <summary>Log sink for the cross-mod domain-fallback warning. Null before startup.</summary>
  internal static ILogger? Logger { get; set; }

  /// <summary>
  /// Registers every <see cref="RegisterAttribute"/>-decorated class in <paramref name="asm"/>
  /// (default: the calling mod's own assembly).
  /// </summary>
  public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null) {
    asm ??= Assembly.GetCallingAssembly();
    string modId = mod.Info.ModID;
    // A module's own [assembly: ExDomain] outranks its host's mod id.
    string domain =
      asm.GetCustomAttribute<ExDomainAttribute>()?.Domain ?? modId;

    // Fallback for an assembly that declares no [assembly: ExDomain].
    _domainByAssembly[asm] = domain;

    foreach (Type type in ReflectionScan.GetCandidateTypes(asm)) {
      var attr = type.GetCustomAttributes()
        .OfType<RegisterAttribute>()
        .FirstOrDefault();
      if (attr == null)
        continue;

      string key = KeyFor(domain, type, attr);

      switch (attr) {
        case BlockRegisterAttribute
          when Validate<Block>(api, modId, type, "block"):
          api.RegisterBlockClass(key, type);
          break;
        case ItemRegisterAttribute
          when Validate<Item>(api, modId, type, "item"):
          api.RegisterItemClass(key, type);
          break;
        case BlockEntityRegisterAttribute
          when Validate<BlockEntity>(api, modId, type, "block entity"):
          RegisterBlockEntity(api, domain, key, attr, type);
          break;
        case BlockBehaviorRegisterAttribute
          when Validate<BlockBehavior>(api, modId, type, "block behavior"):
          api.RegisterBlockBehaviorClass(key, type);
          break;
        case BlockEntityBehaviorRegisterAttribute
          when Validate<BlockEntityBehavior>(
            api,
            modId,
            type,
            "block entity behavior"
          ):
          api.RegisterBlockEntityBehaviorClass(key, type);
          break;
        case CollectibleBehaviorRegisterAttribute
          when Validate<CollectibleBehavior>(
            api,
            modId,
            type,
            "collectible behavior"
          ):
          api.RegisterCollectibleBehaviorClass(key, type);
          break;
        case EntityRegisterAttribute
          when Validate<Entity>(api, modId, type, "entity"):
          api.RegisterEntity(key, type);
          break;
        case EntityBehaviorRegisterAttribute
          when Validate<EntityBehavior>(api, modId, type, "entity behavior"):
          api.RegisterEntityBehaviorClass(key, type);
          break;
        case CropBehaviorRegisterAttribute
          when Validate<CropBehavior>(api, modId, type, "crop behavior"):
          api.RegisterCropBehavior(key, type);
          break;
      }
    }

    ExDefinitions.DiscoverAndRegister(domain, asm);
    ExDefinitions.DiscoverAndRegisterItems(domain, asm);
    ExDefinitions.DiscoverAndRegisterRecipes(domain, asm);

    // Discovered here but run later, at AssetsLoaded.
    ExDefinitions.DiscoverContributors(asm);
  }

  // Assembly -> the domain its registrable types are keyed under.
  private static readonly Dictionary<Assembly, string> _domainByAssembly = [];

  /// <summary>
  /// The domain <paramref name="asm"/>'s registrable types are keyed under: its
  /// <see cref="ExDomainAttribute"/> if it declares one, else the modid it was registered with, else
  /// <paramref name="fallback"/>.
  /// </summary>
  public static string DomainOf(Assembly asm, string fallback) {
    string? declared = asm.GetCustomAttribute<ExDomainAttribute>()?.Domain;
    if (declared != null)
      return declared;
    if (_domainByAssembly.TryGetValue(asm, out string? recorded))
      return recorded;

    Logger?.Warning(
      "[exlib] {0} declares no [assembly: ExDomain] and was never registered; Class<T>()/Behavior<T>() "
        + "resolve its types under '{1}' instead, which is wrong unless that is really this assembly's own domain.",
      asm.GetName().Name,
      fallback
    );
    return fallback;
  }

  /// <summary>
  /// The registry key a <see cref="RegisterAttribute"/>-decorated <paramref name="type"/> is
  /// registered under: <c>{domain}.{Code ?? ClassName}</c>, or the bare key when
  /// <see cref="RegisterAttribute.PrefixModId"/> is false.
  /// </summary>
  public static string KeyFor(string callerDomain, Type type) =>
    KeyFor(
      DomainOf(type.Assembly, callerDomain),
      type,
      type.GetCustomAttributes().OfType<RegisterAttribute>().FirstOrDefault()
    );

  private static string KeyFor(string modId, Type type, RegisterAttribute? attr) {
    string baseKey = attr?.Code ?? type.Name;
    return (attr?.PrefixModId ?? true) ? $"{modId}.{baseKey}" : baseKey;
  }

  /// <summary>Logs a warning and returns false when <paramref name="type"/> does not derive from the
  /// base type its register attribute implies.</summary>
  private static bool Validate<TBase>(
    ICoreAPI api,
    string modId,
    Type type,
    string kind
  ) {
    if (typeof(TBase).IsAssignableFrom(type))
      return true;

    api.Logger.Warning(
      "[{0}] EntityRegistry: {1} is marked as a {2} but does not derive from {3}; skipped.",
      modId,
      type.FullName,
      kind,
      typeof(TBase).Name
    );
    return false;
  }

  /// <summary>Registers a block entity under its primary key plus the <c>{modid}.{ShortId}</c>,
  /// <c>{ShortId}</c> and <c>{shortid}</c> aliases of a <c>BlockEntityXxx</c> name; an explicit
  /// <see cref="RegisterAttribute.Code"/> gets no aliases.</summary>
  private static void RegisterBlockEntity(
    ICoreAPI api,
    string domain,
    string key,
    RegisterAttribute attr,
    Type type
  ) {
    api.RegisterBlockEntityClass(key, type);

    const string prefix = "BlockEntity";
    if (attr.Code != null || !type.Name.StartsWith(prefix))
      return;

    string shortId = type.Name[prefix.Length..];
    api.RegisterBlockEntityClass($"{domain}.{shortId}", type);
    RegisterBareAlias(api, shortId, type);
    RegisterBareAlias(api, shortId.ToLowerInvariant(), type);
  }

  // Bare alias key -> the type that first claimed it.
  private static readonly Dictionary<string, Type> _bareKeysIssued = [];

  private static void RegisterBareAlias(ICoreAPI api, string key, Type type) {
    if (!_bareKeysIssued.TryAdd(key, type)) {
      Type owner = _bareKeysIssued[key];
      if (owner != type)
        api.Logger.Error(
          "[exlib] Bare block entity key '{0}' is already registered by {1}; {2} claims it too - "
            + "a saved block entity keyed '{0}' will load whichever type registered last.",
          key,
          owner.FullName,
          type.FullName
        );
    }

    api.RegisterBlockEntityClass(key, type);
  }
}
