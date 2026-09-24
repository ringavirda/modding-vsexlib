# Content Checks

Most of a mod is content rather than code: block and item codes, recipes, translation keys,
multiblock layouts. The compiler never sees any of it, and the game does not complain about it
either. A recipe that names `yourmod:cokeoven-lit` when the block is really `yourmod:coke-oven-lit`
builds fine and loads fine; the recipe just never shows up in a crafting grid. A block whose name
key is missing from `lang/en.json` shows the player the raw key instead of a name. A multiblock
layout that names a block you renamed last week builds a structure that can never complete. Every
one of these fails silently, and the person who finds it is usually a player, weeks later.

`ExpandedLib.Checks` is twelve rules that read your content and report what does not line up. They
run by themselves at the end of world load and write their findings to the server log, so the first
time you boot a world with a broken code in it you read "names a code that does not exist" instead
of wondering why a recipe vanished. The same twelve run on demand from `/exmod verify`, from your own
code, from a unit test, and from a command-line tool that needs no running game. Five more need the
loaded game itself, vanilla included, and run from `/exmod verify` only, which `exmod check` sends
a booted server after the tests (see [Loaded checks](#loaded-checks)).

There is nothing to switch on. Install exlib, load your mod, and the checks report. The rest of this
page is for the three cases beyond that: running them when you choose, running them without the
game, and adding a rule of your own for an invariant that is yours rather than the framework's.

A check never touches a registry, a file path or an assembly. Each one reads through
[`ICheckSource`](#writing-a-custom-ichecksource), an interface that answers ten questions about a
domain - a domain being one mod's id, the part before the colon in `yourmod:coke-oven`. That is why
one rule, written once, runs against the live game, against a repository tree, and against a zip on
disk without knowing which it is looking at.

## What each check catches

| Check | What it looks for | What goes wrong without it |
| --- | --- | --- |
| `DefinitionCatalogueCheck` | Every [code-first](Code-First-Definitions) block definition your mod declares produced a registered block. | A definition the loader silently dropped: the block is in your C#, absent from the game. |
| `LateDefinitionCheck` | Every definition was registered before the injection deadline. | A definition registered too late is never built at all. See below. |
| `MultiblockCodesCheck` | Every block code a [multiblock](Multiblock-Structures) layout cell names is a block some mod registers. | A structure whose layout names a code that does not exist can never be completed by the player. |
| `RecipeCodesCheck` | Every grid recipe whose output is a block in your own domain names a block you register. A `{name}` a named ingredient without `allowedVariants` fills counts when some block you register matches it in any state. | The recipe loads and is simply never craftable. Item outputs are not covered. |
| `LangCoverageCheck` | Every registered block code resolves to a name in the `en` locale. | The block's name renders as the raw key, `yourmod:block-coke-oven`, in hand and in the handbook. |
| `NetworkNodeContractCheck` | A [network node](Block-Networks) declares a `type` variant group and the orientation scheme it ships; a declared membership names the network it joins. | A node missing its `type` group has no allowed orientations, so placing it fails with no message at all. |
| `PinnedNetworkNodesCheck` | No shipped layout pins the orientation of a network node. | A node picks its own orientation from its neighbours, so a pinned cell can be contradicted at any moment. Mark the cell with the layout's `Connector` instead. |
| `CodePrefixCollisionCheck` | No block's base code is a proper prefix of another's at a `-` boundary. | A wildcard `yourmod:pipe-*` written for the short code also swallows `yourmod:pipe-plated-*`, quietly widening every rule built on it. |
| `StageWildcardsCheck` | Every [construction](Construction) stage ingredient whose code holds `*` carries `storeWildCard`, keyed by the one variant group its `*` spans (`wood`, `metal`), a group of every block it matches; a `{key}` placeholder names one of the block's own variant groups or a key an earlier paid stage stores; a key on the unpaid stage 0 is stored by stage 1; a key is stored by one paid stage, later stages taking it as `{key}` (an ingredient whose code holds its own `{key}` is no second store). The key must be `wood` or `metal`, the two a creative Ctrl build stores. | Breaking the structure throws a NullReferenceException, or its refund silently names a code that does not exist and drops nothing. |
| `GridRecipeShapeCheck` | Every key of a grid recipe's `ingredients` appears in its `ingredientPattern`, every pattern letter other than `_` and a space has a key, the pattern has width times height slots, and the grid is at most 3x3. | A key the pattern never places is never consumed and never shown, so two recipes differing only in it take the same input. A letter with no key or a pattern of the wrong length fails the recipe at load, and a larger grid never matches. |
| `GridRecipeCollisionCheck` | No two grid recipes of the domains the check covers match the same input. It counts as the game's matcher does: a pattern trimmed of empty rows and columns at any offset it fits, a shapeless recipe's ingredients taking the input merged into one stack per item, a wildcard over what it matches as `allowedVariants` narrow it, and a named wildcard in one state across its slots. The game has no mirrored match. | The grid crafts whichever of the two recipes it finds first, and the other is never made from that input. |
| `GridOutputVariantCheck` | A grid recipe whose output is an oriented block names the orientation its `creativeinventory` lists. A block is oriented by the variant group its orientation behaviour writes (`ExOrientable`, `HorizontalOrientable`, `NWOrientable`, `Pillar` or `OmniRotatable`) or its block class writes (`BlockStairs`, when the check's source resolves the class). | The game crafts the block the recipe names, but the handbook page of the creative block lists no recipe, and the crafted stack does not stack with the default the block drops when broken. |

Three of the twelve have a condition attached.

`LateDefinitionCheck` is the odd one out. It names every block, item or recipe definition registered
after `ExDefinitionModSystem` already injected (see [Code-First-Definitions](Code-First-Definitions)),
which the loader then never builds. To do that it has to read `ExDefinitions` directly rather than
`ICheckSource`, and it reports nothing until injection has actually run once in the process, so an
`ICheckSource` built without replaying injection never sees it. The load-time pass runs on the
server only.

`GridRecipeCollisionCheck` reads ingredient codes, and tags only from a loaded game: over
`AssetCheckSource` on 1.22 and later, an ingredient with tags and no code takes the collectibles of
its class whose tags meet it. Over any other source, or before 1.22, such an ingredient overlaps
every ingredient of its item class, so a pair it reports can be one the loaded game tells apart.
Attributes and `skipVariants` narrow a match and are not read. A shapeless recipe
matches its input merged into one stack per item, as the game merges it, so a shapeless oak and pine
also takes oak, oak and pine laid in three slots, and a shapeless oak and oak takes one slot of oak.
An ingredient code without a domain names an item of its recipe file's mod, as the game reads it,
and so does an output code for `GridOutputVariantCheck`.
A recipe the game refuses at load or never matches (disabled, a letter with no key, a pattern that
does not fill its declared grid, a grid larger than 3x3) is skipped. A pair across two mods is
reported in the run of each.

`LangCoverageCheck` in this library only guards the `en` locale. An unresolved `en` key is the one
that renders raw on screen, since every other translation falls back to it. Parity across a mod's
other shipped locales (a missing Ukrainian key, say) is a repository-time concern instead: see
`ExpandedLib.Testing.LangCoverage` and each mod's own `LangParityTests`.

## Loaded checks

Five rules need what only the loaded game knows: vanilla's recipes and blocks, what every recipe
registry makes, the variant groups of `game:` codes. They read an `ILoadedGame`, an `ICheckSource`
that also hands over every registered block and item (`Collectibles`), what the recipe registries
and exlib's process catalogues make (`RecipeOutputs`, one `LoadedOutput` each) and what a
tag-only ingredient takes (`Tagged`). `AssetCheckSource` is the one implementation; a repository or
assembly source has no vanilla content and no patches. The five run from `/exmod verify` only,
never at world load. `exmod check` runs them on every change: its last step is `exmod smoke`, which
boots a dedicated server of the current series (1.22) with the built mods and sends
`/exmod verify`, and fails on any error it reports. `check all` runs smoke once, on 1.22; `check
1.21` and `check 1.20` skip it.

| Check | What it looks for | What goes wrong without it |
| --- | --- | --- |
| `ObtainabilityCheck` | Every recipe ingredient, every construction stage ingredient and every block a creative tab lists is made, one step deep, by one of: a loaded recipe's output (grid, cooking, barrel, alloy, smithing, knapping, clayforming); an output of exlib's process catalogues (a terminal job, a stock route's stopping point, a loaded die's job); a smelted, crushed or ground stack; a beehive kiln firing; the drop of a block of another type; a world source (`game:gravel-*`, from worldgen); a code a mod declares with `ExlibChecks.Produces`. A wildcard is made when one code it matches is, among the states its stack's `allowedVariants` allow and its `skipVariants` leave, and a creative-listed block when a block differing from it only in groups its placement writes is: the orientation groups `GridOutputVariantCheck` recognises, and a network node's `orientation`, the faces it connects on. Another machine or pipe shape of the same blocktype does not count. A creative tab is no source. | A survival player can never obtain the ingredient, so the recipe or structure asking for it is never finished. |
| `VanillaGridCollisionCheck` | No grid recipe of the domain matches the same input as a vanilla grid recipe, patches applied, counted as `GridRecipeCollisionCheck` counts. The domain's own pairs are that check's. | The grid crafts whichever recipe it finds first, and either yours or vanilla's is never made from that input. |
| `GameReferencesCheck` | Every `game:` code the domain's recipes, construction stages and definition bodies name (outputs, ingredients, stage requirements, drops, smelted, ground and shattered stacks) matches a block or item of the class it names that the loaded game registered; a wildcard matches one. A code with no domain is `game:` in a construction stage and the file's own domain elsewhere, as the game reads each. | The reference resolves to nothing: an ingredient no stack fills, a drop that drops nothing. |
| `LoadedStageWildcardsCheck` | `StageWildcardsCheck`'s rules for a stored construction wildcard of a domain the run does not cover, such as `game:plank-*`: its `*` spans only the variant group its `storeWildCard` key names, the key is a variant group of every loaded match, and the pattern matches something. | Breaking the structure refunds a code that does not exist, or throws. |
| `CollectibleCollectionsCheck` | No collectible of the domain carries a null `CreativeInventoryTabs`, and no block a null `Variant`. The loader fills both, so a null is a mod's code clearing one after load. | Vanilla dereferences both: looking at a mount throws on the first, the snowball system at load on the second. |

### What your machines make: `Produces`

A machine's product, or a block cast or poured in place, is made by no recipe any registry holds, so
`ObtainabilityCheck` would report every recipe that asks for it. Declare it from your mod system's
`Start`:

```csharp
ExlibChecks.Produces(Mod.Info.ModID, "yourmod:castplate-*", "the casting table");
```

The code is domain-qualified and may be a wildcard; every loaded block and item it matches counts as
made, in every domain's run. A declaration that matches no loaded block or item is an
`Obtainability` error of the declaring domain, naming the machine. The outputs of exlib's process
catalogues (`ProcessJobRegistry`, `ProcessRouteRegistry`, a die's `machinejob`) need no declaration.

### A finding you ship knowingly: `Exempt`

`ExlibChecks.Exempt(domain, rule, codes, reason)`, called from `Start`, takes every finding of
`rule` (the check's name as its `CheckResult` carries it, e.g. `GridRecipeCollision`) in `domain`'s
runs that names each of `codes` as a whole word, out of the errors. A code is whatever the finding
names: a code, a recipe's `file#position`, or words of the defect (`key G`). Whole word means
`a.json#1` does not take `a.json#10`, and `slag-block` does not take `yourmod:slag-block`: a `:` or
`.` ends a word only before white space or the end, and starts none. A finding about two things,
the two recipes of a collision or the two codes of a prefix clash, lists them in the result's
`Subjects`, and is taken only by an exemption naming both. `Exempt(domain, rule, code, reason)`
names one. An exempted finding moves to the result's `Exempted` lines, followed by the reason, and
is logged at Notification; it applies at load, in `/exmod verify` and in `ExlibChecks.For`,
`LoadedFor` and `Verify`.

```csharp
ExlibChecks.Exempt(
    Mod.Info.ModID,
    "GridRecipeCollision",
    ["yourmod:recipes/grid/crate.json#0", "yourmod:recipes/grid/crate.json#1"],
    "the oak and pine crates are one item"
);
```

An exemption that takes no finding in a run of its rule is itself reported, in a result named
`Exempt`; `ExlibChecks.Verify` also reports one whose rule never ran. Of two exemptions taking the
same finding the first given takes it, and the second, when it takes nothing else, is reported as
its duplicate. Exemptions and `Produces`
declarations are dropped when a world starts loading, so each load's `Start` declares them again.
A finding another mod's content causes in your domain (its patch of your recipe, say) is exempted
by that mod, for your domain, since the exemption is unused whenever it is not loaded.

There are three rungs, in increasing order of control.

## Rung 1: nothing to do

`ExpandedLibModSystem.AssetsFinalize` runs every check against the live game state on the server
and logs the results, after the metal/fluid/process catalogues finish loading. A client runs none:
it receives no recipes, a server asset category. Each check logs one summary line
naming itself, its domain and how many errors it found, followed by one line per error. A modder who
never opens xUnit still sees "your recipe names a code that does not exist" in the server log the
first time the world loads with the mistake in it.

Set `RunChecksOnLoad` to `false` in `exlib`'s config (`ex_values.json`) to skip this pass - the one
reason to is the one-time scan costing something noticeable on a very large modpack's world load.

## Rung 2: `/exmod verify`

When you have already loaded the world and want the checks again after an asset reload, or want to
read the result without hunting through a log, run them from the server console or a chat command:

```
/exmod verify           # exlib itself plus every mod that depends on it
/exmod verify iiex      # one domain only, named explicitly - any loaded mod, dependent or not
```

With no argument, only exlib and its dependents are checked - never a bystander mod with no exlib
dependency, and never vanilla's own `game`/`survival`/`creative`, which never declares one and whose
own incomplete locales are not this library's to police. Naming a domain explicitly checks it
regardless of whether exlib depends on it, so long as some loaded mod answers to that id.

The command runs the twelve content checks, any registered ones and the five
[loaded checks](#loaded-checks) through `ExlibChecks.Verify`, which applies every exemption and
reports each unused one. It prints how many checks ran and how many errors they found, then the
first ten error lines; the full list always goes to the server log via the same `ExlibChecks.Log` call
`AssetsFinalize` uses, so a long list is never truncated where it matters.

This is exactly the command `exmod smoke` (see [Testing Harness](Testing-Harness#the-smoke-lane))
runs against a freshly-booted dedicated server before stopping it, so the smoke lane's pass/fail
includes whatever `/exmod verify` finds, and `exmod check` runs smoke as its last step.

## Without the game: `exlib-verify`

A JSON-only modder has no code to build and no reason to install xUnit, but still wants "does my
mod even load" before ever launching the game. `exlib-verify`, the `ExpandedLib.Verify` .NET tool
built from [extools](https://github.com/ringavirda/modding-vsextools) and installed with
`dotnet tool install -g ExpandedLib.Verify`, answers that from a mod folder or zip alone, against a
provisioned game install and any number of other mods:

```
exlib-verify <modpath> [--game <install>] [--mods <dir>...] [--json] [--strict]
```

`<modpath>` is a folder or a zip carrying `modinfo.json`. `--game` defaults to the same
`VINTAGE_STORY`-or-`.game/<slug>` resolution the test harness uses; `--mods` loads any number of
other mods (folder or zip) as additional asset domains, so a compatibility patch against a mod that
isn't the one under test can actually be checked. `--json` prints a stable
`{level, check, file, line, message}` array for CI; `--strict` also fails the run (exit 1) on an
informational finding, not only an error.

Findings come in two grades. An **error** means the tool can prove the content is wrong, and exits 1:

- a JSON file under the mod's own `assets/` that does not parse, with line and column
- a patch whose `file` target exists in no loaded domain, once its `dependsOn` mods are satisfied
  (a target in a mod that isn't loaded and isn't required is informational instead - see below)
- a patch operation that does not apply against the real target document - `add`/`replace`/
  `remove`/`addmerge`/`addeach`/`move`/`copy`, run through the game's own `Tavis.JsonPatch` engine
  exactly the way `ModJsonPatchLoader` drives it, against the real (and, by the time a check runs,
  already-patched) target JSON
- a `title`/`text` key a `config/handbook/*.json` page names that has no matching key in that
  key's own domain's `lang/en.json`
- a recipe ingredient or output code - `{ "type": "item"|"block", "code": ... }`, wherever it
  appears in a recipe's own JSON shape - that resolves to no block or item code declared anywhere
  across the mod, the game, and any `--mods`

An **informational** finding means the tool cannot decide from outside a running game, and says so
rather than guessing. It exits 0 unless you pass `--strict`:

- a patch's `dependsOn` naming a mod id this run has no `--mods` for - the patch is not evaluated,
  since it may be entirely correct once that mod is actually loaded alongside it
- a patch `condition.when` - there is no live world config outside a running game to evaluate it
  against, so the patch is named but never evaluated
- a `variantgroups` entry this tool cannot expand headlessly (`loadFromProperties`, which needs the
  loader's own `ICoreServerAPI`-bound world-property resolution) - a reference under that type's
  base code is assumed to resolve rather than risking a false error
- a code whose domain isn't loaded at all (no `--mods` for it) - this run has no way to say whether
  it resolves
- a locale other than `en` missing some of `en`'s keys, one line per locale naming the count - the
  same gap `ExpandedLib.Testing.LangCoverage` tracks for a checked-in mod, since a missing non-`en`
  key falls back to English in game rather than showing raw

A hybrid code+JSON mod (most third-party mods on the Mod DB) will still show real findings this way:
anything it registers from C# is invisible to a JSON-only scan, so a reference to it reads as
unresolved. That is a limitation of what a JSON-only pass can know, not a defect in the check - see
extools' `verify/ExlibVerify.Tests`'s own run over `.compat/_im` and `.compat/industrialstory` for
what this looks like against two real mods.

## Rung 3: `ExlibChecks.All` from your own code

When you want the findings as data rather than as log lines - to fail a build, to show them in your
own command, to count them in a test - call the runner yourself:

```csharp
using ExpandedLib.Checks;

IReadOnlyList<CheckResult> results = ExlibChecks.All(api);
foreach (CheckResult result in results.Where(r => r.Errors.Count > 0))
    DoSomethingWith(result);
```

A `CheckResult` is the check's name, the domain it examined and one readable line per violation; an
empty error list means that check found nothing. `All(ICoreAPI)` is the in-game path, over a fresh
`AssetCheckSource`. `All(ICheckSource)` runs the same checks against any source you build yourself -
useful for a build-time script, a CI job, or a tool that reads from somewhere other than a running
game. `Loaded(ILoadedGame)` and `LoadedFor(game, domain)` run the five loaded checks, and
`Verify(game, domain)` runs both kinds for one domain, or every domain when it is null.

## Adding your own check

The twelve checks above are exlib's own. A mod's own content invariant - every machine's job table
names a registered item, every diagram has a shape - gets the same three rungs for one line of its
own, and none of them is a call you have to place.

**Zero-config.** Nothing changes about the shipped checks: they keep running as Rung 1, 2 and 3
above regardless of whether you add one of your own.

**Declarative: `[ExCheckRegister]`.** Write a class exposing `static CheckResult Run(ICheckSource,
string)` and mark it. Declare it a plain (non-`static`) class - the scan that finds it skips
abstract types, and a C# `static class` compiles to one:

```csharp
[ExCheckRegister]
public sealed class MyOwnCheck {
    public static CheckResult Run(ICheckSource source, string domain) {
        var errors = new List<string>();
        // ... your rule against source.BlockCodes, source.Recipes(domain), etc.
        return new CheckResult("MyOwn", domain, errors);
    }
}
```

`ExCheckRegistry.RegisterAll` scans for it the same way `EntityRegistry.RegisterAll` scans for
`[BlockRegister]`, and runs automatically from a deriving `ExModSystem`'s or a module's own `Start` -
there is nothing to call. A class carrying the attribute but not shaped exactly this way is warned
about and skipped; the same class scanned twice (a rejoined world, a module and its host sharing an
assembly) is registered once and every later scan is silently ignored.

**Explicit: `ExlibChecks.All`.** Once registered, your check is appended after the twelve shipped
ones, in registration order, and runs at every rung above - the `AssetsFinalize` log line,
`/exmod verify`, and `ExlibChecks.All` from your own code - with no further wiring. A throw from
`Run` is caught and reported as one error naming your check, the way `ExModuleHost.Isolate` wraps a
module phase, so one bad rule does not take the other checks down with it.

## Writing a custom `ICheckSource`

A custom source is how you run the shipped rules over content the game has not loaded: a repository
tree in CI, a zip on disk, a fixture in a test. Implement the six members - `Domains`, `BlockCodes`,
`ItemCodes`, `Recipes(domain)`, `Lang(domain)`, `BlockDefinitions(domain)` - over whatever you are
validating, and every check runs unmodified. The seventh, `BlockTypes(domain)`, the JSON blocktypes
a domain ships, yields nothing unless you implement it; `StageWildcardsCheck` then reads code-first
definitions only. The eighth, `ItemTypes(domain)`, the JSON itemtypes, yields nothing unless you
implement it either; `GameReferencesCheck` then reads no itemtype bodies. The last two, `BlockClass(classKey)` and `BlockEntityBehaviorClass(key)`, turn a
`class` key or a behaviour key into its C# type and answer null unless you implement them;
`NetworkNodeContractCheck` then knows a node and a membership by their JSON alone.

There is one exception. `LateDefinitionCheck` ignores the source it is handed and reads
`ExDefinitions`, the process-wide registry `ExDefinitionModSystem` injects from, directly. A custom
source not backed by a live game process - `exlib-verify`, a CI job reading a repository tree - can
never make it report; it still prints a "0 error(s)" line, which reads as a pass. `AssetCheckSource`
and the harness's `RepoCheckSource` are the two shipped implementations; reading either is the
fastest way to see what each member is expected to answer.

## What moved from the harness, and what did not

These rules used to live only in `exlib.testing`, where a mod's xUnit suite called them. They moved
here so the game itself runs them, which is why this page exists at all. If you already have a test
suite calling the harness, nothing you wrote has changed; this section is the map between the two.

`MultiblockCodesCheck`, `RecipeCodesCheck`, `LangCoverageCheck`, `CodePrefixCollisionCheck`,
`PinnedNetworkNodesCheck` and `NetworkNodeContractCheck` hold the rules the harness's
`MultiblockCodes`, `RecipeCodes`, `LangCoverage.MissingNames`, `CodePrefixCollision`,
`PinnedNetworkNodes` and `NetworkNodeContract` used to carry themselves; those harness members now
call the check, so the rule is written once. `LateDefinitionCheck` never lived in the harness, since
there was nothing there to replay `ExDefinitionModSystem.AssetsLoaded`. `StageWildcardsCheck`
started here; the harness runs it over a suite's definitions as `StageWildcards`. The three grid
recipe checks, `GridRecipeShapeCheck`, `GridRecipeCollisionCheck` and `GridOutputVariantCheck`,
started here too; the harness runs them over a suite's definitions as `GridRecipes`.
`GameReferencesCheck` holds the reference extraction the harness's `ReferencedCodes` used to carry;
`ReferencedCodes` now reads its references from the check.

`DefinitionCatalogueCheck` is not the harness's `DefinitionCatalogue`. The check reports a
code-first block definition that produced no registered block; the harness's `Resolves` answers
whether a stack's code names something a mod registers. They share only the expansion of a
definition's variant groups into code patterns.

`NetworkNodeContractCheck` knows a network node two ways: its `class` key resolves, through
`ICheckSource.BlockClass`, to a `BlockNetworkNode`, or it declares a behaviour named "ExOrientable"
in `network` mode. A membership is a behaviour keyed `BEBehaviorNetworkMember`, or one that
resolves, through `ICheckSource.BlockEntityBehaviorClass`, to a subclass of it. In game the keys
resolve through the class registry; the harness resolves them by reflection over the suite's
assemblies, then through the classes the install's vanilla mod systems register. A source that cannot resolve a key leaves that definition to the JSON selection. See
[Testing-Harness](Testing-Harness) for the harness side of this split.
