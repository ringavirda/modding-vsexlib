using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using ExpandedLib.Catalogues;
using ExpandedLib.Definitions;
using ExpandedLib.Industry;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A world starting to load empties every process-wide registry of exlib and
/// exlib.industry the previous world filled: on the server always, on a client only when it joins a
/// remote server.</summary>
[Collection("WorldState")]
public class ExWorldStateTests {
  // Keyed by the static root's full name; each value is why the root is a cache.
  private static readonly Dictionary<string, string> Caches = new() {
    ["ExpandedLib.Blocks.PersistScan._cache"] =
      "per-type binders, a pure function of the loaded types",
    ["ExpandedLib.Catalogues.BayOccupancyLoader.RootKeys"] = SchemaKeys,
    ["ExpandedLib.Catalogues.BayOccupancyLoader.ItemKeys"] = SchemaKeys,
    ["ExpandedLib.Catalogues.ProcessJobLoader.RootKeys"] = SchemaKeys,
    ["ExpandedLib.Catalogues.ProcessJobLoader.JobKeys"] = SchemaKeys,
    ["ExpandedLib.Catalogues.ProcessRouteLoader.RootKeys"] = SchemaKeys,
    ["ExpandedLib.Catalogues.ProcessRouteLoader.StageKeys"] = SchemaKeys,
    ["ExpandedLib.Definitions.KnownRootKeys.<Block>k__BackingField"] =
      "the root keys the game's block loader reads, constant",
    ["ExpandedLib.Definitions.KnownRootKeys.<Item>k__BackingField"] =
      "the root keys the game's item loader reads, constant",
    ["ExpandedLib.Config.ExConfigDocument._byApi"] = WeakByWorld,
    ["ExpandedLib.Registries.EntityRegistry._bareKeysIssued"] = WeakByWorld,
    ["ExpandedLib.Structures.JsonMultiblockLayout._resolved"] = WeakByWorld,
    ["ExpandedLib.Structures.CellRole._single"] = TypeInitializers,
    ["ExpandedLib.Helpers.ExHighlightSlots._slots"] = TypeInitializers,
    ["ExpandedLib.Helpers.ExMeasure._conversions"] =
      "unit conversions rebuilt whenever the language's unit symbols change",
    ["ExpandedLib.Registries.ExModules._all"] = LoadedAssemblies,
    ["ExpandedLib.Registries.ExModules._entryPointErrors"] = LoadedAssemblies,
    ["ExpandedLib.Registries.ExHarmony.AppliedCategories"] = LivePatches,
    ["ExpandedLib.Registries.ExHarmony.UncategorizedPatched"] = LivePatches,
    ["ExpandedLib.Registries.IncompatibleMods.Known"] =
      "the known clashing mods, constant",
    ["ExpandedLib.Structures.MultiblockCellRoles.None"] = EmptySentinel,
    ["ExpandedLib.Structures.MultiblockCellRoles.NoCells"] = EmptySentinel,
    ["ExpandedLib.Structures.MultiblockConnectors.None"] = EmptySentinel,
    ["ExpandedLib.Structures.MultiblockFacings.None"] = EmptySentinel,
    ["ExpandedLib.Helpers.ExOrientations.Face"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.FaceAll"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.Axis"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.AxisFlat"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.DirectedAxisFlat"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.DirectedAxis"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.PipeBend"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.CanalBend"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.PipeTee"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.CanalTee"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.PipeCross"] = Orientations,
    ["ExpandedLib.Helpers.ExOrientations.CanalCross"] = Orientations,
    ["ExpandedLib.Industry.Helpers.ExSounds.ClipLengths"] =
      "the length of each shipped sound clip, constant",
    ["ExpandedLib.Industry.Metals.MetalFamilyEmitter.Builders"] = Emitters,
    ["ExpandedLib.Industry.Metals.MetalToolEmitter.Presets"] = Emitters,
    ["ExpandedLib.Industry.Metals.MetalToolEmitter.ToolTemplates"] = Emitters,
    ["ExpandedLib.Helpers.ExHighlightSlots._next"] = TypeInitializers,
    ["ExpandedLib.Registries.ExModules._lastAssemblyCount"] = LoadedAssemblies,
    ["ExpandedLib.Helpers.ExMeasure._conversionSig"] =
      "the unit symbols the conversions were built for",
    ["ExpandedLib.Helpers.ExMeasure._metricRegex"] =
      "built from the language's unit symbols with the conversions",
    ["ExpandedLib.Helpers.ExMeasure.<System>k__BackingField"] =
      PlayerPreference,
    ["ExpandedLib.Industry.Helpers.ExSounds._machineVolume"] = PlayerPreference,
    ["ExpandedLib.Structures.StructureFillers.<FillerCode>k__BackingField"] =
      "the one filler block exlib's Start names, the same in every world",
  };

  private const string SchemaKeys = "the catalogue schema's key set, constant";
  private const string WeakByWorld =
    "weak table keyed by a world's own object; the next world brings new keys";
  private const string TypeInitializers =
    "filled once per process by type initializers";
  private const string LoadedAssemblies =
    "rescanned from the loaded assemblies whenever their count changes";
  private const string LivePatches =
    "mirrors the live Harmony patches; emptied with them by UnpatchAll";
  private const string EmptySentinel = "an empty sentinel nothing writes";
  private const string Orientations = "a constant orientation scheme";
  private const string Emitters = "the emitters' constant templates";
  private const string PlayerPreference =
    "the local player's display preference, read from the client's preferences file, not the world";

  private static Assembly[] Assemblies =>
    [typeof(ExWorldState).Assembly, typeof(IndustryModule).Assembly];

  private static ExModuleModSystem Driver(TestWorld world) {
    var system = new ExModuleModSystem();
    ReflectionHelpers.SetProperty(
      system,
      nameof(ModSystem.Mod),
      world.Mods.GetMod("exlib")!
    );
    return system;
  }

  [Fact]
  public void A_server_load_start_empties_every_static_collection_that_is_not_a_cache() {
    var filled = new List<StaticCells.Fill>();
    var unfillable = new List<string>();
    foreach ((string root, FieldInfo field) in StaticCells.Roots(Assemblies))
      if (!Caches.ContainsKey(root))
        StaticCells.FillRoot(root, field, filled, unfillable);
    Assert.Empty(unfillable);
    Assert.Contains(
      filled,
      f => f.Path.StartsWith("ExpandedLib.Definitions.ExDefinitions._blocks")
    );
    Assert.True(filled.Count >= 40, $"only {filled.Count} collections filled");

    var world = new TestWorld();
    Driver(world).StartPre(world.Api);

    string[] survivors =
    [
      .. filled.Where(f => f.Survives()).Select(f => f.Path),
    ];
    Assert.True(
      survivors.Length == 0,
      "Survives a load start - reset it where it is owned (ExWorldState, IndustryModule.StartPre) "
        + "or list it as a cache with its reason:\n"
        + string.Join("\n", survivors)
    );
  }

  // Fails when a settable static's reset goes: recovery fallback, mold gate, sound channel, filler
  // latch, preferences api, injection flag, temperature formatter.
  [Fact]
  public void A_server_load_start_returns_every_settable_static_value_it_holds() {
    var planted = new List<StaticCells.Fill>();
    var unplantable = new List<string>();
    foreach ((string root, FieldInfo field) in StaticCells.Roots(Assemblies))
      if (!Caches.ContainsKey(root))
        StaticCells.PlantValue(root, field, planted, unplantable);
    Assert.Empty(unplantable);
    Assert.Contains(
      planted,
      f =>
        f.Path
        == "ExpandedLib.Industry.Metals.MetalRegistry.<DefaultRecoveryFallback>k__BackingField"
    );

    var world = new TestWorld();
    Driver(world).StartPre(world.Api);

    string[] survivors =
    [
      .. planted.Where(f => f.Survives()).Select(f => f.Path),
    ];
    Assert.True(
      survivors.Length == 0,
      "Survives a load start - reset it where it is owned (ExWorldState, IndustryModule.StartPre) "
        + "or list it as a cache with its reason:\n"
        + string.Join("\n", survivors)
    );
  }

  // Fails when the liquid reset stops seeding the built-in media, or keeps a registered one.
  [Fact]
  public void A_server_load_start_leaves_exactly_the_four_built_in_liquids() {
    ExLiquids.Register(new LiquidDef { Code = "worldstatetest-brine" });

    var world = new TestWorld();
    Driver(world).StartPre(world.Api);

    Assert.Equal(
      ["Air", "Exhaust", "Steam", "Water"],
      ExLiquids.All.Select(d => d.Code).Order()
    );
  }

  [Fact]
  public void Every_listed_cache_names_a_static_that_exists() {
    HashSet<string> roots =
    [
      .. StaticCells.Roots(Assemblies).Select(r => r.Root),
    ];
    Assert.Empty(Caches.Keys.Where(k => !roots.Contains(k)));
  }

  [Fact]
  public void A_singleplayer_client_start_leaves_the_servers_state_whole() {
    var world = new TestWorld();
    world.ClientApi.IsSinglePlayer.Returns(true);
    ((ICoreAPI)world.ClientApi).ModLoader.Returns(world.Mods);
    RegisterServerState();

    Driver(world).StartPre(world.ClientApi);

    Assert.Contains(ExDefinitions.Blocks, d => d.Code == "worldstate");
    Assert.True(MetalRegistry.TryGet("worldstatetest:molten", out _));
  }

  [Fact]
  public void A_remote_client_start_resets_it() {
    var world = new TestWorld();
    world.ClientApi.IsSinglePlayer.Returns(false);
    ((ICoreAPI)world.ClientApi).ModLoader.Returns(world.Mods);
    RegisterServerState();

    Driver(world).StartPre(world.ClientApi);

    Assert.DoesNotContain(ExDefinitions.Blocks, d => d.Code == "worldstate");
    Assert.False(MetalRegistry.TryGet("worldstatetest:molten", out _));
  }

  private static void RegisterServerState() {
    ExDefinitions.RegisterBlock(
      ExBlockDef.Create("worldstatetest", "worldstate")
    );
    MetalRegistry.Register(
      new MetalDef { Code = "worldstate", MoltenItem = "worldstatetest:molten" }
    );
    Assert.Contains(ExDefinitions.Blocks, d => d.Code == "worldstate");
    Assert.True(MetalRegistry.TryGet("worldstatetest:molten", out _));
  }
}

/// <summary>Walks the static fields of an assembly set down to every collection they hold, and plants
/// a recognisable entry in each.</summary>
/// <remarks>A root is every non-constant static field of a type the compiler did not generate. From
/// a root the walk descends through objects of the walked assemblies' own types into their instance
/// fields; a collection ends the walk. A <c>static readonly</c> array is fixed at type
/// initialisation and not a root; an array inside an object is not walked.</remarks>
internal static class StaticCells {
  /// <summary>One planted entry: the path of the collection it went into, and whether that path
  /// still holds it.</summary>
  internal sealed record Fill(string Path, Func<bool> Survives);

  internal static IEnumerable<(string Root, FieldInfo Field)> Roots(
    IEnumerable<Assembly> assemblies
  ) {
    foreach (Assembly asm in assemblies)
      foreach (Type type in asm.GetTypes())
        if (!IsGenerated(type))
          foreach (
            FieldInfo field in type.GetFields(
              BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly
            )
          )
            if (
              !field.IsLiteral && !(field.IsInitOnly && field.FieldType.IsArray)
            )
              yield return ($"{type.FullName}.{field.Name}", field);
  }

  internal static void FillRoot(
    string root,
    FieldInfo field,
    List<Fill> filled,
    List<string> unfillable
  ) {
    if (field.DeclaringType!.ContainsGenericParameters) {
      if (IsCollectionType(field.FieldType))
        unfillable.Add(root + " (static of an open generic type)");
      return;
    }
    object? value = field.GetValue(null);
    if (field.FieldType.IsArray) {
      Type element = field.FieldType.GetElementType()!;
      Array planted = Array.CreateInstance(element, 1);
      object marker = Placeholder(element);
      planted.SetValue(marker, 0);
      field.SetValue(null, planted);
      filled.Add(
        new Fill(
          root,
          () =>
            field.GetValue(null) is Array now && Array.IndexOf(now, marker) >= 0
        )
      );
      return;
    }
    if (value == null && IsCollectionType(field.FieldType)) {
      unfillable.Add(root + " (null collection field)");
      return;
    }

    Walk(
      value,
      root,
      0,
      new(ReferenceEqualityComparer.Instance),
      filled,
      unfillable
    );
  }

  private static void Walk(
    object? value,
    string path,
    int depth,
    HashSet<object> seen,
    List<Fill> filled,
    List<string> unfillable
  ) {
    if (value == null || depth > 4)
      return;
    Type type = value.GetType();
    if (type.IsPrimitive || type.IsEnum || value is string or Delegate or Array)
      return;
    if (!type.IsValueType && !seen.Add(value))
      return;
    if (IsCollectionType(type)) {
      Fill? fill = Plant(value, path);
      if (fill == null)
        unfillable.Add($"{path} ({type.Name})");
      else
        filled.Add(fill);
      return;
    }
    if (!IsWalked(type)) {
      if (type.IsGenericType && !type.IsValueType)
        unfillable.Add(
          $"{path} ({type.Name}, a wrapper the walk cannot plant in)"
        );
      return;
    }
    for (Type? t = type; t != null && IsWalked(t); t = t.BaseType)
      foreach (
        FieldInfo f in t.GetFields(
          BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly
        )
      )
        Walk(
          f.GetValue(value),
          $"{path}.{f.Name}",
          depth + 1,
          seen,
          filled,
          unfillable
        );
  }

  /// <summary>Plants a sentinel in a settable static root that holds no collection: a value type set
  /// to another value, a string, delegate or object set to a new instance. Collection roots, and
  /// readonly or open-generic roots, are left to <see cref="FillRoot"/>.</summary>
  internal static void PlantValue(
    string root,
    FieldInfo field,
    List<Fill> planted,
    List<string> unplantable
  ) {
    if (
      field.IsInitOnly
      || field.DeclaringType!.ContainsGenericParameters
      || field.FieldType.IsArray
      || IsCollectionType(field.FieldType)
    )
      return;
    Type type = field.FieldType;
    object? sentinel;
    if (type == typeof(bool))
      sentinel = !(bool)field.GetValue(null)!;
    else if (type.IsEnum) {
      Array values = Enum.GetValues(type);
      object now = field.GetValue(null)!;
      sentinel = values.Cast<object>().FirstOrDefault(v => !v.Equals(now));
    } else if (type.IsPrimitive)
      sentinel = Convert.ChangeType(
        Convert.ToDouble(field.GetValue(null)) + 7,
        type
      );
    else if (type.IsValueType)
      sentinel = null;
    else
      sentinel = Placeholder(type);
    if (sentinel == null) {
      unplantable.Add($"{root} ({type.Name})");
      return;
    }
    field.SetValue(null, sentinel);
    planted.Add(
      new Fill(
        root,
        () =>
          type.IsValueType
            ? sentinel.Equals(field.GetValue(null))
            : ReferenceEquals(sentinel, field.GetValue(null))
      )
    );
  }

  // The returned check reads the collection again through its path: a registry replaced by a new
  // object reads as emptied.
  private static Fill? Plant(object collection, string path) {
    Type type = collection.GetType();
    if (collection is IDictionary dict) {
      Type[] kv = GenericArgs(type, typeof(IDictionary<,>));
      object key = Placeholder(kv[0]);
      dict[key] = Placeholder(kv[1]);
      return new Fill(
        path,
        () => Resolve(path) is IDictionary now && now.Contains(key)
      );
    }
    Type[]? element = GenericArgsOrNull(type, typeof(ICollection<>));
    MethodInfo? add = type.GetMethod("Add", element ?? Type.EmptyTypes);
    MethodInfo? contains = type.GetMethod(
      "Contains",
      element ?? Type.EmptyTypes
    );
    if (element == null || add == null || contains == null)
      return null;
    object item = Placeholder(element[0]);
    add.Invoke(collection, [item]);
    return new Fill(
      path,
      () =>
        Resolve(path) is { } now
        && now.GetType().GetMethod("Contains", element) is { } has
        && (bool)has.Invoke(now, [item])!
    );
  }

  // A path is a static root's full name followed by instance field names; null once a step is null.
  private static object? Resolve(string path) {
    foreach ((string root, FieldInfo field) in Roots([AssemblyOf(path)]))
      if (path == root || path.StartsWith(root + ".")) {
        object? value = field.GetValue(null);
        foreach (
          string name in path[root.Length..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
        ) {
          if (value == null)
            return null;
          FieldInfo? next = null;
          for (
            Type? t = value.GetType();
            t != null && next == null;
            t = t.BaseType
          )
            next = t.GetField(
              name,
              BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly
            );
          value = next?.GetValue(value);
        }
        return value;
      }
    return null;
  }

  private static Assembly AssemblyOf(string path) =>
    path.StartsWith("ExpandedLib.Industry.")
      ? typeof(IndustryModule).Assembly
      : typeof(ExWorldState).Assembly;

  private static bool IsWalked(Type type) =>
    type.Assembly == typeof(ExWorldState).Assembly
    || type.Assembly == typeof(IndustryModule).Assembly;

  private static bool IsGenerated(Type type) =>
    type.Name.Contains('<')
    || type.GetCustomAttribute<CompilerGeneratedAttribute>() != null
    || (type.DeclaringType != null && IsGenerated(type.DeclaringType));

  private static bool IsCollectionType(Type type) =>
    type != typeof(string)
    && (
      typeof(IEnumerable).IsAssignableFrom(type)
      || (
        type.IsGenericType
        && type.GetGenericTypeDefinition() == typeof(ConditionalWeakTable<,>)
      )
    );

  private static Type[] GenericArgs(Type type, Type open) =>
    GenericArgsOrNull(type, open)
    ?? throw new InvalidOperationException($"{type} is not {open.Name}");

  private static Type[]? GenericArgsOrNull(Type type, Type open) =>
    type.GetInterfaces()
      .FirstOrDefault(i =>
        i.IsGenericType && i.GetGenericTypeDefinition() == open
      )
      ?.GetGenericArguments();

  private static object Placeholder(Type type) {
    if (type == typeof(string))
      return "exlib-worldstate-" + Guid.NewGuid().ToString("N");
    if (type == typeof(Type))
      return typeof(Marker);
    if (type == typeof(Assembly))
      return typeof(Marker).Assembly;
    if (type == typeof(object))
      return new Marker();
    if (type.IsArray)
      return Array.CreateInstance(type.GetElementType()!, 0);
    if (typeof(Delegate).IsAssignableFrom(type)) {
      MethodInfo invoke = type.GetMethod("Invoke")!;
      return Expression
        .Lambda(
          type,
          invoke.ReturnType == typeof(void)
            ? Expression.Empty()
            : Expression.Default(invoke.ReturnType),
          invoke
            .GetParameters()
            .Select(p => Expression.Parameter(p.ParameterType))
        )
        .Compile();
    }
    if (type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple`"))
      return Activator.CreateInstance(
        type,
        [.. type.GetGenericArguments().Select(Placeholder)]
      )!;
    if (type.IsValueType)
      return Activator.CreateInstance(type)!;
    if (type.IsInterface || type.IsAbstract)
      return Substitute.For([type], []);
    return
      type.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        Type.EmptyTypes
      )
        is { } ctor
      ? ctor.Invoke([])
      : RuntimeHelpers.GetUninitializedObject(type);
  }

  private sealed class Marker;
}
