using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NSubstitute.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
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
  /// creative, only the survival world properties load, which a <c>loadFromProperties</c> reads,
  /// and the survival files the mod's patches name.</summary>
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
  /// <exception cref="JsonReaderException">The mod's patch file is not JSON.</exception>
  public TestWorld LoadAssets(string modPath, string? gamePath = null) =>
    LoadAssets([modPath], gamePath);

  /// <summary>Loads several mods as one game does: every listed mod's assets in one asset manager,
  /// every listed mod in <see cref="Mods"/> before any system starts, then the game's own patch loader
  /// and object loader, each run once over them all.</summary>
  /// <remarks>A patch's <c>dependsOn</c> is met by exlib, the listed mods and any id <see cref="Mods"/>
  /// held before, never by <c>game</c>; its <c>condition</c> reads <see cref="Config"/>'s <c>Tree</c>.
  /// Each <c>game:</c> file a patch names without a wildcard is read from <c>assets/survival</c>
  /// whatever its conditions; a file the install lacks is logged as not found. A listed mod's
  /// <c>modinfo.json</c> dependencies besides <c>game</c>, <c>survival</c> and <c>creative</c> load
  /// before it, once, from <see cref="RepoPaths.Assets"/>'s folder; an unreadable one is skipped.</remarks>
  /// <param name="modPaths">Mod folders in load order, each as <see cref="LoadAssets(string, string)"/>
  /// takes one; an empty list loads the base game alone.</param>
  /// <param name="gamePath">As <see cref="LoadAssets(string, string)"/> takes it.</param>
  /// <returns>This world, as <see cref="LoadAssets(string, string)"/> returns it.</returns>
  /// <exception cref="InvalidOperationException">As <see cref="LoadAssets(string, string)"/> throws it,
  /// for any listed folder, or VSEssentials no longer holds the patch or object loader.</exception>
  /// <exception cref="JsonReaderException">A listed mod's patch file is not JSON.</exception>
  public TestWorld LoadAssets(
    IReadOnlyList<string> modPaths,
    string? gamePath = null
  ) => LoadAssets(modPaths, gamePath, []);

  /// <summary>Loads the game as a running server holds it: every survival blocktype and itemtype
  /// (a file that does not parse or has no <c>code</c> is left out), the listed mods' types, the shapes and
  /// textures of the listed mods and of survival in the asset manager <c>Api.Assets</c> answers
  /// from, and each started mod system's <c>AssetsFinalize</c> in the game's execute order.</summary>
  /// <remarks>Loads as <see cref="LoadAssets(IReadOnlyList{string}, string)"/> does, which also empties every
  /// per-world registry (<see cref="Registries.ExWorldState"/>) for the whole test process; a test
  /// assembly that seeds one re-seeds it after the call. The load mirrors about 200 MB of art. A
  /// system's <c>AssetsFinalize</c> that throws ends the load with its exception.</remarks>
  /// <param name="modPaths">Mod folders in load order, as <see cref="LoadAssets(string, string)"/>
  /// takes one; an empty list loads survival alone.</param>
  /// <param name="gamePath">As <see cref="LoadAssets(string, string)"/> takes it.</param>
  /// <returns>This world, as <see cref="LoadAssets(IReadOnlyList{string}, string)"/> returns it, with
  /// <c>Api.Assets</c> answering shapes and textures.</returns>
  /// <exception cref="InvalidOperationException">As <see cref="LoadAssets(IReadOnlyList{string}, string)"/>
  /// throws it.</exception>
  /// <exception cref="JsonReaderException">A listed mod's patch file is not JSON.</exception>
  public TestWorld LoadAsGame(
    IReadOnlyList<string> modPaths,
    string? gamePath = null
  ) => Load(modPaths, gamePath, ["*"], asGame: true);

  /// <summary>As <see cref="LoadAssets(IReadOnlyList{string}, string)"/>, and the vanilla survival
  /// blocktype and itemtype files that define <paramref name="vanillaCodes"/> load with the mods.</summary>
  /// <param name="modPaths">As the public overload takes them.</param>
  /// <param name="gamePath">As the public overload takes it.</param>
  /// <param name="vanillaCodes">Block and item codes in the <c>game</c> domain, with or without the domain,
  /// <c>*</c> allowed; one no vanilla file defines loads nothing.</param>
  /// <returns>This world.</returns>
  internal TestWorld LoadAssets(
    IReadOnlyList<string> modPaths,
    string? gamePath,
    IReadOnlyCollection<string> vanillaCodes,
    bool recipeCodes = false
  ) => Load(modPaths, gamePath, vanillaCodes, asGame: false, recipeCodes);

  private TestWorld Load(
    IReadOnlyList<string> modPaths,
    string? gamePath,
    IReadOnlyCollection<string> vanillaCodes,
    bool asGame,
    bool recipeCodes = false
  ) {
    gamePath ??=
      VsAssemblyResolver.InstallPath
      ?? throw new InvalidOperationException(
        "No game install found - set the game's env var or provision .game/<slug>."
      );
    string assetsPath = Path.Combine(gamePath, "assets");
    List<ListedMod> mods = ReadWithDependencies(modPaths);

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
      MirrorAssets(
        mgr,
        Path.Combine(mod.Path, "assets", mod.Id),
        mod.Id,
        asGame
      );
    if (asGame)
      MirrorArt(mgr.Add, survival);
    MirrorPatchedVanilla(mgr, survival, [.. mods.Select(m => m.Id)]);
    var skips = new List<(string File, string Reason)>();
    MirrorVanillaTypes(mgr.Add, survival, vanillaCodes, skips);

    // GamePaths.AssetsPath/Lang.Load are process-wide statics the object loader needs primed; safe
    // to set repeatedly.
    lock (LangGate) {
      ReflectionHelpers.SetStaticField(
        typeof(GamePaths),
        "<AssetsPath>k__BackingField",
        assetsPath
      );
      Lang.Load(Log, mgr, "en");
    }

    ICoreServerAPI loaderApi = BuildLoaderApi(
      mgr,
      out ClassRegistry rawClassRegistry
    );
    foreach (ListedMod mod in mods)
      if (Mods.GetMod(mod.Id) == null)
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
#if GAME_GE_1_22
      if (recipeCodes)
        MirrorVanillaTypes(
          (location, asset) =>
          {
            if (!mgr.AllAssets.ContainsKey(location))
              mgr.Add(location, asset);
          },
          survival,
          RecipeVanillaCodes(mgr),
          skips
        );
#endif
      RunVanillaLoader(
        loaderApi,
        "Vintagestory.ServerMods.NoObf.ModRegistryObjectTypeLoader"
      );
      RegisterLoaded(loaderApi);
      ((ICoreAPI)Api).Assets.Returns(mgr);
#if GAME_GE_1_22
      _loadedAssets = mgr;
      _gameSurvival = survival;
#endif
      if (asGame) {
        foreach (ModSystem system in FinalizeOrder(started))
          system.AssetsFinalize(loaderApi);
      }
    } finally {
      for (int i = started.Count - 1; i >= 0; i--)
        started[i].Dispose();
      LoadedSystems = started;
      skips.AddRange(Log.Errors.Select(e => ("(load errors)", e)));
      VanillaSkips = skips;
    }
    return this;
  }

  /// <summary>The systems in the order the game calls their <c>AssetsFinalize</c>: ascending
  /// <c>ExecuteOrder</c>, ties in list order.</summary>
  internal static List<ModSystem> FinalizeOrder(
    IEnumerable<ModSystem> systems
  ) => [.. systems.OrderBy(s => s.ExecuteOrder())];

  // Serialises Lang.Load, which rewrites the game's process-wide language table.
  private static readonly object LangGate = new();

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
    lock (LangGate) {
      ReflectionHelpers.SetStaticField(
        typeof(GamePaths),
        "<AssetsPath>k__BackingField",
        assetsPath
      );
      Lang.Load(Log, mgr, "en");
    }

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
        GameStatics.Load(collectible, Api);
      }
    return loaded;
  }

  /// <summary>The mod systems the last <see cref="LoadAssets(IReadOnlyList{string}, string)"/>
  /// started, exlib's then each listed mod's, in start order; every one was disposed, the
  /// last-started first, before it returned.</summary>
  internal IReadOnlyList<ModSystem> LoadedSystems { get; private set; } = [];

  /// <summary>The vanilla type files the last <see cref="LoadAssets(IReadOnlyList{string}, string)"/>
  /// left out, each with the reason: a file that does not parse or has no <c>code</c>, and every
  /// error the world's log held after the load, from any loader or mod system, whose file is
  /// <c>(load errors)</c>.</summary>
  internal IReadOnlyList<(string File, string Reason)> VanillaSkips {
    get;
    private set;
  } = [];

  /// <summary>The full names of the install's vanilla mod systems whose <c>Start</c> threw against
  /// the last <see cref="LoadAssets(IReadOnlyList{string}, string)"/>'s class-registration API, in start order.</summary>
  internal IReadOnlyList<string> PassedOverVanillaSystems {
    get;
    private set;
  } = [];

  /// <summary>A listed mod as <see cref="LoadAssets(IReadOnlyList{string}, string)"/> reads its
  /// folder: <paramref name="Assembly"/> is null for a content mod.</summary>
  internal sealed record ListedMod(
    string Path,
    string Id,
    string Version,
    string[] Dependencies,
    Assembly? Assembly
  );

  /// <summary>Reads <paramref name="modPaths"/> in order, each preceded by the dependencies its
  /// <c>modinfo.json</c> names that are not listed or read already: the ids other than
  /// <c>game</c>, <c>survival</c> and <c>creative</c>, in the folder <see cref="RepoPaths.Assets"/>
  /// finds for the id. exlib is read for its assets alone, its systems being started apart. A dependency
  /// whose folder cannot be read is skipped.</summary>
  /// <exception cref="InvalidOperationException">No <c>modinfo.json</c> or modid resolves in a
  /// listed folder, or a listed mod that is not a content mod has no compiled dll under
  /// <c>bin/</c>.</exception>
  internal static List<ListedMod> ReadWithDependencies(
    IReadOnlyList<string> modPaths
  ) {
    List<ListedMod> listed = [.. modPaths.Select(p => ReadListedMod(p))];
    HashSet<string> known = new(
      listed.Select(m => m.Id),
      StringComparer.OrdinalIgnoreCase
    );
    List<ListedMod> ordered = [];

    void Add(ListedMod mod) {
      foreach (string id in mod.Dependencies) {
        if (id is "game" or "survival" or "creative" || !known.Add(id))
          continue;
        string assets = RepoPaths.Assets(id);
        try {
          Add(
            ReadListedMod(
              Path.GetDirectoryName(Path.GetDirectoryName(assets))!,
              withCode: id != "exlib"
            )
          );
        } catch (InvalidOperationException) { }
      }
      ordered.Add(mod);
    }

    foreach (ListedMod mod in listed)
      Add(mod);
    return ordered;
  }

  /// <exception cref="InvalidOperationException">No <c>modinfo.json</c> or modid resolves, or a mod
  /// that is not a content mod has no compiled dll under <c>bin/</c>.</exception>
  private static ListedMod ReadListedMod(string modPath, bool withCode = true) {
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
      isContentMod || !withCode
        ? null
        : Assembly.LoadFrom(FindModAssembly(modRoot, modId))
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

  /// <summary>Adds to <paramref name="mgr"/> each <c>game:</c> file that a patch of one of
  /// <paramref name="modIds"/> names, read from <paramref name="survival"/>, whatever the patch's
  /// conditions. A wildcard target names no file and a file the install lacks is not added; the
  /// patch loader meets both as the game's does.</summary>
  /// <exception cref="JsonReaderException">A listed mod's patch file is not JSON.</exception>
  private static void MirrorPatchedVanilla(
    AssetManager mgr,
    IAssetOrigin survival,
    IReadOnlyCollection<string> modIds
  ) {
    HashSet<AssetLocation> targets = [];
    foreach (
      IAsset patches in mgr.AllAssets.Values.Where(a =>
        modIds.Contains(a.Location.Domain)
        && a.Location.Path.StartsWith("patches/", StringComparison.Ordinal)
      )
    )
      foreach (
        JObject patch in JToken.Parse(patches.ToText()).Children<JObject>()
      )
        if (patch["file"] is JValue { Value: string file })
          targets.Add(
            AssetLocation
              .Create(file, patches.Location.Domain)
              .WithPathAppendixOnce(".json")
          );
    foreach (AssetLocation target in targets) {
      string path = Path.Combine(survival.OriginPath, target.Path);
      if (target.Domain == GlobalConstants.DefaultDomain && File.Exists(path))
        mgr.Add(target, new Asset(File.ReadAllBytes(path), target, survival));
    }
  }

  /// <summary>As the delegate overload, over the files of <c>blocktypes</c> and <c>itemtypes</c>
  /// under <paramref name="originPath"/>, adding them nowhere.</summary>
  /// <returns>The locations added.</returns>
  internal static List<AssetLocation> MirrorVanillaTypes(
    string originPath,
    IReadOnlyCollection<string> codes,
    List<(string File, string Reason)> skips
  ) =>
    MirrorVanillaTypes(
      (_, _) => { },
      new PathOrigin("game", originPath),
      codes,
      skips
    );

  /// <summary>Passes to <paramref name="add"/> each survival blocktype and itemtype file whose
  /// <c>code</c> can begin one of <paramref name="codes"/>: the code is the file's, or the file's
  /// followed by a <c>-</c>, or a wildcard's literal text before its first <c>*</c> and the file's
  /// code lead into one another. A file with an <c>ArtPigment</c> behaviour also brings the files
  /// its <c>decorBlockCodes</c> name. A file with no parse or no string <c>code</c> is a skip.</summary>
  /// <returns>The locations passed to <paramref name="add"/>.</returns>
  internal static List<AssetLocation> MirrorVanillaTypes(
    Action<AssetLocation, IAsset> add,
    IAssetOrigin survival,
    IReadOnlyCollection<string> codes,
    List<(string File, string Reason)> skips
  ) {
    List<AssetLocation> added = [];
    List<IAsset> assets = MirrorTypeFiles(add, survival, codes, skips, added);
    HashSet<string> decor = [];
    foreach (IAsset asset in assets)
      foreach (
        JToken token in JToken
          .Parse(asset.ToText())
          .SelectTokens("$..[?(@.name == 'ArtPigment')]..decorBlockCodes[*]")
      )
        if (token is JValue { Value: string decorCode })
          decor.Add(decorCode);
    MirrorTypeFiles(add, survival, decor, [], added);
    return added;
  }

  private static List<IAsset> MirrorTypeFiles(
    Action<AssetLocation, IAsset> add,
    IAssetOrigin survival,
    IReadOnlyCollection<string> codes,
    List<(string File, string Reason)> skips,
    List<AssetLocation> added
  ) {
    string[] wanted =
    [
      .. codes.Select(c => c.StartsWith("game:") ? c[5..] : c),
    ];
    List<IAsset> assets = [];
    if (wanted.Length == 0)
      return assets;
    foreach (
      AssetCategory category in (AssetCategory[])
        [AssetCategory.blocktypes, AssetCategory.itemtypes]
    )
      foreach (IAsset asset in survival.GetAssets(category, shouldLoad: true)) {
        if (added.Contains(asset.Location))
          continue;
        JObject file;
        try {
          file = JObject.Parse(asset.ToText());
        } catch (JsonException e) {
          skips.Add((asset.Location.ToString(), e.Message));
          continue;
        }
        if (file["code"] is not JValue { Value: string code }) {
          skips.Add((asset.Location.ToString(), "no code"));
          continue;
        }
        if (wanted.Any(w => MayDefine(code, w))) {
          add(asset.Location, asset);
          added.Add(asset.Location);
          assets.Add(asset);
        }
      }
    return assets;
  }

  private static bool MayDefine(string code, string wanted) {
    string stem = code + "-";
    int star = wanted.IndexOf('*');
    if (star < 0)
      return wanted == code
        || wanted.StartsWith(stem, StringComparison.Ordinal);
    string literal = wanted[..star];
    return stem.StartsWith(literal, StringComparison.Ordinal)
      || literal.StartsWith(stem, StringComparison.Ordinal);
  }

  /// <summary>Copies every asset under <paramref name="fullPath"/> for <paramref name="domain"/> into
  /// <paramref name="mgr"/>'s live asset dictionary; <c>AssetManager.AddPathOrigin</c> alone does not
  /// reach it.</summary>
  private static void MirrorAssets(
    AssetManager mgr,
    string fullPath,
    string domain,
    bool withArt = false
  ) {
    if (!Directory.Exists(fullPath))
      return;
    var origin = new PathOrigin(domain, fullPath);
    foreach (AssetCategory cat in KnownCategories)
      foreach (IAsset a in origin.GetAssets(cat, shouldLoad: true))
        mgr.Add(a.Location, a);
    if (withArt)
      MirrorArt(mgr.Add, origin);
  }

  /// <summary>Passes to <paramref name="add"/> every shape and texture asset of
  /// <paramref name="origin"/>.</summary>
  private static void MirrorArt(
    Action<AssetLocation, IAsset> add,
    IAssetOrigin origin
  ) {
    foreach (AssetCategory cat in ArtCategories)
      foreach (IAsset a in origin.GetAssets(cat, shouldLoad: true))
        add(a.Location, a);
  }

  private static readonly AssetCategory[] ArtCategories =
  [
    AssetCategory.shapes,
    AssetCategory.textures,
  ];

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
    TyronThreadPool.Inst.Logger ??= new ThreadPoolLogger();
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
