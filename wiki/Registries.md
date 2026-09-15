# Registries

Vintage Story builds a mod's content from JSON. A file under `blocktypes/` describes a block, and a
field in that file names the C# class the game should construct for it. The name is a string, and
the engine can only match it to your class because your mod called `api.RegisterBlockClass` for
that class during startup. Items, block entities (the object that holds a placed block's own saved
state and ticks with it), behaviours and entity classes each have a register call of their own.

Those calls live in your mod system - the class the game constructs for your mod and calls at each
phase of startup - and you maintain them by hand. Every class you add needs another line, carrying a
string your JSON has to spell the same way. The compiler checks neither half: it sees a string
literal, and the JSON never meets the C# until the game loads a world. A class you forgot is simply
not registered, and a class you renamed keeps whatever string the old line held.

exlib replaces each list with an attribute. Tag a class `[BlockRegister]` and one reflection scan of
your assembly finds it and registers it under `{yourmodid}.{ClassName}` - a key derived from the
class, not typed a second time. Chat commands and per-player client preferences are found the same
way, from attributes of their own. There are four registry families - entities, commands,
preferences and [config](Config-System) - plus [recipe profiles](Recipe-Costs); this page covers the
first three. [Code-First Definitions](Code-First-Definitions) takes care of the other half, the
hand-written JSON.

You do not run the scans yourself. Derive `ExModSystem` and each one runs in the phase it belongs
to, in an order that already accounts for what depends on what.

## ExModSystem: the zero-line rung

The game constructs each mod system once and calls it at a fixed series of points, its phases:
`Start` on both sides, the asset phases as content loads, then `StartServerSide` or
`StartClientSide` for whichever side this process is. Which phase a registration belongs in is not
a matter of taste. A block class has to exist before assets are read; a client command that names a
preference has to be built after that preference is registered.

Derive `ExModSystem` instead of `ModSystem` and you write none of that - it runs every registry
below for you, each in its phase:

```csharp
public class YourModSystem : ExModSystem { }
```

`Start` loads your `[ExConfigRegister]` config, registers your attribute-marked classes with
`EntityRegistry.RegisterAll` and your content guards with `ExCheckRegistry.RegisterAll`.
`StartServerSide` runs `CommandRegistry.RegisterAll`. `StartClientSide` runs
`PreferenceRegistry.RegisterAll` and then `CommandRegistry.RegisterAll`, preferences first so that
a command naming one finds it already registered. Each side builds its own commands, so command
registration runs once per side, not once for the whole mod.

For your own work, override `OnStartPre`, `OnStart`, `OnAssetsLoaded`, `OnStartServerSide`,
`OnStartClientSide` or `OnAssetsFinalize`. Each is empty by default and runs after the registration
its phase does, so anything you write there can already look up what was registered. Setting
`PatchHarmony => true` folds `ExHarmony.PatchOnce` and `UnpatchAll` in as well.

The rest of this page documents the calls `ExModSystem` makes for you. You need them when your mod
system wants a different order (see [Ordering rules](Lifecycle) - a preference read before a command
that names it, say), or when the code doing the registering is not a `ModSystem` at all.

## Entity registration

"Entity" here is the engine's own word for anything it constructs from a registry key, not just the
creatures that walk around: blocks, items, block entities, behaviours of five kinds and entity
classes all go through `Registries/Entities/` in one scan. Every attribute it looks for inherits one
base:

```csharp
public abstract class RegisterAttribute(string? code = null) : Attribute
{
    public string? Code { get; }              // override the registry key; default {modid}.{ClassName}
    public bool PrefixModId { get; init; } = true;   // false -> register under a bare key (replace vanilla)
}
```

`Code` replaces the class name in the key, for when your JSON already names something the class is
not called. `PrefixModId = false` drops your mod id and registers under the bare name instead, which
is how you take over a key the game itself uses; leave it alone unless replacing vanilla is the
point, because a bare key carries no mod id and so nothing separates your claim on that name from
another mod's.

Nine sealed attributes inherit that base, one per registry the engine keeps. Each one checks that
the class it sits on really derives from the type that registry expects, and when it does not, logs
a warning naming the class and skips it rather than handing the engine something it cannot
construct:

| Attribute | Target base type |
| --- | --- |
| `[BlockRegister]` | `Block` |
| `[ItemRegister]` | `Item` |
| `[BlockEntityRegister]` | `BlockEntity` |
| `[BlockBehaviorRegister]` | `BlockBehavior` |
| `[BlockEntityBehaviorRegister]` | `BlockEntityBehavior` |
| `[CollectibleBehaviorRegister]` | `CollectibleBehavior` |
| `[EntityRegister]` | `Entity` |
| `[EntityBehaviorRegister]` | `EntityBehavior` |
| `[CropBehaviorRegister]` | `CropBehavior` |

The common case is the whole of it: tag the class, and the key follows the class name.

```csharp
[BlockRegister]                         // -> "yourmod.BlockPipe"
public class BlockPipe : BlockNetworkNode { }
```

The two properties, and the aliases a block entity gets for free, come out like this:

| Variant | Registers as |
| --- | --- |
| `[BlockRegister("pipeStraight")]` | `"yourmod.pipeStraight"` |
| `[BlockRegister("MultiblockStructure", PrefixModId = false)]` | `"MultiblockStructure"` (replaces vanilla) |
| `[BlockEntityRegister]` on `BlockEntityPipe` | `"yourmod.BlockEntityPipe"` + aliases `"yourmod.Pipe"`, `"Pipe"`, `"pipe"` |

A class named `BlockEntityXxx` also registers the short-name aliases `{modid}.{Xxx}`, `{Xxx}` and
`{xxx}`, unless you set an explicit `Code`. That is so a blocktype's `entityClass` field can name
your block entity the short way vanilla blocktypes do, instead of spelling out the class name. The
bare `{Xxx}`/`{xxx}` pair carries no domain, so when a second mod's class claims a short name
already issued, exlib logs an error naming both types rather than letting the second silently
overwrite the first's registration.

One call registers the lot, from `Start`. It scans the assembly you hand it; omit the argument and
it scans the caller's own, which is what you want until your mod ships more than one:

```csharp
public static class EntityRegistry
{
    public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null);   // default asm = caller's
}
```

```csharp
public override void Start(ICoreAPI api)
    => EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);
```

The same scan also picks up code-first definition providers in the assembly - any
`IExBlockDefProvider`, `IExItemDefProvider` or `IExRecipeDefProvider` - so blocks, items and recipes
declared in C# register through this one call too.

The scan survives an assembly that will not fully load. `ReflectionScan.GetCandidateTypes` keeps
whatever types a `ReflectionTypeLoadException` did yield, so one class whose dependency is missing
costs you that class and not every other registration in the mod.

Two registrations stay outside the attributes. Entity renderers are `ICoreClientAPI`-only, so a
client-side-only registration does not fit a rung that runs identically on both sides; register
them with the raw client-side call instead.

Mountables are left out for a different reason: `RegisterMountable` takes a delegate matching
`ICoreAPI.GetMountableDelegate`'s signature (`(IWorldAccessor, TreeAttribute) -> IMountableSeat`),
not a class, and both this rung's scan and `Validate<TBase>` are type-based - a method-target
attribute would need a second code path this rung doesn't have. Register mountables with the raw
`api.RegisterMountable(...)` call instead.

## Command registration

A chat command is what a player types into the chat box, and the game builds one from a fluent
builder at startup. Two things about that are awkward. The builder call can go anywhere, so a mod
with a dozen commands ends up with a dozen calls in one start method and no sign of which side each
one belongs on. And every mod that adds commands adds its own root, so a player who runs five of
them learns five.

`Registries/Commands/` answers both. A top-level command implements `IExCommand`; a sub-command
that hangs off an existing command implements `IExSubCommand` and names the parent it attaches to,
which is how every Expanded mod puts its commands under the one `/exmod` root. The `Side` on each
attribute says where the command belongs - `Server` for one that reads or changes world state,
`Client` for one that only touches the local player's display, `Universal` for one that is written
to run either side - and the registry skips any class whose side is not the side it is running on.

```csharp
public interface IExCommand
{
    void Register(ICoreAPI api, Mod mod);                        // build via api.ChatCommands.Create(...)
}

public interface IExSubCommand
{
    string ParentName { get; }                                  // existing command to attach to, e.g. "exmod"
    void Register(ICoreAPI api, Mod mod, IChatCommand parent);  // build via parent.BeginSubCommand(...)
}
```

A sub-command is the usual shape. This one puts `/exmod status` on the server, taking its
description from a lang key so it translates like anything else:

```csharp
[SubCommandRegister(Side = EnumAppSide.Server)]
public sealed class StatusSubCommand : IExSubCommand
{
    public string ParentName => "exmod";

    public void Register(ICoreAPI api, Mod mod, IChatCommand parent)
    {
        parent.BeginSubCommand("status")
              .WithDescription(Lang.Get(mod.Info.ModID + ":command-status-desc"))
              .HandleWith(args => /* ... */)
              .EndSubCommand();
    }
}
```

Register them from `Start` (or the side-specific start methods):

```csharp
public static class CommandRegistry
{
    public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null);
}
```

The registry resolves each sub-command's parent via `api.ChatCommands.GetOrCreate`, creating it if
nobody has yet, so several mods can hang sub-commands off the shared `exmod` root without caring
which of them loads first. See [Commands](Commands) for the `/exmod` and `.exmod` root exlib
provides and the sub-commands it already carries.

## Preferences

A display preference is a choice that belongs to one player and not to the world: whether a
machine's readout reads in metric or imperial changes nothing anyone else can see, so it has no
business in world state or in a server config. The game offers nowhere to put such a thing, which
is why every mod that wants a toggle ends up with a config file of its own.

`Registries/Preferences/` is that place - one client-side store, shared by every Expanded mod,
saved per player. Implement `IExPreference` and tag it `[PreferenceRegister]`:

```csharp
public interface IExPreference
{
    string Key { get; }                       // lower-case, no spaces: config key + sub-command name + lang stem
    IReadOnlyList<string> Options { get; }     // allowed values, lower-case
    string Default { get; }                    // must be in Options
    void Apply(string value);                  // push the stored value into live client state
}
```

`Apply` is the only member with work to do, and you never call it yourself: the store calls it when
the player changes the setting and once for the local player when the world finishes loading, which
is what makes a choice survive a restart. `Options` and `Default` bound what can reach it, so the
value handed to `Apply` is always one of the options and needs no checking. exlib's own unit toggle
is the whole of an implementation:

```csharp
[PreferenceRegister]
public sealed class MeasurePreference : IExPreference
{
    public string Key => "measure";
    public IReadOnlyList<string> Options { get; } = ["metric", "imperial"];
    public string Default => "metric";
    public void Apply(string value) => ExMeasure.System = ExMeasure.Parse(value);
}
```

Register it in `StartClientSide`, before your own `CommandRegistry.RegisterAll`:

```csharp
public override void StartClientSide(ICoreClientAPI api)
{
    PreferenceRegistry.RegisterAll(api, Mod, GetType().Assembly);
    CommandRegistry.RegisterAll(api, Mod, GetType().Assembly);
}
```

That is the only ordering constraint on this page, and it exists because a preference sub-command
resolves its definition once, at registration time, into a local it then holds. A command
registered before the preference it names finds nothing. Written defensively, you get a sub-command
whose name, options and description come from a throwaway fallback the store does not hold; written
as `ExPreferences.Find(key)!`, you get a null-reference exception at world load.

> **You do not load or apply the store yourself.** exlib calls `ExPreferences.LoadConfig` in its own
> `StartClientSide`, and hooks `LevelFinalize` to apply every registered preference for the local
> player - which is exactly why registering yours later in the same phase still works, and why
> `ExPreferences.ApplyForPlayer` is not yours to call: `api.World.Player` is not ready during
> `StartClientSide`. Calling `LoadConfig` again re-reads the file and re-points the store's
> process-global API handle at your own; harmless at that phase, but it claims an ownership you do
> not have.

The shared store persists to `ModConfig/exmod_preferences.json` keyed by player UID:

```csharp
public static class ExPreferences
{
    public const string ConfigFileName = "exmod_preferences.json";

    public static void Register(IExPreference preference);
    public static IExPreference? Find(string key);
    public static void LoadConfig(ICoreAPI api);                                   // exlib calls this
    public static string GetForPlayer(string playerUid, string key);
    public static void SetForPlayer(string playerUid, string key, string value);   // store + apply + persist
    public static void ApplyForPlayer(string playerUid);                           // exlib calls this
    public static IEnumerable<IExPreference> All { get; }
}
```

The on-disk shape is an internal map of player UID to that player's chosen values. The store is
process-global static state shared by every Expanded mod, which is what lets one file and one
`LevelFinalize` hook serve all of them.

Two lookups fail quietly rather than throwing, both on an unregistered key: `GetForPlayer` yields
`string.Empty`, and `SetForPlayer` persists the value but applies nothing. Register the preference
before you read or write it.

The `Key` doubles as the lang-key stem: `"measure"` drives `command-measure-desc`,
`pref-measure-label`, `pref-measure-metric`, etc. Changing the setting in game is a sub-command you
write, one that calls `SetForPlayer`; exlib ships one for its own `measure` preference, and it is
the reason the key has to be a legal command name.

## Recipe registry

Vanilla keeps each kind of recipe - grinding, smithing, barrel - as a recipe type of its own: a
class describing one recipe, a folder of JSON files that fill it, and a list the game holds them in.
You want one of these only when your machine takes recipes of a shape no existing type has. Recipe
*files* for a type that already exists are assets, and need no code here at all.

A mod shipping its own recipe *type* registers a `RecipeRegistryGeneric<T>`, the same rung vanilla's
own recipe kinds use (`api.RegisterRecipeRegistry`), which is what gets a recipe client sync and a
handbook entry for free:

```csharp
public static class ExRecipeRegistry
{
    public static List<T> Register<T>(ICoreAPI api, string code)
        where T : IByteSerializable, new();

    public static void LoadRecipes<T>(ICoreServerAPI sapi, string folder, List<T> into,
        Func<T, bool>? resolve = null) where T : IByteSerializable, new();
}
```

`Register` runs identically on both sides, from `Start` - the `code` must be the same string on
client and server, or the two can never sync the recipes it carries:

```csharp
public override void Start(ICoreAPI api)
    => WidgetRecipes = ExRecipeRegistry.Register<WidgetRecipe>(api, "widgetrecipes");
```

`LoadRecipes` is server-only: it reads every JSON asset under `recipes/{folder}` (one recipe object,
or an array of them) into the list `Register` returned, resolving each against its own asset's
domain. Call it from `AssetsLoaded`, after `Register` has run on both sides:

```csharp
public override void AssetsLoaded(ICoreAPI api)
{
    if (api is ICoreServerAPI sapi)
        ExRecipeRegistry.LoadRecipes(sapi, "widgets", WidgetRecipes, r => {
            r.Resolve(sapi.World);
            return r.Enabled;
        });
}
```

`resolve` runs once per loaded recipe before it is added, and returning `false` drops it - an
ingredient resolve, an `Enabled` check, whatever your recipe type needs done once loaded.

## Shipping more than one assembly

Everything above assumes one dll. Split your mod in two and you meet a rule of the engine's: one dll
may declare as many mod systems as it likes, but a mod folder holding several dlls may have mod
systems in only one of them. The second dll cannot bring a `ModSystem` of its own along to do its
registering.

exlib ships `exlib.dll` and `exlib.industry.dll` in one folder and hits that rule on its own
account. `exlib.industry.dll` is a module: an assembly that extends a framework or a mod without a
`ModSystem` of its own, and is driven through a host mod's lifecycle instead, getting the same
registration in the same phases. See [Modules](Modules) for identity, the two shipping forms, the
phase table and the worked example.

## Registration cheat-sheet

Driving the registries yourself, in the phases `ExModSystem` would have put them in:

```csharp
public override void Start(ICoreAPI api)
{
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);    // blocks/items/entities/behaviours + def providers
    YourValues.Load(api);                                        // generated config accessor (Config System)
}

public override void StartClientSide(ICoreClientAPI api)
{
    PreferenceRegistry.RegisterAll(api, Mod, GetType().Assembly);   // before the commands that name them
    CommandRegistry.RegisterAll(api, Mod, GetType().Assembly);      // [CommandRegister]/[SubCommandRegister]
}

public override void StartServerSide(ICoreServerAPI api)
{
    CommandRegistry.RegisterAll(api, Mod, GetType().Assembly);
}
```

`CommandRegistry.RegisterAll` is safe to call from `Start` - each command declares its side, so it
registers once either way - but every shipped mod calls it from the two side hooks instead. That is
what keeps a client sub-command from being built before the preference it names exists.

## Other mods

A mod often wants to do something only when another mod is present: a bridging recipe, a patch to
that mod's blocks, a compatibility layer that must not load otherwise. `ExMods` is three ways to
ask, shortest first.

A one-line check:

```csharp
if (ExMods.IsLoaded(api, "toolsmith")) { /* ... */ }
if (ExMods.AtLeast(api, "toolsmith", "1.9.0")) { /* ... */ }
```

A JSON patch condition, no C# at all - exlib sets `ExMods.FlagKey(modId)` (`"exlib:mod:<modid>"`) to
`true` in world config for every enabled mod, before the patch loader runs:

```json
{ "op": "add", "path": "/...", "condition": { "when": "exlib:mod:toolsmith", "isValue": "true" } }
```

A run-now callback, for code that only makes sense once:

```csharp
ExMods.WhenLoaded(api, "toolsmith", () => RegisterToolsmithCompat(api));
```

```csharp
public static class ExMods
{
    public static bool IsLoaded(ICoreAPI api, string modId);
    public static string? Version(ICoreAPI api, string modId);
    public static bool AtLeast(ICoreAPI api, string modId, string minimumVersion);
    public static bool WhenLoaded(ICoreAPI api, string modId, Action action);
    public static string FlagKey(string modId);
}
```

`IsLoaded` never throws: a null or blank id is just not loaded. `AtLeast` compares the way the game
itself does, so a pre-release such as `"1.9.0-rc.1"` sorts below its own release `"1.9.0"`.

## Harmony

Harmony is the runtime patching library mods use to change a vanilla method they cannot subclass or
override. Applying patches is two calls, one at startup and one when your mod is disposed, and both
want guarding: several mods built on the same framework share one process, and a patch applied
twice or never removed is everyone's problem. `ExHarmony` is that pair with the guards in it, and
the bootstrap is three lines, once per mod system:

```csharp
public override void Start(ICoreAPI api)
{
    _harmony = ExHarmony.PatchOnce(Mod, GetType().Assembly);
}

public override void Dispose()
{
    ExHarmony.UnpatchAll(Mod);
}
```

`PatchOnce` applies every uncategorised `[HarmonyPatch]` class in the assembly, guarded so it is a
no-op on a second call however many dependent mods share the process. A class also carrying
`[HarmonyPatchCategory("...")]` is left alone until you opt it in, gated on another mod being loaded:

```csharp
ExHarmony.PatchCategoryWhenLoaded(api, _harmony, GetType().Assembly, "compat-toolsmith", "toolsmith");
```

```csharp
public static class ExHarmony
{
    public static Harmony PatchOnce(Mod mod, Assembly assembly);
    public static bool PatchCategoryWhenLoaded(ICoreAPI api, Harmony harmony, Assembly assembly, string category, string requiredModId);
    public static void UnpatchAll(Mod mod);
}
```

## Related pages

- [Config System](Config-System) - `[ExConfigRegister]` and the generated value accessor.
- [Source Generators](Source-Generators) - what config classes and lang files generate.
- [Commands](Commands) - the shared `/exmod` root.
