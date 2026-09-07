# Source Generators

`ExpandedLib.Generators` is a Roslyn source-generator project (netstandard2.0) that removes two
kinds of boilerplate at compile time: typed accessors for config classes, and typed lang keys. Both
run automatically on build - there is nothing to invoke.

The project sets `IncludeBuildOutput=false` and is consumed as an **analyzer**, so it ships no
runtime assembly. Outside this repository it comes in the `exlib-testing` bundle under
`analyzers/`; wire it with `<Analyzer Include="analyzers/ExpandedLib.Generators.dll" />`.

## `ExConfigGenerator` - config accessors

**Triggers on:** a class tagged `[ExConfigRegister(fileName, modId)]` (see [Config System](Config-System)).

**Emits:** a static partial class (default name = your type name with `Config` -> `Values`, override
with `AccessorName`) containing:

- `public const string ConfigFileName` - the file name you passed.
- A private `ExConfigRegister<T>` backing store, with `LegacyFileNames` initialised and a static
  `Migrations` member forwarded if your config type declares one.
- `public static void Load(ICoreAPI api)` - calls the store's `Load`; if `Manageable = true`, also
  registers with `ExConfigProfiles`.
- `public static void Edit(Action<T> mutate)` and `public static void Save()`.
- One `public static` read-only getter per public, non-static, readable property (except
  `ConfigVersion`), forwarding to the live config.

So this:

```csharp
[ExConfigRegister("ex_values.json", "iiex", LegacyFileNames = ["lpex_values.json"], Manageable = true)]
public class IiexConfig : IExVersionedConfig
{
    public string? ConfigVersion { get; set; }
    public static readonly ExConfigMigration[] Migrations = [ /* ... */ ];
    public float PumpWaterPerSecond { get; set; } = 16.67f;
}
```

generates roughly:

```csharp
public static partial class IiexValues
{
    public const string ConfigFileName = "ex_values.json";
    private static readonly ExConfigRegister<IiexConfig> _store =
        new(ConfigFileName, "iiex", IiexConfig.Migrations) { LegacyFileNames = ["lpex_values.json"] };
    private static IiexConfig _config => _store.Config;

    public static void Load(ICoreAPI api) { _store.Load(api); ExConfigProfiles.Register(_store); }
    public static void Edit(Action<IiexConfig> mutate) { mutate(_store.Config); _store.Save(); }
    public static void Save() => _store.Save();

    public static float PumpWaterPerSecond => _config.PumpWaterPerSecond;
}
```

## `ExLangKeyGenerator` - typed lang keys

**Triggers on:** an `assets/{domain}/lang/en.json` supplied to the compiler as an `AdditionalFiles`
item. There is no attribute and nothing to tag - the file is the input.

**Emits:** a `{Domain}Lang` class of `public const string` members, one per key in that file, so a
mistyped key is a compile error instead of a raw key rendered in the player's UI. English is the
source of truth; the other locales are never read by the generator.

A bare key `k` emits the value `"{domain}:{k}"`. A key that is already domain-qualified - a vanilla
override such as `"game:placefailure-..."` - emits verbatim, so an override still resolves to the
domain it overrides.

The class is emitted into the consuming project's `RootNamespace`, so it resolves from any file in
that project without a `using`.

**The consuming csproj must feed it**, which is the step most easily missed - with no
`AdditionalFiles` the generator runs, finds nothing and emits nothing, and the only symptom is that
`{Domain}Lang` does not exist. Setting `<AssetDomain>` (see [Getting Started](Getting-Started)) does
this for you: the `ExpandedLib` package's own `build/ExpandedLib.targets` carries

```xml
<AdditionalFiles Include="$(ModAssetsRoot)\$(AssetDomain)\lang\en.json" />
```

conditioned on `$(AssetDomain)` being set, so a mod project declares its domain and nothing else.
Only the **primary** domain is fed: a mod packing a second tree (an absorbed mod's assets, or a
`game:` override) should not emit typed constants for someone else's keys.

## Notes

- Generated code is re-emitted every build, so adding a config property or a lang key is instant -
  no boilerplate to duplicate or keep in sync.
- The generator targets `netstandard2.0` (a Roslyn requirement); if you fork it, mind the usual
  netstandard2.0 source-generator constraints (no newer BCL APIs).

## Related pages

- [Config System](Config-System) - the runtime side of `[ExConfigRegister]`.
- [Registries](Registries) - `[BlockRegister]` / `[ItemRegister]` registration.
- [Testing Harness](Testing-Harness) - the `exlib-testing` bundle these ship in.

## Diagnostics

| Id | Severity | Generator | Reported when |
|----|----------|-----------|----------------|
| `EXLIB0001` | Error | `ExConfigGenerator` | A class carries `[ExRecipeProfile]` with no companion `[ExConfigRegister]`, so no accessor is generated at all. |
| `EXLIB0002` | Error | `ExConfigGenerator` | A class's `[ExRecipeProfile]` shape is invalid (missing catalogue property, missing `DefaultCatalogue`, missing or unregistered level property). Reported at the attribute's location alongside the `#error` the generated accessor still carries - the `#error` is what stops the build; this diagnostic only gives an IDE somewhere to navigate to. |
| `EXLIB0003` | Warning | `ExLangKeyGenerator` | The consuming project sets `$(AssetDomain)` but no matching `assets/{domain}/lang/en.json` reached the compiler as an `AdditionalFiles` item, so `{Domain}Lang` is not generated. Not reported for a project with no `$(AssetDomain)` at all. |
| `EXLIB0004` | Warning | `ExLangKeyGenerator` | Two lang keys sanitise to the same member name; the one that sorts later gets a numeric suffix instead of the plain name. |
