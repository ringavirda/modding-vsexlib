using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Common;
#if GAME_GE_1_22
using Vintagestory.API.Datastructures;
using Vintagestory.Common.Datastructures;
#endif

namespace ExpandedLib.Testing;

/// <summary>
/// Real-asset loading for <see cref="TestWorld"/>: drives the game's own <c>AssetManager</c> and
/// <c>ModRegistryObjectTypeLoader</c> against a mod's actual JSON/code, producing real, resolved
/// <see cref="Block"/>/<see cref="Item"/> instances.
/// </summary>
public sealed partial class TestWorld {
  /// <summary>Loads one mod's real assets through the game's own asset manager and object loader and
  /// registers the resulting <see cref="Block"/>/<see cref="Item"/> instances; vanilla survival and
  /// creative content is not loaded.</summary>
  /// <remarks>The install's vanilla mod systems register their classes first, so a vanilla-only
  /// load logs nothing. exlib's driver then runs its <c>StartPre</c>, emptying every per-world
  /// registry (<see cref="Registries.ExWorldState"/>), and every exlib mod system its <c>Start</c>,
  /// before the mod's. Every system started, a mod's own included, is disposed when the load ends,
  /// thrown or not, the mod's first, so the Harmony holds they took are released.</remarks>
  /// <param name="modPath">A mod's or sample's folder; <c>modinfo.json</c>/<c>bin/</c> may sit at its
  /// root or under <c>src/</c>, assets always under <c>assets/&lt;modid&gt;/</c>. A mod whose
  /// <c>modinfo.json</c> declares <c>"type": "content"</c> has no compiled assembly.</param>
  /// <param name="gamePath">The game install to read base assets and vanilla mods from; defaults to
  /// <see cref="VsAssemblyResolver.InstallPath"/>.</param>
  /// <returns>This world.</returns>
  /// <exception cref="InvalidOperationException">No game install resolves, no <c>modinfo.json</c>
  /// resolves, or a mod that is not a content mod has no compiled dll under <c>bin/</c>.</exception>
  public TestWorld LoadAssets(string modPath, string? gamePath = null) {
    gamePath ??=
      VsAssemblyResolver.InstallPath
      ?? throw new InvalidOperationException(
        "No game install found - set the game's env var or provision .game/<slug>."
      );
    string assetsPath = Path.Combine(gamePath, "assets");

    // modinfo.json presence picks src/ vs. the mod's own root; checked directly since a stale
    // ignored bin/ can outlive a layout move.
    string modRoot = File.Exists(Path.Combine(modPath, "modinfo.json"))
      ? modPath
      : Path.Combine(modPath, "src");

    string modInfoPath = Path.Combine(modRoot, "modinfo.json");
    if (!File.Exists(modInfoPath))
      throw new InvalidOperationException(
        $"No modinfo.json under '{modPath}' or '{modRoot}'."
      );
    var modInfoJson = JObject.Parse(File.ReadAllText(modInfoPath));
    string modId =
      (string?)modInfoJson["modid"]
      ?? throw new InvalidOperationException($"'{modInfoPath}' has no modid.");
    string version = (string?)modInfoJson["version"] ?? "0.0.0";
    bool isContentMod = string.Equals(
      (string?)modInfoJson["type"],
      "content",
      StringComparison.OrdinalIgnoreCase
    );

    Assembly? modAssembly = isContentMod
      ? null
      : Assembly.LoadFrom(FindModAssembly(modRoot, modId));

    // The base game domain only.
    var mgr = new AssetManager(assetsPath, EnumAppSide.Server);
    mgr.InitAndLoadBaseAssets(Log);
    MirrorAssets(mgr, Path.Combine(modPath, "assets", modId), modId);

    // GamePaths.AssetsPath/Lang.Load are process-wide statics the object loader needs primed; safe
    // to set repeatedly.
    ReflectionHelpers.SetStaticField(
      typeof(GamePaths),
      "<AssetsPath>k__BackingField",
      assetsPath
    );
    Lang.Load(Log, mgr, "en");

    ICoreServerAPI loaderApi = BuildLoaderApi(
      mgr,
      out ClassRegistry rawClassRegistry
    );
    PassedOverVanillaSystems = StartVanillaMods(gamePath, rawClassRegistry);
    var started = new List<ModSystem>();
    try {
      StartExlib(loaderApi, started);
      LoadMod(loaderApi, modAssembly, modId, version, started);
    } finally {
      for (int i = started.Count - 1; i >= 0; i--)
        started[i].Dispose();
      LoadedSystems = started;
    }
    return this;
  }

  /// <summary>The mod systems the last <see cref="LoadAssets"/> started, exlib's then the mod's, in
  /// start order; every one was disposed, the mod's first, before it returned.</summary>
  internal IReadOnlyList<ModSystem> LoadedSystems { get; private set; } = [];

  /// <summary>The full names of the install's vanilla mod systems whose <c>Start</c> threw against
  /// the last <see cref="LoadAssets"/>'s class-registration API, in start order.</summary>
  internal IReadOnlyList<string> PassedOverVanillaSystems {
    get;
    private set;
  } = [];

  // Lists each system in started before its Start: the game keeps a system whose Start throws and
  // disposes it with the rest.
  private void LoadMod(
    ICoreServerAPI loaderApi,
    Assembly? modAssembly,
    string modId,
    string version,
    List<ModSystem> started
  ) {
    Mods.Add(modId, version);
    Mod mod = Mods.GetMod(modId)!;

    foreach (
      Type t in (modAssembly?.GetTypes() ?? []).Where(t =>
        typeof(ModSystem).IsAssignableFrom(t) && !t.IsAbstract
      )
    ) {
      var sys = (ModSystem)Activator.CreateInstance(t)!;
      ReflectionHelpers.SetField(sys, "<Mod>k__BackingField", mod);
      if (sys.ShouldLoad(EnumAppSide.Server)) {
        started.Add(sys);
        sys.Start(loaderApi);
      }
    }
    // Runs regardless of whether the mod is code-first; a plain-JSON mod is a no-op here.
    new ExDefinitionModSystem().AssetsLoaded(loaderApi);

    RunObjectLoader(loaderApi);

    foreach (
      Block block in loaderApi
        .ReceivedCalls()
        .Where(c =>
          c.GetMethodInfo().Name == nameof(ICoreServerAPI.RegisterBlock)
        )
        .Select(c => (Block)c.GetArguments()[0]!)
    )
      Register(block);
    foreach (
      Item item in loaderApi
        .ReceivedCalls()
        .Where(c =>
          c.GetMethodInfo().Name == nameof(ICoreServerAPI.RegisterItem)
        )
        .Select(c => (Item)c.GetArguments()[0]!)
    )
      Register(item);
  }

  /// <summary>Copies every asset under <paramref name="fullPath"/> for <paramref name="domain"/> into
  /// <paramref name="mgr"/>'s live asset dictionary; <c>AssetManager.AddPathOrigin</c> alone does not
  /// reach it.</summary>
  private static void MirrorAssets(
    AssetManager mgr,
    string fullPath,
    string domain
  ) {
    if (!Directory.Exists(fullPath))
      return;
    var origin = new PathOrigin(domain, fullPath);
    foreach (AssetCategory cat in KnownCategories)
      foreach (IAsset a in origin.GetAssets(cat, shouldLoad: true))
        mgr.Add(a.Location, a);
  }

  private static readonly AssetCategory[] KnownCategories =
  [
    AssetCategory.blocktypes,
    AssetCategory.itemtypes,
    AssetCategory.entities,
    AssetCategory.recipes,
    AssetCategory.worldproperties,
    AssetCategory.patches,
    AssetCategory.lang,
    AssetCategory.config,
  ];

  /// <summary>Finds the mod's compiled assembly under <c>modPath/bin/</c>: the first dll (recursive
  /// search) whose file name matches <paramref name="modId"/> case-insensitively.</summary>
  private static string FindModAssembly(string modPath, string modId) {
    string binPath = Path.Combine(modPath, "bin");
    if (!Directory.Exists(binPath))
      throw new InvalidOperationException(
        $"No compiled assembly under '{binPath}' - build '{modPath}' first."
      );
    return Directory
        .EnumerateFiles(binPath, "*.dll", SearchOption.AllDirectories)
        .FirstOrDefault(p =>
          string.Equals(
            Path.GetFileNameWithoutExtension(p),
            modId,
            StringComparison.OrdinalIgnoreCase
          )
        )
      ?? throw new InvalidOperationException(
        $"No '{modId}.dll' under '{binPath}' - build '{modPath}' first."
      );
  }

  /// <summary>
  /// Builds the isolated <c>ICoreServerAPI</c> substitute the object loader runs against, wiring a
  /// real <c>ClassRegistry</c>, real tag registries and a real logger.
  /// </summary>
  private ICoreServerAPI BuildLoaderApi(
    AssetManager mgr,
    out ClassRegistry rawClassRegistry
  ) {
    var api = Substitute.For<ICoreServerAPI>();
    var coreApi = (ICoreAPI)api;
    coreApi.Assets.Returns(mgr);
    coreApi.Logger.Returns(Log);
    coreApi.Side.Returns(EnumAppSide.Server);
    coreApi.World.Returns(World);
    coreApi.ModLoader.Returns(Mods);

    rawClassRegistry = new ClassRegistry();
    coreApi.ClassRegistry.Returns(
      new ClassRegistryAPI(World, rawClassRegistry)
    );
    ForwardClassRegistrations(coreApi, rawClassRegistry);

    // CollectibleTagRegistry/EntityTagRegistry appear only from 1.22 onward.
#if GAME_GE_1_22
    var collectibleTags = new ConcurrentTagRegistry(Log, "collectible");
    var entityTags = new ConcurrentTagRegistryFast(Log, "entity");
    coreApi.CollectibleTagRegistry.Returns(collectibleTags);
    coreApi.EntityTagRegistry.Returns(entityTags);
    GenericComplexConditionConverter.StaticInit(collectibleTags, entityTags);
    CollectibleTagSetConverter.StaticInit(collectibleTags);
    EntityTagSetConverter.StaticInit(entityTags);
#endif

    var serverApi = Substitute.For<IServerAPI>();
    serverApi.Logger.Returns(Log);
    api.Server.Returns(serverApi);

    return api;
  }

  /// <summary>Forwards every class registration made through <paramref name="api"/> into
  /// <paramref name="registry"/>, as the game's own API does.</summary>
  private static void ForwardClassRegistrations(
    ICoreAPI api,
    ClassRegistry registry
  ) {
    api.When(x => x.RegisterBlockClass(Arg.Any<string>(), Arg.Any<Type>()))
      .Do(ci =>
        registry.RegisterBlockClass(ci.ArgAt<string>(0), ci.ArgAt<Type>(1))
      );
    api.When(x =>
        x.RegisterBlockEntityClass(Arg.Any<string>(), Arg.Any<Type>())
      )
      .Do(ci =>
        registry.RegisterBlockEntityType(ci.ArgAt<string>(0), ci.ArgAt<Type>(1))
      );
    api.When(x => x.RegisterItemClass(Arg.Any<string>(), Arg.Any<Type>()))
      .Do(ci =>
        registry.RegisterItemClass(ci.ArgAt<string>(0), ci.ArgAt<Type>(1))
      );
    api.When(x =>
        x.RegisterBlockBehaviorClass(Arg.Any<string>(), Arg.Any<Type>())
      )
      .Do(ci =>
        registry.RegisterBlockBehaviorClass(
          ci.ArgAt<string>(0),
          ci.ArgAt<Type>(1)
        )
      );
    api.When(x =>
        x.RegisterBlockEntityBehaviorClass(Arg.Any<string>(), Arg.Any<Type>())
      )
      .Do(ci =>
        registry.RegisterBlockEntityBehaviorClass(
          ci.ArgAt<string>(0),
          ci.ArgAt<Type>(1)
        )
      );
    api.When(x =>
        x.RegisterCollectibleBehaviorClass(Arg.Any<string>(), Arg.Any<Type>())
      )
      .Do(ci =>
        registry.RegisterCollectibleBehaviorClass(
          ci.ArgAt<string>(0),
          ci.ArgAt<Type>(1)
        )
      );
    api.When(x => x.RegisterCropBehavior(Arg.Any<string>(), Arg.Any<Type>()))
      .Do(ci =>
        registry.RegisterCropBehavior(ci.ArgAt<string>(0), ci.ArgAt<Type>(1))
      );
    api.When(x => x.RegisterEntity(Arg.Any<string>(), Arg.Any<Type>()))
      .Do(ci =>
        registry.RegisterEntityType(ci.ArgAt<string>(0), ci.ArgAt<Type>(1))
      );
    api.When(x =>
        x.RegisterEntityBehaviorClass(Arg.Any<string>(), Arg.Any<Type>())
      )
      .Do(ci =>
        registry.RegisterentityBehavior(ci.ArgAt<string>(0), ci.ArgAt<Type>(1))
      );
    api.When(x =>
        x.RegisterMountable(Arg.Any<string>(), Arg.Any<GetMountableDelegate>())
      )
      .Do(ci =>
        registry.RegisterMountable(
          ci.ArgAt<string>(0),
          ci.ArgAt<GetMountableDelegate>(1)
        )
      );
  }

  /// <summary>Starts every server-side mod system of the dlls in <paramref name="gamePath"/>'s
  /// <c>Mods/</c> folder, in the game's execute order, against an API whose only effect is class
  /// registration into <paramref name="registry"/>. A system whose <c>Start</c> throws against that
  /// API is passed over; what it registered before throwing stays.</summary>
  /// <returns>The full names of the systems passed over; empty when the install has no
  /// <c>Mods/</c> folder.</returns>
  private static List<string> StartVanillaMods(
    string gamePath,
    ClassRegistry registry
  ) {
    var passedOver = new List<string>();
    string modsPath = Path.Combine(gamePath, "Mods");
    if (!Directory.Exists(modsPath))
      return passedOver;
    var api = Substitute.For<ICoreServerAPI>();
    ((ICoreAPI)api).Side.Returns(EnumAppSide.Server);
    ForwardClassRegistrations(api, registry);

    var systems = new List<ModSystem>();
    foreach (
      string dll in Directory.EnumerateFiles(modsPath, "*.dll").OrderBy(p => p)
    )
      foreach (
        Type t in Assembly
          .Load(Path.GetFileNameWithoutExtension(dll))
          .GetTypes()
          .Where(t =>
            typeof(ModSystem).IsAssignableFrom(t)
            && !t.IsAbstract
            && t.GetConstructor(Type.EmptyTypes) != null
          )
      )
        systems.Add((ModSystem)Activator.CreateInstance(t)!);

    foreach (
      ModSystem system in systems
        .Where(s => s.ShouldLoad(EnumAppSide.Server))
        .OrderBy(s => s.ExecuteOrder())
    ) {
      try {
        system.Start(api);
      } catch (Exception) {
        passedOver.Add(system.GetType().FullName!);
      }
    }
    return passedOver;
  }

  /// <summary>Runs <see cref="Registries.ExModuleModSystem.StartPre"/>, then the <c>Start</c> of
  /// every server-side mod system in exlib's assembly in the game's execute order, each under this
  /// world's <c>exlib</c> mod against <paramref name="api"/>. Lists each system in
  /// <paramref name="started"/> before it starts, for the caller to dispose.</summary>
  private void StartExlib(ICoreServerAPI api, List<ModSystem> started) {
    Mod exlib = Mods.GetMod("exlib")!;
    List<ModSystem> systems =
    [
      .. typeof(Registries.ExModuleModSystem)
        .Assembly.GetTypes()
        .Where(t =>
          typeof(ModSystem).IsAssignableFrom(t)
          && !t.IsAbstract
          && t.GetConstructor(Type.EmptyTypes) != null
        )
        .Select(t => (ModSystem)Activator.CreateInstance(t)!)
        .Where(s => s.ShouldLoad(EnumAppSide.Server))
        .OrderBy(s => s.ExecuteOrder()),
    ];
    foreach (ModSystem system in systems)
      ReflectionHelpers.SetField(system, "<Mod>k__BackingField", exlib);

    // Only the driver's StartPre: ExpandedLibModSystem's asks this world's TestModLoader, which
    // answers every mod id enabled, so it would name smex and ppex as outdated.
    systems.OfType<Registries.ExModuleModSystem>().Single().StartPre(api);
    foreach (ModSystem system in systems) {
      started.Add(system);
      system.Start(api);
    }
  }

  /// <summary>Reflectively runs <c>ModRegistryObjectTypeLoader.AssetsLoaded</c>, found by name each
  /// call to avoid a version-fragile static reference.</summary>
  private static void RunObjectLoader(ICoreServerAPI api) {
    Type loaderType =
      Assembly
        .Load("VSEssentials")
        .GetType("Vintagestory.ServerMods.NoObf.ModRegistryObjectTypeLoader")
      ?? throw new InvalidOperationException(
        "VSEssentials no longer exposes Vintagestory.ServerMods.NoObf.ModRegistryObjectTypeLoader."
      );
    object loader =
      Activator.CreateInstance(loaderType, nonPublic: true)
      ?? throw new InvalidOperationException(
        $"Could not construct {loaderType.FullName}."
      );
    try {
      loaderType.GetMethod("AssetsLoaded")!.Invoke(loader, [api]);
    } catch (TargetInvocationException e) when (e.InnerException != null) {
      throw e.InnerException;
    }
  }
}
