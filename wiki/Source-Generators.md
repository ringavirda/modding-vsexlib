# Source Generators

A source generator is a compiler plugin. It reads your code as the project compiles, writes more
C#, and hands it back to the same compilation. You never see those files and never edit them; the
generated members are simply there, with IntelliSense, as though you had typed them.
`ExpandedLib.Generators` carries two of them, and both run on every build with nothing to invoke.

They exist because two chores in a mod are pure repetition, and both fail quietly. A config class is
plain data: to read a value at runtime you need a loaded instance of it, a file name, and the load
and save plumbing around it, and every property you add needs a second line somewhere to expose it.
Text is worse. Every string the player sees is a key into `assets/{domain}/lang/en.json`, and the
game's translation call takes that key as a raw string, so a misspelled key is not a compile error,
not a crash, and usually not even a log line - the player just reads `yourmod:blockdesc-machine`
where a sentence should be.

Both generators turn that repetition into symbols the compiler checks. A config class tagged
`[ExConfigRegister]` gets a static accessor class, so a tunable is read as
`IiexValues.PumpWaterPerSecond` and adding one is a single property. A lang file gets a
`{Domain}Lang` class of constants, so a key that does not exist stops the build. In your own mod you
set `<AssetDomain>` in the csproj and tag your config class; that is the whole of it.

The project targets netstandard2.0, sets `IncludeBuildOutput=false` and is consumed as an
**analyzer**, so it ships no runtime assembly and nothing of it lands in your mod folder. The
`ExpandedLib` package carries the dll under `analyzers/dotnet/cs/`, where the compiler finds it
without being told: reference the package as [Installing](Installing) describes and the generators
are already running. Taking the `exlib-testing` bundle from a release instead puts the dll under
`analyzers/`, and you wire it yourself with
`<Analyzer Include="analyzers/ExpandedLib.Generators.dll" />`.

## `ExConfigGenerator` - config accessors

**Triggers on:** a class tagged `[ExConfigRegister(fileName, modId)]` (see [Config System](Config-System)).

**Emits:** a static partial class named after your config type, with a trailing `Config` swapped
for `Values` (`IiexConfig` gives `IiexValues`) unless you pass `AccessorName`. It carries:

- `public const string ConfigFileName` - the file name you passed, so a command or a test can name
  the file without repeating the literal.
- A private `ExConfigRegister<T>` backing store holding the live config, with `LegacyFileNames` and
  `LegacySectionIds` initialised from the attribute and a static `Migrations` member forwarded if
  your config type declares one. The store owns the reading and writing; the accessor is its face.
- `public static void Load(ICoreAPI api)` - call it once during mod startup. It calls the store's
  `Load`; if `Manageable = true`, it also registers with `ExConfigProfiles`, which is what puts your
  values in `/exmod config` for an admin to read and change on a running server. A config class that
  also carries `[ExRecipeProfile]` registers its cost catalogue with the shared recipe-cost
  framework in the same call (see [Recipe Costs](Recipe-Costs)).
- `public static void Edit(Action<T> mutate)` and `public static void Save()`, for changing a value
  at runtime and flushing it to disk. Config is host-authoritative, so both are server-side in
  practice.
- One `public static` read-only getter per public, non-static, readable property (except
  `ConfigVersion`), forwarding to the live config. Your gameplay code reads these and never holds
  the config object itself, so a reload or a live edit is visible on the next read.

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
source of truth; the other locales are never read by the generator. A member's name is its key with
the separators dropped and each segment capitalised, so `blockdesc-machine` becomes
`BlockdescMachine`. Two keys that reduce to the same member name do not silently overwrite each
other: the one that sorts later takes a numeric suffix and the build warns (`EXLIB0004` below).

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
- The netstandard2.0 target is a Roslyn requirement, not a choice; if you fork the project, mind
  the usual source-generator constraints that come with it (no newer BCL APIs).

## Related pages

- [Config System](Config-System) - the runtime side of `[ExConfigRegister]`.
- [Registries](Registries) - `[BlockRegister]` / `[ItemRegister]` registration.
- [Testing Harness](Testing-Harness) - the `exlib-testing` bundle these ship in.

## Diagnostics

Both generators report through the compiler, so these arrive as ordinary build errors and warnings.
The errors stop the build; the warnings mean a symbol you expected is missing, or is there under a
name you did not choose.

| Id | Severity | Generator | Reported when |
|----|----------|-----------|----------------|
| `EXLIB0001` | Error | `ExConfigGenerator` | A class carries `[ExRecipeProfile]` with no companion `[ExConfigRegister]`, so no accessor is generated at all. |
| `EXLIB0002` | Error | `ExConfigGenerator` | A class's `[ExRecipeProfile]` shape is invalid (missing catalogue property, missing `DefaultCatalogue`, missing or unregistered level property). Reported at the attribute's location alongside the `#error` the generated accessor still carries - the `#error` is what stops the build; this diagnostic only gives an IDE somewhere to navigate to. |
| `EXLIB0003` | Warning | `ExLangKeyGenerator` | The consuming project sets `$(AssetDomain)` but `assets/{domain}/lang/en.json` did not yield a parsed lang file - missing from the `AdditionalFiles` items, or present but malformed - so `{Domain}Lang` is not generated. Not reported for a project with no `$(AssetDomain)` at all. |
| `EXLIB0004` | Warning | `ExLangKeyGenerator` | Two lang keys sanitise to the same member name; the one that sorts later gets a numeric suffix instead of the plain name. |
