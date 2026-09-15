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
using Vintagestory.Common.Datastructures;
#endif

namespace ExpandedLib.Testing;

/// <summary>
/// Real-asset loading for <see cref="TestWorld"/>: drives the game's own <c>AssetManager</c> and
/// <c>ModRegistryObjectTypeLoader</c> against a mod's actual JSON/code, producing real, resolved
/// <see cref="Block"/>/<see cref="Item"/> instances.
/// </summary>
public sealed partial class TestWorld {
  /// <summary>
  /// Loads one mod's real assets through the game's own asset manager and object loader, registering
  /// resulting <see cref="Block"/>/<see cref="Item"/> instances, excluding vanilla survival/creative
  /// content.
  /// </summary>
  /// <param name="modPath">A mod's or sample's folder; <c>modinfo.json</c>/<c>bin/</c> may sit at its
  /// root or under <c>src/</c>, assets always under <c>assets/&lt;modid&gt;/</c>.</param>
  /// <param name="gamePath">The game install to read base assets from; defaults to
  /// <see cref="VsAssemblyResolver.InstallPath"/>.</param>
  /// <exception cref="InvalidOperationException">No game install resolves, no <c>modinfo.json</c>
  /// resolves, or the compiled dll cannot be found under <c>bin/</c>.</exception>
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

    Assembly modAssembly = Assembly.LoadFrom(FindModAssembly(modRoot, modId));

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

    Mods.Add(modId, version);
    Mod mod = Mods.GetMod(modId)!;

    foreach (
      Type t in modAssembly
        .GetTypes()
        .Where(t => typeof(ModSystem).IsAssignableFrom(t) && !t.IsAbstract)
    ) {
      var sys = (ModSystem)Activator.CreateInstance(t)!;
      ReflectionHelpers.SetField(sys, "<Mod>k__BackingField", mod);
      if (sys.ShouldLoad(EnumAppSide.Server))
        sys.Start(loaderApi);
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

    return this;
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
    ClassRegistry captured = rawClassRegistry;
    coreApi.ClassRegistry.Returns(new ClassRegistryAPI(World, captured));
    coreApi
      .When(x => x.RegisterBlockClass(Arg.Any<string>(), Arg.Any<Type>()))
      .Do(ci =>
        captured.RegisterBlockClass(ci.ArgAt<string>(0), ci.ArgAt<Type>(1))
      );
    coreApi
      .When(x => x.RegisterBlockEntityClass(Arg.Any<string>(), Arg.Any<Type>()))
      .Do(ci =>
        captured.RegisterBlockEntityType(ci.ArgAt<string>(0), ci.ArgAt<Type>(1))
      );
    coreApi
      .When(x => x.RegisterItemClass(Arg.Any<string>(), Arg.Any<Type>()))
      .Do(ci =>
        captured.RegisterItemClass(ci.ArgAt<string>(0), ci.ArgAt<Type>(1))
      );
    coreApi
      .When(x =>
        x.RegisterBlockBehaviorClass(Arg.Any<string>(), Arg.Any<Type>())
      )
      .Do(ci =>
        captured.RegisterBlockBehaviorClass(
          ci.ArgAt<string>(0),
          ci.ArgAt<Type>(1)
        )
      );
    coreApi
      .When(x =>
        x.RegisterBlockEntityBehaviorClass(Arg.Any<string>(), Arg.Any<Type>())
      )
      .Do(ci =>
        captured.RegisterBlockEntityBehaviorClass(
          ci.ArgAt<string>(0),
          ci.ArgAt<Type>(1)
        )
      );

    // CollectibleTagRegistry/EntityTagRegistry appear only from 1.22 onward.
#if GAME_GE_1_22
    coreApi.CollectibleTagRegistry.Returns(
      new ConcurrentTagRegistry(Log, "collectible")
    );
    coreApi.EntityTagRegistry.Returns(
      new ConcurrentTagRegistryFast(Log, "entity")
    );
#endif

    var serverApi = Substitute.For<IServerAPI>();
    serverApi.Logger.Returns(Log);
    api.Server.Returns(serverApi);

    return api;
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
