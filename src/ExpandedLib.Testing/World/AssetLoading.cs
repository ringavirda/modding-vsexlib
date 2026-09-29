using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NSubstitute.Core;
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
  /// <summary>Loads one mod's real assets as <see cref="LoadAssets(IReadOnlyList{string}, string)"/>
  /// loads a list of one, so the mod's patches into its own files apply; of vanilla survival and
  /// creative, only the survival world properties load, which a <c>loadFromProperties</c> reads.</summary>
  /// <remarks>exlib's <c>StartPre</c> first empties every per-world registry
  /// (<see cref="Registries.ExWorldState"/>). Every system started is disposed when the load ends,
  /// thrown or not. A block's api, resolved <c>drops</c> and <c>OnLoaded</c> are the caller's.</remarks>
  /// <param name="modPath">A mod's or sample's folder: <c>modinfo.json</c> and <c>bin/</c> at its root
  /// or under <c>src/</c>, assets under <c>assets/&lt;modid&gt;/</c>; a content mod has no dll.</param>
  /// <param name="gamePath">The game install to read base assets and vanilla mods from; defaults to
  /// <see cref="VsAssemblyResolver.InstallPath"/>.</param>
  /// <returns>This world, holding each loaded block and item under an id of its own and every class
  /// the load registered in its class registry (<see cref="RegisterClasses"/>).</returns>
  /// <exception cref="InvalidOperationException">No game install resolves, no <c>modinfo.json</c>
  /// resolves, or a mod that is not a content mod has no compiled dll under <c>bin/</c>.</exception>
  public TestWorld LoadAssets(string modPath, string? gamePath = null) =>
    LoadAssets([modPath], gamePath);

  /// <summary>Loads several mods as one game does: every listed mod's assets in one asset manager,
  /// every listed mod in <see cref="Mods"/> before any system starts, then the game's own patch loader
  /// and object loader, each run once over them all.</summary>
  /// <remarks>A patch's <c>dependsOn</c> is met by exlib, the listed mods and any id <see cref="Mods"/>
  /// held before, never by <c>game</c>; its <c>condition</c> reads <see cref="Config"/>'s <c>Tree</c>;
  /// a vanilla file it names is not loaded, so it is not applied. The loader's "JsonPatch Loader: ..."
  /// line lands in <see cref="Log"/>. Applying a patch needs a newer Newtonsoft.Json than the 13.0.1 a
  /// test host brings.</remarks>
  /// <param name="modPaths">Mod folders in load order, each as <see cref="LoadAssets(string, string)"/>
  /// takes one; an empty list loads the base game alone.</param>
  /// <param name="gamePath">As <see cref="LoadAssets(string, string)"/> takes it.</param>
  /// <returns>This world, as <see cref="LoadAssets(string, string)"/> returns it.</returns>
  /// <exception cref="InvalidOperationException">As <see cref="LoadAssets(string, string)"/> throws it,
  /// for any listed folder, or VSEssentials no longer holds the patch or object loader.</exception>
  public TestWorld LoadAssets(
    IReadOnlyList<string> modPaths,
    string? gamePath = null
  ) {
    gamePath ??=
      VsAssemblyResolver.InstallPath
      ?? throw new InvalidOperationException(
        "No game install found - set the game's env var or provision .game/<slug>."
      );
    string assetsPath = Path.Combine(gamePath, "assets");
    List<ListedMod> mods = [.. modPaths.Select(ReadListedMod)];

    // The base game domain only.
    var mgr = new AssetManager(assetsPath, EnumAppSide.Server);
    mgr.InitAndLoadBaseAssets(Log);
    var survival = new PathOrigin("game", Path.Combine(assetsPath, "survival"));
    foreach (
      IAsset asset in survival.GetAssets(
        AssetCategory.worldproperties,
        shouldLoad: true
      )
    )
      mgr.Add(asset.Location, asset);
    foreach (ListedMod mod in mods)
      MirrorAssets(mgr, Path.Combine(mod.Path, "assets", mod.Id), mod.Id);

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
    foreach (ListedMod mod in mods)
      Mods.Add(mod.Id, mod.Version, true, mod.Dependencies);
    PassedOverVanillaSystems = StartVanillaMods(gamePath, rawClassRegistry);
    var started = new List<ModSystem>();
    try {
      StartExlib(loaderApi, started);
      foreach (ListedMod mod in mods)
        StartMod(loaderApi, mod, started);
      // The manager refuses a single asset until the game marks every asset loaded, which it does
      // before the AssetsLoaded stage; the patch loader reads each file it patches that way.
      ReflectionHelpers.SetField(mgr, "allAssetsLoaded", true);
      new ExDefinitionModSystem().AssetsLoaded(loaderApi);
      RunVanillaLoader(
        loaderApi,
        "Vintagestory.ServerMods.NoObf.ModJsonPatchLoader"
      );
      RunVanillaLoader(
        loaderApi,
        "Vintagestory.ServerMods.NoObf.ModRegistryObjectTypeLoader"
      );
      RegisterLoaded(loaderApi);
    } finally {
      for (int i = started.Count - 1; i >= 0; i--)
        started[i].Dispose();
      LoadedSystems = started;
    }
    return this;
  }

  /// <summary>Loads the install's vanilla survival block and item types named by
  /// <paramref name="blocks"/> and <paramref name="items"/> through the game's own object loader
  /// and registers each variant this world holds nothing of by code, as
  /// <see cref="DefineBlock"/> registers one; <c>OnLoaded</c> runs once all are
  /// registered.</summary>
  /// <param name="blocks">Blocktype file names without <c>.json</c>, matched under
  /// <c>assets/survival/blocktypes</c> at any depth, so a file that moved between game versions
  /// is still found.</param>
  /// <param name="items">Itemtype file names without <c>.json</c>, matched the same way under
  /// <c>itemtypes</c>.</param>
  /// <returns>The block and item variants registered.</returns>
  /// <exception cref="InvalidOperationException">No game install resolves.</exception>
  internal IReadOnlyList<CollectibleObject> LoadVanilla(
    IReadOnlyCollection<string> blocks,
    IReadOnlyCollection<string> items
  ) {
    string assetsPath = Path.Combine(
      VsAssemblyResolver.InstallPath
        ?? throw new InvalidOperationException(
          "No game install found - set the game's env var or provision .game/<slug>."
        ),
      "assets"
    );
    var mgr = new AssetManager(assetsPath, EnumAppSide.Server);
    mgr.InitAndLoadBaseAssets(Log);
    var survival = new PathOrigin("game", Path.Combine(assetsPath, "survival"));
    foreach (
      (AssetCategory category, IReadOnlyCollection<string>? names) in new[]
      {
        (AssetCategory.worldproperties, (IReadOnlyCollection<string>?)null),
        (AssetCategory.blocktypes, blocks),
        (AssetCategory.itemtypes, items),
      }
    )
      foreach (IAsset asset in survival.GetAssets(category, shouldLoad: true))
        if (
          names == null
          || names.Contains(Path.GetFileNameWithoutExtension(asset.Name))
        )
          mgr.Add(asset.Location, asset);
    ReflectionHelpers.SetStaticField(
      typeof(GamePaths),
      "<AssetsPath>k__BackingField",
      assetsPath
    );
    Lang.Load(Log, mgr, "en");

    ICoreServerAPI loaderApi = BuildLoaderApi(mgr, out _);
    RunVanillaLoader(
      loaderApi,
      "Vintagestory.ServerMods.NoObf.ModRegistryObjectTypeLoader"
    );
    // Vanilla collectibles read world properties from the api's assets in OnLoaded, which the
    // manager refuses until the game marks every asset loaded.
    ReflectionHelpers.SetField(mgr, "allAssetsLoaded", true);
    Api.Assets.Get(Arg.Any<AssetLocation>())
      .Returns(ci => mgr.TryGet(ci.Arg<AssetLocation>()));
    Api.Assets.TryGet(Arg.Any<AssetLocation>(), Arg.Any<bool>())
      .Returns(ci => mgr.TryGet(ci.Arg<AssetLocation>(), ci.Arg<bool>()));
    var loaded = new List<CollectibleObject>();
    foreach (ICall call in loaderApi.ReceivedCalls())
      switch (call.GetArguments().FirstOrDefault()) {
        case Block block
          when call.GetMethodInfo().Name == nameof(ICoreServerAPI.RegisterBlock)
            && GetByCode(block.Code) == null:
          block.BlockId = _nextDefinedId++;
          Register(block);
          loaded.Add(block);
          break;
        case Item item
          when call.GetMethodInfo().Name == nameof(ICoreServerAPI.RegisterItem)
            && GetItem(item.Code) == null:
          item.ItemId = _nextItemId++;
          Register(item);
          loaded.Add(item);
          break;
      }
    foreach (CollectibleObject collectible in loaded)
      if (collectible is Block block)
        Finish(block);
      else {
        ReflectionHelpers.SetField(collectible, "api", Api);
        collectible.OnLoaded(Api);
      }
    return loaded;
  }

  /// <summary>The mod systems the last <see cref="LoadAssets(IReadOnlyList{string}, string)"/>
  /// started, exlib's then each listed mod's, in start order; every one was disposed, the
  /// last-started first, before it returned.</summary>
  internal IReadOnlyList<ModSystem> LoadedSystems { get; private set; } = [];

  /// <summary>The full names of the install's vanilla mod systems whose <c>Start</c> threw against
  /// the last <see cref="LoadAssets(IReadOnlyList{string}, string)"/>'s class-registration API, in start order.</summary>
  internal IReadOnlyList<string> PassedOverVanillaSystems {
    get;
    private set;
  } = [];

  /// <summary>A listed mod as <see cref="LoadAssets(IReadOnlyList{string}, string)"/> reads its
  /// folder: <paramref name="Assembly"/> is null for a content mod.</summary>
  private sealed record ListedMod(
    string Path,
    string Id,
    string Version,
    string[] Dependencies,
    Assembly? Assembly
  );

  /// <exception cref="InvalidOperationException">No <c>modinfo.json</c> or modid resolves, or a mod
  /// that is not a content mod has no compiled dll under <c>bin/</c>.</exception>
  private static ListedMod ReadListedMod(string modPath) {
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
    bool isContentMod = string.Equals(
      (string?)modInfoJson["type"],
      "content",
      StringComparison.OrdinalIgnoreCase
    );
    return new ListedMod(
      modPath,
      modId,
      (string?)modInfoJson["version"] ?? "0.0.0",
      [
        .. (modInfoJson["dependencies"] as JObject)
          ?.Properties()
          .Select(p => p.Name)
          ?? [],
      ],
      isContentMod ? null : Assembly.LoadFrom(FindModAssembly(modRoot, modId))
    );
  }

  // Lists each system in started before its Start: the game keeps a system whose Start throws and
  // disposes it with the rest.
  private void StartMod(
    ICoreServerAPI loaderApi,
    ListedMod listed,
    List<ModSystem> started
  ) {
    Mod mod = Mods.GetMod(listed.Id)!;
    foreach (
      Type t in (listed.Assembly?.GetTypes() ?? []).Where(t =>
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
  }

  /// <summary>Gives every block and item registered through <paramref name="loaderApi"/> an id of
  /// its own and registers it in this world.</summary>
  private void RegisterLoaded(ICoreServerAPI loaderApi) {
    foreach (
      Block block in loaderApi
        .ReceivedCalls()
        .Where(c =>
          c.GetMethodInfo().Name == nameof(ICoreServerAPI.RegisterBlock)
        )
        .Select(c => (Block)c.GetArguments()[0]!)
    ) {
      block.BlockId = _nextDefinedId++;
      Register(block);
    }
    foreach (
      Item item in loaderApi
        .ReceivedCalls()
        .Where(c =>
          c.GetMethodInfo().Name == nameof(ICoreServerAPI.RegisterItem)
        )
        .Select(c => (Item)c.GetArguments()[0]!)
    ) {
      item.ItemId = _nextItemId++;
      Register(item);
    }
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

    rawClassRegistry = Classes();
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
  internal static List<string> StartVanillaMods(
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

  /// <summary>Reflectively runs the <c>AssetsLoaded</c> of the VSEssentials mod system named
  /// <paramref name="typeName"/>, found by name each call to avoid a version-fragile static
  /// reference.</summary>
  /// <exception cref="InvalidOperationException">VSEssentials holds no such type, or it cannot be
  /// constructed.</exception>
  private static void RunVanillaLoader(ICoreServerAPI api, string typeName) {
    Type loaderType =
      Assembly.Load("VSEssentials").GetType(typeName)
      ?? throw new InvalidOperationException(
        $"VSEssentials no longer exposes {typeName}."
      );
    object loader =
      Activator.CreateInstance(loaderType, nonPublic: true)
      ?? throw new InvalidOperationException(
        $"Could not construct {loaderType.FullName}."
      );
    try {
      loaderType
        .GetMethod("AssetsLoaded", [typeof(ICoreAPI)])!
        .Invoke(loader, [api]);
    } catch (TargetInvocationException e) when (e.InnerException != null) {
      throw e.InnerException;
    }
  }
}
