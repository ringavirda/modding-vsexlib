using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Definitions;

/// <summary>Process-wide registry of code-first block, item and recipe definitions, keyed by
/// asset location so a re-register replaces rather than duplicates.</summary>
public static class ExDefinitions {
  private static readonly ExKeyedRegistry<ExBlockDef> _blocks = new(d =>
    d.Location.ToString()
  );

  private static readonly ExKeyedRegistry<ExItemDef> _items = new(d =>
    d.Location.ToString()
  );

  private static readonly ExKeyedRegistry<ExRecipeDef> _recipes = new(d =>
    d.Location.ToString()
  );

  // Location -> the assembly whose provider last registered it.
  private static readonly Dictionary<string, Assembly> _blockProviders = new(
    StringComparer.Ordinal
  );
  private static readonly Dictionary<string, Assembly> _itemProviders = new(
    StringComparer.Ordinal
  );
  private static readonly Dictionary<string, Assembly> _recipeProviders = new(
    StringComparer.Ordinal
  );

  // Types, never instances: a contributor is instantiated fresh by RunContributors.
  private static readonly List<Type> _contributors = [];

  // The location of every block, item and recipe def actually injected, recorded by RecordInjected.
  private static readonly HashSet<string> _injected = new(
    StringComparer.Ordinal
  );

  /// <summary>Whether an injection pass has run in this process. Reset by <see cref="Clear"/>.</summary>
  internal static bool InjectionRan { get; private set; }

  /// <summary>Log sink for exlib's own definition diagnostics. Null before startup.</summary>
  public static ILogger? Logger { get; set; }

  /// <summary>Registers (or replaces) a code-first block definition.</summary>
  public static void RegisterBlock(ExBlockDef def) =>
    RegisterBlock(def, Assembly.GetCallingAssembly());

  internal static void RegisterBlock(ExBlockDef def, Assembly providerAssembly) {
    TrackProvider(_blockProviders, def.Location.ToString(), providerAssembly);
    _blocks.Register(def);
  }

  /// <summary>Registers (or replaces) a code-first item definition.</summary>
  public static void RegisterItem(ExItemDef def) =>
    RegisterItem(def, Assembly.GetCallingAssembly());

  internal static void RegisterItem(ExItemDef def, Assembly providerAssembly) {
    TrackProvider(_itemProviders, def.Location.ToString(), providerAssembly);
    _items.Register(def);
  }

  /// <summary>Registers (or replaces) a code-first recipe file.</summary>
  public static void RegisterRecipe(ExRecipeDef def) =>
    RegisterRecipe(def, Assembly.GetCallingAssembly());

  internal static void RegisterRecipe(
    ExRecipeDef def,
    Assembly providerAssembly
  ) {
    TrackProvider(_recipeProviders, def.Location.ToString(), providerAssembly);
    _recipes.Register(def);
  }

  // Records which assembly registered `location` this time, and logs when that differs from last time.
  private static void TrackProvider(
    Dictionary<string, Assembly> providers,
    string location,
    Assembly providerAssembly
  ) {
    if (
      providers.TryGetValue(location, out Assembly? prior)
      && prior != providerAssembly
    )
      Logger?.Notification(
        "[exlib] {0} re-registered by {1} (was {2})",
        location,
        providerAssembly.GetName().Name,
        prior.GetName().Name
      );
    providers[location] = providerAssembly;
  }

  /// <summary>Every registered block definition.</summary>
  public static IReadOnlyCollection<ExBlockDef> Blocks => _blocks.Values;

  /// <summary>Every registered item definition.</summary>
  public static IReadOnlyCollection<ExItemDef> Items => _items.Values;

  /// <summary>Every registered recipe file.</summary>
  public static IReadOnlyCollection<ExRecipeDef> Recipes => _recipes.Values;

  /// <summary>Every discovered <see cref="IExDefinitionContributor"/> type, in discovery order.</summary>
  public static IReadOnlyList<Type> Contributors => _contributors.AsReadOnly();

  /// <summary>Drops every registered definition (used by tests to isolate the static registry).</summary>
  public static void Clear() {
    _blocks.Clear();
    _items.Clear();
    _recipes.Clear();
    _blockProviders.Clear();
    _itemProviders.Clear();
    _recipeProviders.Clear();
    _contributors.Clear();
    _injected.Clear();
    InjectionRan = false;
  }

  /// <summary>Records that injection ran and which locations it covered.</summary>
  internal static void RecordInjected(IEnumerable<AssetLocation> locations) {
    InjectionRan = true;
    foreach (AssetLocation location in locations)
      _injected.Add(location.ToString());
  }

  /// <summary>Whether <paramref name="location"/> was covered by the recorded injection pass.</summary>
  internal static bool WasInjected(AssetLocation location) =>
    _injected.Contains(location.ToString());

  /// <summary>Builds a <c>type -&gt; orientation states</c> map from a class's code-first defs.</summary>
  public static Dictionary<string, string[]> OrientationMap(
    IEnumerable<ExBlockDef> defs
  ) {
    var map = new Dictionary<string, string[]>();
    foreach (ExBlockDef d in defs) {
      string[] orientations = d.VariantStates("orientation");
      foreach (string type in d.VariantStates("type"))
        map[type] = orientations;
    }
    return map;
  }

  /// <summary>Scans <paramref name="asm"/> for <see cref="IExBlockDefProvider"/> classes and
  /// registers each one's co-located definition.</summary>
  /// <returns>How many were registered.</returns>
  public static int DiscoverAndRegister(string domain, Assembly asm) =>
    Discover<ExBlockDef>(
      domain,
      asm,
      typeof(IExBlockDefProvider),
      def => RegisterBlock(def, asm)
    );

  /// <summary>Item-side sibling of <see cref="DiscoverAndRegister"/>.</summary>
  /// <returns>How many were registered.</returns>
  public static int DiscoverAndRegisterItems(string domain, Assembly asm) =>
    Discover<ExItemDef>(
      domain,
      asm,
      typeof(IExItemDefProvider),
      def => RegisterItem(def, asm)
    );

  /// <summary>Recipe-side sibling of <see cref="DiscoverAndRegister"/>.</summary>
  /// <returns>How many were registered.</returns>
  public static int DiscoverAndRegisterRecipes(string domain, Assembly asm) =>
    Discover<ExRecipeDef>(
      domain,
      asm,
      typeof(IExRecipeDefProvider),
      def => RegisterRecipe(def, asm)
    );

  /// <summary>Scans <paramref name="asm"/> for concrete <see cref="IExDefinitionContributor"/>
  /// types with a parameterless constructor and records each once. One without a parameterless
  /// constructor is logged as a warning and skipped.</summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static void DiscoverContributors(Assembly asm) {
    foreach (Type type in ReflectionScan.GetCandidateTypes(asm)) {
      if (!typeof(IExDefinitionContributor).IsAssignableFrom(type))
        continue;
      if (type.GetConstructor(Type.EmptyTypes) == null) {
        Logger?.Warning(
          "[exlib] {0} implements IExDefinitionContributor but has no parameterless constructor; skipped.",
          type.FullName
        );
        continue;
      }
      if (!_contributors.Contains(type))
        _contributors.Add(type);
    }
  }

  /// <summary>Instantiates and runs every discovered <see cref="IExDefinitionContributor"/>, in
  /// discovery order, each isolated: a throw is logged and the rest still run.</summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static void RunContributors(ICoreAPI api) {
    int ran = 0;
    foreach (Type type in _contributors) {
      try {
        ((IExDefinitionContributor)Activator.CreateInstance(type)!).Contribute(
          api
        );
        ran++;
      } catch (Exception e) {
        api.Logger.Error(
          "[exlib] definition contributor {0} threw; skipped.",
          type.FullName
        );
        api.Logger.Error(e);
      }
    }
    if (ran > 0)
      api.Logger.Notification(
        "[exlib] Ran {0} definition contributor(s).",
        ran
      );
  }

  // The provider interface is passed as a Type, not a type argument: it carries a `static
  // abstract` member (CS8920).
  private static int Discover<TDef>(
    string domain,
    Assembly asm,
    Type providerInterface,
    Action<TDef> register
  )
    where TDef : IExDef {
    int count = 0;
    foreach (Type type in ReflectionScan.GetCandidateTypes(asm))
      foreach (TDef def in DefinitionsOf<TDef>(type, domain, providerInterface)) {
        register(def);
        count++;
      }
    return count;
  }

  /// <summary>The code-first block defs a <paramref name="type"/> declares itself, or empty when
  /// it declares none.</summary>
  public static IEnumerable<ExBlockDef> DefinitionsOf(
    Type type,
    string domain
  ) => DefinitionsOf<ExBlockDef>(type, domain, typeof(IExBlockDefProvider));

  /// <summary>The item-side sibling of <see cref="DefinitionsOf"/> (via <see cref="IExItemDefProvider"/>).</summary>
  public static IEnumerable<ExItemDef> ItemDefinitionsOf(
    Type type,
    string domain
  ) => DefinitionsOf<ExItemDef>(type, domain, typeof(IExItemDefProvider));

  /// <summary>The recipe-side sibling of <see cref="DefinitionsOf"/> (via <see cref="IExRecipeDefProvider"/>).</summary>
  public static IEnumerable<ExRecipeDef> RecipeDefinitionsOf(
    Type type,
    string domain
  ) => DefinitionsOf<ExRecipeDef>(type, domain, typeof(IExRecipeDefProvider));

  // The defs a type declares itself via its provider interface's static `Definitions(string)` factory.
  // DeclaredOnly: a derived class must not return its base's inherited defs.
  private static IEnumerable<TDef> DefinitionsOf<TDef>(
    Type type,
    string domain,
    Type providerInterface
  ) {
    if (!providerInterface.IsAssignableFrom(type))
      return [];

    MethodInfo? define = type.GetMethod(
      "Definitions",
      BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
      binder: null,
      types: [typeof(string)],
      modifiers: null
    );
    string effective =
      type.GetCustomAttribute<ExDefDomainAttribute>()?.Domain ?? domain;
    return define?.Invoke(null, [effective]) as IEnumerable<TDef> ?? [];
  }

  /// <summary>Serializes every registered block definition to the synthetic assets the loader
  /// consumes. Side-effect-free: the caller performs the <c>AssetManager.Add</c>.</summary>
  public static IEnumerable<(
    AssetLocation location,
    IAsset asset
  )> BuildBlockAssets(IAssetOrigin origin) =>
    BuildAssets(_blocks.Values, origin);

  /// <summary>Item-side sibling of <see cref="BuildBlockAssets"/>: one
  /// <c>{domain}:itemtypes/{code}.json</c> synthetic asset per registered item def.</summary>
  public static IEnumerable<(
    AssetLocation location,
    IAsset asset
  )> BuildItemAssets(IAssetOrigin origin) => BuildAssets(_items.Values, origin);

  /// <summary>Recipe-side sibling of <see cref="BuildBlockAssets"/>: one
  /// <c>{domain}:recipes/{category}/{name}.json</c> synthetic asset per registered recipe file.</summary>
  public static IEnumerable<(
    AssetLocation location,
    IAsset asset
  )> BuildRecipeAssets(IAssetOrigin origin) =>
    BuildAssets(_recipes.Values, origin);

  // Serializes each def to a synthetic asset at its own Location.
  private static IEnumerable<(
    AssetLocation location,
    IAsset asset
  )> BuildAssets(IEnumerable<IExDef> defs, IAssetOrigin origin) {
    foreach (IExDef def in defs) {
      // Parameterless ToString(): the Formatting overload is not exposed at runtime by the
      // game's bundled Newtonsoft build.
      byte[] bytes = Encoding.UTF8.GetBytes(def.ToJson().ToString());
      yield return (
        def.Location,
        ExSyntheticAsset.Create(def.Location, bytes, origin)
      );
    }
  }
}
