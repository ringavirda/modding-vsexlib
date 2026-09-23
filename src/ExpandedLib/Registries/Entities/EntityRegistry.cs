using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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

  /// <summary>Registers a block entity under the <c>{modid}.{ShortId}</c>, <c>{ShortId}</c> and
  /// <c>{shortid}</c> aliases of a <c>BlockEntityXxx</c> name, then under its primary key, which the
  /// game saves it by; an explicit <see cref="RegisterAttribute.Code"/> gets no aliases.</summary>
  private static void RegisterBlockEntity(
    ICoreAPI api,
    string domain,
    string key,
    RegisterAttribute attr,
    Type type
  ) {
    const string prefix = "BlockEntity";
    if (attr.Code == null && type.Name.StartsWith(prefix)) {
      string shortId = type.Name[prefix.Length..];
      api.RegisterBlockEntityClass($"{domain}.{shortId}", type);
      RegisterBareAliases(api, type, shortId, shortId.ToLowerInvariant());
    }

    // The game saves a block entity under the last key its type was registered with.
    api.RegisterBlockEntityClass(key, type);
    _primaryKeys[type] = key;
  }

  /// <summary>
  /// Registers <paramref name="type"/> under <paramref name="key"/> so a save naming that key loads
  /// it, while the game keeps saving it under its primary key.
  /// </summary>
  /// <remarks>A type <see cref="RegisterAll"/> has not registered yet gets its primary key
  /// registered after the alias when it is. A type <see cref="RegisterAll"/> never registers is
  /// saved under <paramref name="key"/>, the last key registered for it.</remarks>
  /// <param name="api">The api whose class registry receives the alias.</param>
  /// <param name="key">The load-only key, for example the class name an older release saved.</param>
  /// <param name="type">A <see cref="BlockEntity"/> type.</param>
  /// <exception cref="ArgumentException"><paramref name="type"/> does not derive from
  /// <see cref="BlockEntity"/>.</exception>
  public static void AliasBlockEntity(ICoreAPI api, string key, Type type) {
    if (!typeof(BlockEntity).IsAssignableFrom(type))
      throw new ArgumentException(
        $"{type.FullName} is not a block entity class.",
        nameof(type)
      );

    api.RegisterBlockEntityClass(key, type);
    if (_primaryKeys.TryGetValue(type, out string? primary))
      api.RegisterBlockEntityClass(primary, type);
  }

  // Block entity type -> the primary key RegisterAll registered it under.
  private static readonly Dictionary<Type, string> _primaryKeys = [];

  // Class registry -> bare alias key -> the type that owns it in that registry.
  private static readonly ConditionalWeakTable<
    IClassRegistryAPI,
    Dictionary<string, Type>
  > _bareKeysIssued = new();

  private static bool PublishesSaveKeys(Type type) =>
    type.Assembly.IsDefined(typeof(ExPublishedSaveKeysAttribute));

  private static void RegisterBareAliases(
    ICoreAPI api,
    Type type,
    params string[] keys
  ) {
    bool published = PublishesSaveKeys(type);
    Dictionary<string, Type> issued = _bareKeysIssued.GetOrCreateValue(
      api.ClassRegistry
    );
    // Contested keys settled by publication, grouped by the other claimant.
    Dictionary<Type, List<string>> settled = [];

    foreach (string key in keys.Distinct()) {
      if (!issued.TryGetValue(key, out Type? owner) || owner == type) {
        issued[key] = type;
        api.RegisterBlockEntityClass(key, type);
        continue;
      }

      if (published == PublishesSaveKeys(owner)) {
        api.Logger.Error(
          "[exlib] Bare block entity key '{0}' is already registered by {1}; {2} claims it too - "
            + "a saved block entity keyed '{0}' will load whichever type registered last.",
          key,
          owner.FullName,
          type.FullName
        );
        api.RegisterBlockEntityClass(key, type);
        continue;
      }

      if (!settled.TryGetValue(owner, out List<string>? contested))
        settled[owner] = contested = [];
      contested.Add(key);
      if (published) {
        issued[key] = type;
        api.RegisterBlockEntityClass(key, type);
      }
    }

    foreach ((Type other, List<string> contested) in settled) {
      (Type keeper, Type skipped) = published ? (type, other) : (other, type);
      api.Logger.Notification(
        "[exlib] Bare block entity key(s) {0} belong to {1}, whose assembly declares "
          + "[assembly: ExPublishedSaveKeys]; {2} is not registered under them.",
        string.Join(", ", contested.Select(k => $"'{k}'")),
        keeper.FullName,
        skipped.FullName
      );
    }
  }
}
