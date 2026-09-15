# Config System

Every mod grows numbers a player wants to change: how fast a pump moves water, how much a pipe
holds, how long a heat soak takes. The game gives you a `ModConfig` folder and stops there. You
write the JSON reading and writing, you decide what a missing file means, you decide what happens
when someone types `-1` where you expected a fraction, and if you want a value editable without a
restart you write that command too. Then you fix a bad default in the next release and nobody sees
it, because every existing player's file already carries the old number.

exlib does that work once. You write a plain class with one property per tunable - a POCO, nothing
but auto-properties and the values you ship as their defaults - and tag it. A source generator
(code the compiler writes while it builds your mod, see [Source Generators](Source-Generators))
reads the tag and emits a static accessor class beside it: one getter per property, `Load`, `Save`
and `Edit`, range checks, migrations that reset a field when you change its default, folding of a
file you renamed, and live editing from chat if you ask for it.

In your mod it is two steps. Write the class, and let `Load` run once at startup. After that every
read is `YourValues.SomeProperty`, from anywhere, on either side.

## Where the values actually live

Read this section before you pick a file name: it settles the two arguments you are about to write
into the tag. `ModConfig` is the game's folder for mod settings, and exlib does not put one file
per mod in it. A config file is **shared and mod-sectioned**: one JSON document whose top-level
keys are mod ids, each holding that mod's whole config object.

```json
{
  "exlib": { "ConfigVersion": "0.7.2", "LitresPerPipe": 30.0 },
  "iiex":  { "ConfigVersion": "0.6.9", "PumpWaterPerSecond": 16.67 },
  "yourmod": { "ConfigVersion": "1.0.0", "YourValue": 5.0 }
}
```

Each store reads and writes only its own section, so several mods share one file without seeing each
other's keys, and each carries its own independent `ConfigVersion` and migration history. The shipped
mods use two documents: `ex_values.json` for gameplay tunables and `ex_recipes.json` for recipe-cost
levels.

Three consequences follow from that:

- **Your section key is your mod id.** Naming a file another mod already uses is legal and simply adds
  a section to it.
- **Two configs owned by the same mod id cannot share a file.** The second one to bind overwrites the
  first's section. Give them different file names.
- **`Save()` rewrites the whole document**, from the in-memory copy that every mod's section binds
  into. That is safe by design - mod load is single-threaded, and between load and flush the
  in-memory document is the authority - but it is why a whole-file parse failure is expensive: the
  unreadable file is set aside as `<name>.corrupt` and *every* mod in it starts from coded defaults,
  where a single unreadable section costs only its own mod.

## Declaring a config

A config type is a plain class with one auto-property per tunable, each carrying the value you
ship. It implements `IExVersionedConfig` and tags itself `[ExConfigRegister]`:

```csharp
[ExConfigRegister(
    "ex_values.json",                   // the shared document under ModConfig/
    "iiex",                             // owning mod id - and this config's section key
    LegacyFileNames = new string[] { "iwex_values.json", "lpex_values.json" },
    Manageable = true                   // expose to /exmod config
)]
public class IiexConfig : IExVersionedConfig
{
    public string? ConfigVersion { get; set; }      // managed for you; stamps the writing mod version

    public static readonly ExConfigMigration[] Migrations =
    [
        new() { ToVersion = "0.6.0", ResetFields = [nameof(PumpWaterPerSecond)] },
    ];

    [ExConfigRange(0, 1)]
    public float BoilerWaterIntakeFillFraction { get; set; } = 0.5f;

    public float PumpWaterPerSecond { get; set; } = 16.67f;
    public string RecipeLevel { get; set; } = "normal";
}
```

The tag says four things. The first argument is the document under `ModConfig` your section goes
in: `ex_values.json` to sit beside the family mods, or a name of your own. The second is the
section key, which is your mod id. `LegacyFileNames` names files an older version of your mod
wrote, so upgrading players keep their settings, and `Manageable` opts the config into the
`/exmod config` command; both have their own section below, as do the `Migrations` array and
`[ExConfigRange]`.

`IExVersionedConfig` asks for one property in return. The store stamps it with the version of the
mod that last wrote the section, and reads it back on the next load to decide which migrations
still have to run:

```csharp
public interface IExVersionedConfig
{
    string? ConfigVersion { get; set; }   // null on first run; set to the mod version that last wrote the section
}
```

## Using the generated accessor

You do not read the config object itself. The generator emits a static class beside it -
`IiexValues` for `IiexConfig`, the type name with a trailing `Config` replaced by `Values`,
overridable with `AccessorName` on the tag - and every read goes through that. Being static, it
needs nothing handed to it, so a block, a block entity or a renderer reads a value wherever it
happens to be:

```csharp
public static partial class IiexValues
{
    public const string ConfigFileName = "ex_values.json";

    public static void Load(ICoreAPI api);            // load + migrate + sanitize (server also writes back)
    public static void Save();                        // persist live config
    public static void Edit(Action<IiexConfig> mutate);   // mutate + save

    public static float  BoilerWaterIntakeFillFraction { get; }   // one read-only getter per config property
    public static float  PumpWaterPerSecond { get; }
    public static string RecipeLevel { get; }
    // ...
}
```

```csharp
public override void Start(ICoreAPI api) => IiexValues.Load(api);   // call once at startup

// Read anywhere:
float fraction = IiexValues.BoilerWaterIntakeFillFraction;

// Change + persist (typically server-side admin):
IiexValues.Edit(c => c.RecipeLevel = "cheap");
```

Call `Load` once, before anything reads a value. A mod system derived from `ExModSystem` already
does: its `Start` calls `ExConfig.LoadAll`, which finds every generated accessor in your assembly
and loads it. Write the call yourself only when your entry point is a plain `ModSystem`, the class
the game instantiates to start your mod. `Load` runs on both sides and each side reads its own
copy: it folds any legacy file in, applies migrations, resets invalid values and stamps the running
mod version.

> **Only the server writes the file back.** In singleplayer both sides load the same store in one
> process against one file and would race over it, so the client migrates, sanitizes and stamps
> **in memory only**. The server's copy is the authority. `Save()` before `Load()` is likewise a
> silent no-op, because the store has no API handle yet - an `Edit()` that early mutates memory and
> persists nothing.

One nuance of the emitted surface: a getter is generated for every public, readable, non-static
property except `ConfigVersion`, **including get-only ones**. Validation and `/exmod config` both
require a setter, so a computed get-only property is readable through the accessor yet invisible to
both.

## Range validation

A hand-edited file eventually carries a nonsense number, and a config that trusts the file turns
that into a divide by zero or a machine that quietly does nothing, a long way from the line that
caused it. Declare the range your code can actually handle and the store enforces it:

```csharp
[ExConfigRange(0, 1)]      // bounded
public float Fraction { get; set; } = 0.5f;

[ExConfigRange(1)]         // floor only; max = +infinity
public float Capacity { get; set; } = 30f;
```

`ExConfigRange(double min[, double max])` is enforced both on live edits (rejected if out of
bounds) and on load. Numeric properties **without** the attribute default to a non-negative, finite
range `[0, +inf)`.

> **On load an invalid value is reset, not clamped.** A file carrying `2000000` under
> `[ExConfigRange(1, 1_000_000)]` comes back as the *coded default*, not as `1000000`. NaN and
> infinity are treated the same way, and so is a reference-typed value nulled out in the file whose
> coded default is non-null - a nulled string or collection would otherwise NRE its reader. Every
> reset is named in a warning log line.

## Version-reset migrations

Once a player's file exists it wins over your code, so changing a default in a new release reaches
nobody who has already played. That is right for a value the player deliberately tuned, and wrong
for one you shipped badly and fixed. A migration marks the second case. On load, when the section's
stamped version is below a migration's `ToVersion` and the running mod is at or past it, the named
fields go back to their coded defaults; everything else the player tuned is left alone.

```csharp
public sealed class ExConfigMigration
{
    public required string ToVersion { get; init; }   // reset fires when first loading at/above this version
    public string? FromVersion { get; init; }         // optional lower bound; null = any older version
    public string[]? ResetFields { get; init; }       // fields to reset; null/empty = reset all
}
```

```csharp
public static readonly ExConfigMigration[] Migrations =
[
    new() { ToVersion = "0.6.0", ResetFields = [nameof(IiexConfig.PumpWaterPerSecond)] },
];
```

The generator forwards a static `Migrations` member on your config type into the store
automatically - it must be static, named exactly `Migrations`, and be a field or a property.

## Legacy file names

`LegacyFileNames` carries player configs across a rename **or** across the move from a per-mod file
into the shared document. On load, if this mod's **section** is absent and one of the legacy files
still exists under `ModConfig`, that file's contents become the section and the old file is renamed
to `<name>.migrated` rather than deleted, so the carry-over stays reversible. First existing name
wins, and the fold never re-runs once the section exists.

That is how the shipped mods moved: `ppex.json` became `lpex_values.json` became the `iiex` section
of `ex_values.json`, and a player upgrading across either step keeps their settings.

## Live editing: `Manageable`

Stopping a server to change one number is a poor way to tune a mod, and asking every mod author to
write an edit command is a poor way to fix that. Set `Manageable = true` and the generated `Load`
registers the store with `ExConfigProfiles`, the process-wide list the shared
[command](Commands) reads. Your values become editable from chat, with no command code of your
own:

```
/exmod config                       # list manageable mods
/exmod config iiex                  # list iiex's editable values
/exmod config iiex PumpWaterPerSecond    # show current value
/exmod config iiex PumpWaterPerSecond 20 # set it (immediate, no reload), validated + persisted
```

The command knows nothing about your config type. It works through one non-generic view that every
store implements, which is what lets one command serve every mod:

```csharp
public interface IExConfigAccess
{
    string ModId { get; }
    string FileName { get; }
    IReadOnlyList<string> ValueNames { get; }
    bool TryGet(string name, out string canonicalName, out string value);
    ExConfigEditResult Set(string name, string raw);
    string ExportJson();
    void ImportJson(string json);
}

public enum ExConfigEditStatus { Ok, UnknownValue, ParseFailed, OutOfRange }

public sealed class ExConfigEditResult
{
    public required ExConfigEditStatus Status { get; init; }
    public string Name { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Expected { get; init; }    // e.g. "number", "true/false"
    public string? Range { get; init; }       // e.g. "0..1"
}

public static class ExConfigProfiles
{
    public static void Register(IExConfigAccess config);
    public static bool TryGet(string code, out IExConfigAccess config);
    public static IReadOnlyCollection<string> Codes { get; }
}
```

Only simple-typed values are surfaced for editing: `string`, `bool`, `int`, `long`, `float` and
`double`, and only when the property has a setter.

## What the client sees

Each side loads its own `ex_values.json` (or whatever file your config uses) independently -
nothing crosses the wire on its own. That means a client's display, handbook and predictions
normally read *its own* local tuning, not the host's, whenever it differs. `ExConfigSyncModSystem`
closes that gap for every `Manageable` config, at three rungs:

- **Nothing to do.** On join, the server sends every registered section (see `ExConfigProfiles`) to
  the connecting client, which imports each one into its matching store. A `Manageable` config gets
  this without any code of its own.
- **`/exmod config set` reaches players.** After a successful edit the command broadcasts the
  changed section to everyone connected, so a live tweak takes effect without a reconnect - the same
  as it already did for the host itself.
- **`ImportJson` directly**, for a mod that carries its own transport (a config that is not
  `Manageable`, or a sync path that needs different timing than join/edit): call
  `IExConfigAccess.ExportJson()` on the sending side and `ImportJson(json)` on the receiving one.

An import never touches the client's own config file - it replaces only the live, in-memory values
every reader goes through the accessor's getters to see, the same way `Edit` does, minus the write.
In singleplayer both sides already share one process and one store, so the import is a same-values
no-op.

## The underlying store (if you skip the generator)

The generated accessor is a shell over `ExConfigRegister<TConfig>`, the store that does the work.
Hold one yourself if you would rather not generate the accessor:

```csharp
public sealed class ExConfigRegister<TConfig> : IExConfigAccess
    where TConfig : class, IExVersionedConfig, new()
{
    public TConfig Config { get; private set; }      // never null; holds coded defaults before Load
    public string ModId { get; }
    public string FileName { get; }
    public IReadOnlyList<string> LegacyFileNames { get; init; }

    public ExConfigRegister(string fileName, string modId, params ExConfigMigration[] migrations);

    public void Load(ICoreAPI api);
    public void Save();

    // the IExConfigAccess view the /exmod config command drives
    public IReadOnlyList<string> ValueNames { get; }
    public bool TryGet(string name, out string canonicalName, out string value);
    public ExConfigEditResult Set(string name, string raw);

    // the IExConfigAccess view ExConfigSyncModSystem drives
    public string ExportJson();
    public void ImportJson(string json);
}
```

Three things the generator does for you that you then own:

- **There is no `Edit` on the store.** Mutate `Config` and call `Save()` yourself.
- **`Manageable` is an attribute flag only the generator honours.** A hand-rolled store must call
  `ExConfigProfiles.Register` itself or it never appears in `/exmod config`.
- **`LegacyFileNames` is init-only and not a constructor parameter**, so set it through an object
  initializer.

The generator path is recommended - adding a property is then a one-line change with the getter,
validation and command wiring all emitted for you.

## Related pages

- [Source Generators](Source-Generators) - exactly what `[ExConfigRegister]` emits.
- [Commands](Commands) - the `/exmod config` sub-command.
- [Recipe Costs](Recipe-Costs) - a `RecipeLevel` string in config drives the recipe profile.
