# Modules

Sooner or later a mod outgrows one dll. A framework grows a content layer that half its users do
not want. A feature gets large enough to build, version and test on its own. Another modder wants
to extend your mod and needs somewhere to put the code that is not inside your repository.

The obvious move is a second dll with a `ModSystem` of its own to do its registering - a
`ModSystem` being the class the game constructs for a mod and calls at each phase of startup - and
that is the one thing the engine refuses. A mod folder may hold as many dlls as it likes, but only
one of them may contain a `ModSystem`; the engine refuses the whole mod when a second one does,
with no indication of which files collided. So the second assembly has no way of being told that
startup has begun, and nothing registers what is in it.

A module is exlib's answer. It is an assembly that declares an id and names a host mod, and the
host drives it through the phases it would have had for itself: its config loaded, its classes,
commands and preferences registered, and a hook at every phase for its own work. Declaring one is a
single assembly attribute. Implementing one is a class that implements `IExModule` and overrides
only the phases it needs.

`ExpandedLib.Industry` - pipes, molten metal, mechanical power, metals and heat - is the worked
example, a module shipped inside exlib's own mod folder. A module can equally ship as its own
Vintage Story mod that other mods depend on.

## What a module is

Concretely, a module's assembly carries an id, the id of its host, and one or more entry points.
The host loads that assembly's config, registers its classes, commands and preferences, and calls
its entry points at each phase. The engine sees only the host: there is one `ModSystem` in the
folder, and the module never needed one.

exlib needed that for itself before it could offer it to anyone else: `exlib.dll` and
`exlib.industry.dll` ship in one folder, so the industry layer could never have had a mod system of
its own. Other modders have asked for electric and heating layers of their own; those are modules
too, and a third party's own mod can be one without shipping inside exlib's folder at all.

## The two assembly attributes

One assembly-level attribute is the whole of what discovery needs. It goes in any file of the
module's project, conventionally `AssemblyInfo.cs`, and nothing calls it: exlib scans the loaded
assemblies for it.

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ExModuleAttribute(string id) : Attribute {
  public string Id { get; }
  public string Host { get; set; } = "exlib";
  public string Mod { get; set; } = id;
  public string[] Requires { get; set; } = [];
  public bool PatchHarmony { get; set; }
}
```

- `Id` is the module's id, unique across every module loaded in the process, lower-case.
- `Host` names the mod whose lifecycle drives this module. It defaults to `"exlib"`, the framework
  itself; a mod hosting its own modules sets its own id instead.
- `Mod` is the Vintage Story mod id that ships this assembly, defaulting to `Id`. A module shipped
  inside another mod's folder (Industry, inside exlib's) sets this to that mod's id, so `ExModules`
  can tell "not enabled this world" from "not part of the process at all".
- `Requires` names module ids this one runs after, within the same host.
- `PatchHarmony`, when true, has the host patch this assembly's uncategorised `[HarmonyPatch]`
  classes at `Start` and unpatch them at `Dispose`.

`[assembly: ExDomain]` is unchanged by any of this - it still names the asset domain an assembly's
registered classes and code-first definitions are keyed under. A module carrying no `ExDomain` is
still discovered and driven; its classes key under its host's mod id instead, through
`EntityRegistry`'s existing fallback. That is right for a mod's own private second assembly, and
wrong for a framework module meant to be extended by other mods, which should declare its own
domain the way Industry keeps `exlib`.

## The two shipping forms

A module ships one of two ways:

- **Inside the host's mod folder**, beside the host's own dll - Industry's shape, `exlib.industry.dll`
  next to `exlib.dll` in the exlib mod folder. This is how a framework grows a second assembly
  without a second `ModSystem`.
- **As its own Vintage Story mod**, depending on the host mod so the game loads it first. This is the
  third-party shape: several mods can each depend on the same module mod, which a module dll
  embedded and duplicated inside two different mod folders cannot do - two assemblies of the same
  name loaded twice is not supported.

The second form costs one extra file. A folder of its own puts the module in front of the engine's
Code-mod loader, which does not know what a module is: a code mod whose folder contains no
`ModSystem` at all is refused outright, with `declared as code mod, but there are no .dll files
that contain at least one ModSystem or has a ModInfo attribute`. So a module shipped as its own mod
carries an empty placeholder,

```csharp
public class YourModuleModSystem : ModSystem { }
```

purely to satisfy that check. Nothing goes in it - `ExModuleModSystem` is what actually drives the
module's own entry points through the phases below. A module shipped inside a host's folder needs
no placeholder: the host's own dll already satisfies the loader for the folder.

## Enabled-mod filtering

Which mods run is the player's decision, taken per world, and the process can outlive a world.
Finding a module is therefore not the same as running one.

Discovery finds every module in the process, but drives only the ones whose shipping mod
(`ExModuleAttribute.Mod`) is enabled on the world being asked about - a module assembly still
present from an earlier world, or one belonging to a mod the player disabled for this one, is left
out. A module skipped this way is logged once per host lookup, at Notification level:

```
[exlib] module <id> skipped: mod <mod> is not enabled
```

`ExModuleModSystem` also logs, once per host instance at `StartPre`, the modules that host actually
drives:

```
[exlib] modules hosted by exlib: industry
```

or `none` when there aren't any.

## Phases

A module is driven through the same phases as a `ModSystem`, in the host's own execute order, and
each module's entry points run in the host's dependency order (see [Requires](#requires-and-ordering)
below). Before a module's entry points run at all, the host performs the registration a
`ModSystem` would do for its own assembly, against the module's assembly instead:

| Host phase | What the host does for the module first | `IExModule` hook |
| --- | --- | --- |
| `StartPre` | Nothing yet - too early for registration. | `StartPre(ICoreAPI api)` |
| `Start` | `ExConfig.LoadAll`, `EntityRegistry.RegisterAll`, `ExCheckRegistry.RegisterAll`, and - if `PatchHarmony` is set - `ExHarmony.PatchOnce` under the module's Harmony id. | `Start(ICoreAPI api)` |
| `AssetsLoaded` | Nothing extra. Runs at the host's own `ExecuteOrder`, which decides whether an asset read here sees patched JSON - see below. | `AssetsLoaded(ICoreAPI api)` |
| `AssetsFinalize` | Nothing extra. | `AssetsFinalize(ICoreAPI api)` |
| `StartServerSide` | `CommandRegistry.RegisterAll`. | `StartServerSide(ICoreServerAPI api)` |
| `StartClientSide` | `PreferenceRegistry.RegisterAll`, then `CommandRegistry.RegisterAll` (preferences first, the same rule as any `ExModSystem`). | `StartClientSide(ICoreClientAPI api)` |
| `Dispose` | Nothing before; `ExHarmony.UnpatchAll` after, if `PatchHarmony` was set. | `Dispose()` |

Every `IExModule` method has an empty default, so a module overrides only what it needs. The phases
run in the engine's own order - `StartPre`, `Start`, `AssetsLoaded`, `AssetsFinalize`, then
`StartServerSide` or `StartClientSide` for whichever side is running, then `Dispose` - the same
order a `ModSystem`'s own hooks run in.

`AssetsLoaded` is the one phase worth reading twice, because where it falls depends on the host.
exlib's own order is 0.03, ahead of the JSON patch loader at 0.05, so an asset read there sees
unpatched JSON and a catalogue read belongs in `AssetsFinalize` instead. A module hosted by its own
mod's `ExModSystem` runs at that mod's order, 0.1 by default, past the patch loader, so the same
read comes back patched. `AssetsLoaded` is also not where a definition should be contributed,
because one module's `Start` may have run before another's or after - see the next section.

A module that throws from any phase is logged (naming the module type) and the rest of the host's
modules keep running; a module's own failure never takes down the phase for the others.

## `IExDefinitionContributor`

Some definitions cannot be written until the assets are readable. Industry's metal items come from
a catalogue of JSON files that any mod may add to, so what there is to emit is not known until
every mod's `Start` has run. Emitting them from your own `Start` is a race you cannot win: your
module's `Start` may run before another's or after, and which it is changes with the mods installed.

This interface is the phase that has no race.

```csharp
public interface IExDefinitionContributor {
  void Contribute(ICoreAPI api);
}
```

Implement it on a module's entry point, or on a main assembly's, alongside `IExModule`.
`EntityRegistry.RegisterAll` discovers implementors the same way it discovers a mod's registered
classes, so there is no call to write. `ExDefinitions.RunContributors` then instantiates and runs
each one at `AssetsLoaded` 0.04, right before injection and regardless of which host or module
order discovered it. That is the one legal place to emit a definition that depends on loaded
assets, because it is guaranteed to run after every `Start`, however the hosts and modules involved
are ordered against each other. It runs on the server only: the definition system does not exist
client-side.

## `Requires` and ordering

Two modules of one host sometimes have to run in a set order, when one registers a qualifier or a
catalogue the other reads. `Requires` is how the later one says so: a module runs after every
module it names, ties broken by alphabetical order on the id. Three things can go wrong, each
excluding only what it has to and logging one line naming the problem:

- A module names a `Requires` id that is not among the host's other modules:

  ```
  module <id> requires <other>, which is not loaded; <id> is not driven
  ```

- A set of modules requires each other in a cycle:

  ```
  modules <a>, <b> form a requires cycle; none of them are driven
  ```

- Two assemblies declare the same module id; the first (by assembly name) stands and the rest are
  excluded:

  ```
  module <id> is declared by both <asmA> and <asmB>; <asmB> ignored
  ```

`ExModules.IsLoaded(api, moduleId)` answers whether an enabled module of that id was discovered, on
any host - the check a mod makes before relying on one it does not itself require:

```csharp
if (ExModules.IsLoaded(api, "industry"))
  RegisterIndustryCompat(api);
```

Every loaded module also gets a world-config flag, the same idiom as `ExMods.FlagKey` for mods, for
a JSON patch condition to gate on with no C# at all:

```json
{ "op": "add", "path": "/...", "condition": { "when": "exlib:module:industry", "isValue": "true" } }
```

`ExModules.FlagKey(moduleId)` builds the key (`"exlib:module:<id>"`); `ExModuleModSystem` sets it
`true` for every enabled module, on both sides, before the JSON patch loader runs.

## `PatchHarmony`

A module that sets `PatchHarmony = true` gets its uncategorised `[HarmonyPatch]` classes patched at
`Start` and unpatched at `Dispose`, the same convenience `ExModSystem` offers a main assembly -
patched and unpatched under `ExModuleInfo.HarmonyId`, which is `Host` and `Id` joined with a dot
(`"<host>.<id>"`), distinct from the host's own Harmony id and from every other module's.

## Industry as the worked example

Industry ships inside the exlib mod folder, keeps the `exlib` domain so a shipped blocktype and a
player's save keep the keys they already have, and is hosted by exlib itself:

```csharp
// AssemblyInfo.cs
[assembly: ExDomain("exlib")]
[assembly: ExModule("industry", Mod = "exlib")]
```

Its entry point registers a variant qualifier before anything else runs, contributes the metal
resource item family once assets are readable everywhere, and loads the metal catalogue once the
JSON patch pipeline has merged every mod's:

```csharp
public sealed class IndustryModule : IExModule, IExDefinitionContributor {
  public void StartPre(ICoreAPI api) =>
    ExBlockNames.AddVariantQualifier("refractory", "exlib:refractory-");

  public void Contribute(ICoreAPI api) {
    foreach (
      ExItemDef def in MetalFamilyEmitter.Emit(
        AssetCatalogueLoader.GetMany<MetalDef>(api, "config/metals/")
      )
    )
      ExDefinitions.RegisterItem(def);
  }

  public void AssetsFinalize(ICoreAPI api) => MetalCatalogueLoader.Load(api).Log(api.Logger);
}
```

`Mod = "exlib"` is what tells `ExModules.For` that Industry ships as part of the exlib mod, not a
mod of its own called `industry` - the id nothing installs and nothing could ever enable.

## Building a module of your own

None of the samples - [Getting Started](Getting-Started) and [First
Machine](First-Machine)'s `TwinTubBlower` and `BurdenMaker`, `PlatedPipes` and `SmokeStack` - is a
module; all four are ordinary content mods built on exlib, which is what most mods should be. Reach
for a module when a second assembly is the point: a layer other mods extend, or a part of your own
mod with its own build and version. `ExpandedLib.Industry` above is the one worked example in this
repository, and for the third-party shape the pieces are the same, arranged around your own mod
folder and domain instead of exlib's:

- An `AssemblyInfo.cs` (or any assembly-level file) carrying `[assembly: ExDomain("yourmodule")]`
  and `[assembly: ExModule("yourmodule")]` - mod id, module id and domain all the same string is the
  simplest shape, though nothing requires it.
- An empty `ModSystem` to satisfy the Code-mod loader, as above - nothing in it does any work.
- A JSON-shaped def read with `AssetCatalogueLoader.GetMany<T>(api, "config/<yours>/")`, the way
  Industry reads `config/metals/` - one file, one object; a catalogue of several entries ships as
  several files, not one file holding an array.
- A catalogue type populated at `AssetsFinalize`, once the patch pipeline has merged every domain's
  contribution, and read by any mod that depends on you.
- Code-first items or blocks your module contributes, emitted from `IExDefinitionContributor.Contribute`
  rather than from `Start` - see "`IExDefinitionContributor`" above for why that phase is the one
  legal place.
- The entry point itself, one class implementing `IExModule` (and `IExDefinitionContributor` if it
  contributes definitions), registered the same way any other class in the assembly is - by
  `EntityRegistry.RegisterAll`, with no explicit call to write.

A depending mod checks `ExModules.IsLoaded(api, "yourmodule")` before relying on you optionally, or
just declares you as a `modinfo.json` dependency to require you outright - see [Getting
Started](Getting-Started) section 1 for the dependency shape, which is the same for a module mod as
for exlib itself.

## For third parties

A module package publishes under its own id, not `ExpandedLib.*` - that prefix names exlib's own
framework and content-layer packages. `grains`, `electric`, `heating`: whatever the module's own id
is, the package that ships it is named for that, the same as any other Vintage Story mod that
happens to extend a framework instead of shipping content directly.
