# Recipe Costs

Players disagree about grind. The machine that is a fair goal for one group is a week of ore for
another, and the usual answers are bad ones: ship a second set of recipe JSON and keep the two in
step forever, or tell half your players to edit the assets themselves.

Recipe costs are the other answer. One set of recipes, several **named cost levels** over them.
You ship `normal`, which is the costs you authored, and `cheap`, which is `normal` at half price
with every ingredient floored at 1 so nothing becomes free. An admin types
`/exmod recipes yourmod cheap` and every managed recipe changes in place, with no restart and no
second asset folder. Both kinds of recipe are covered: grid crafting, the recipes a player assembles
in the crafting grid, and right-click construction (RCC), where a player raises a block through
stages by clicking materials onto it.

What you provide is a **catalogue**: one entry per recipe you want managed, each matched by a
wildcard code so a single entry covers a whole variant family. What exlib provides is everything
around it - reading your authored quantities out of the live recipes to fill `normal` for you,
scaling the derived levels, repairing a catalogue a player has hand-edited, persisting it, and
applying the chosen level on every load. In the family mods the catalogue is itself a
[config](Config-System), so players can read and edit it as a file, and the chosen level is one
string in the mod's ordinary config.

## The shape of it

A `RecipeProfile` is not data, it is a set of callbacks. exlib has no idea where your catalogue
lives or how your mod remembers the chosen level, so you hand it functions that reach into your own
storage and it drives them:

```csharp
public sealed class RecipeProfile
{
    public required string Code { get; init; }   // command code, e.g. "iiex" in `/exmod recipes iiex cheap`

    public required Func<IDictionary<string, RecipeCostEntry>> Catalogue { get; init; }       // live, persisted
    public required Func<IReadOnlyDictionary<string, RecipeCostEntry>> Defaults { get; init; } // fresh shipped copy
    public required Func<string> GetLevel { get; init; }       // read active level
    public required Action<string> SetLevel { get; init; }     // set + persist active level
    public required Action SaveCatalogue { get; init; }        // persist the catalogue file after fill

    public IReadOnlyList<string> Levels { get; init; } = ["normal", "cheap"];
    public IReadOnlyDictionary<string, double> DerivedLevels { get; init; }
        = new Dictionary<string, double> { ["cheap"] = 0.5 };
}
```

`Levels` is what the command offers, in display order, and the first is the authored baseline.
`DerivedLevels` maps a level name to a scale factor applied to `normal`, each ingredient floored at
1, so `cheap` at `0.5` is half cost with nothing to author by hand. A level in `Levels` but not in
`DerivedLevels` is one you author yourself, entry by entry, in the catalogue.

Registering the profile is the whole of the wiring. On each load exlib runs one pipeline over it:
repair the catalogue against your shipped defaults, read the authored quantities out of the live
recipes into any `normal` profile that is still empty, fill each derived level by scaling, save the
catalogue if any of that changed, then write the chosen level into the live recipes. Only the
server saves; the catalogue file is the server's.

## Catalogue entries

An entry says what kind of recipe it is, how to find it, and what it costs at each level. The two
kinds carry different costs: a grid recipe has ingredient quantities and an output count, an RCC
construction has a set of require-stacks per build stage.

```csharp
public class RecipeCostEntry
{
    public string Type { get; set; } = "grid";   // "grid" (match by output code) or "rcc" (match by block code)
    public string Match { get; set; } = "";       // wildcard code, e.g. "iiex:enginewatt-*"
    public Dictionary<string, RecipeProfileCost> Profiles { get; set; } = new();   // level name -> cost
}

public class RecipeProfileCost
{
    public Dictionary<string, int>? Ingredients { get; set; }                  // grid: ingredient code -> qty
    public int? Quantity { get; set; }                                         // grid: output count (null keeps authored)
    public Dictionary<string, Dictionary<string, int>>? Stages { get; set; }   // rcc: stage index -> {ingredient -> qty}
    public bool HasContent { get; }
}
```

`Match` is compared against a grid recipe's output code or an RCC block's code, and it is
wildcard-aware. One block definition usually expands into a family of codes, one per variant of
metal, size or orientation, so `iiex:enginewatt-*` manages the whole family from one entry instead
of one entry per code.

## Registering

Fill the callbacks in from your own config accessors and register the profile:

```csharp
ExRecipeProfiles.Register(new RecipeProfile
{
    Code = Mod.Info.ModID,
    Catalogue = () => IiexRecipeValues.Recipes,
    Defaults  = IiexRecipeConfig.DefaultCatalogue,
    GetLevel  = () => IiexValues.RecipeLevel,
    SetLevel  = level => IiexValues.Edit(c => c.RecipeLevel = level),
    SaveCatalogue = IiexRecipeValues.Save,
});
```

Call this once from `Start`, after your config and catalogue have loaded, and nothing else is
needed: exlib applies every registered profile from its own `StartServerSide` and
`StartClientSide`. Both sides apply, so what a player sees in the handbook and the crafting grid
carries the adjusted quantities and not the authored ones. The chosen level is only a string in your
[config](Config-System) (`RecipeLevel` above), so it persists like any other setting and is
editable through `/exmod config` as well as `/exmod recipes`.

The registry itself is small. `TryGet` and `Codes` are what the `/exmod recipes` command reads to
list mods and find the one a player named:

```csharp
public static class ExRecipeProfiles
{
    public static void Register(RecipeProfile profile);
    public static bool TryGet(string code, out RecipeProfile profile);
    public static IReadOnlyCollection<string> Codes { get; }
    public static void ApplyAll(ICoreAPI api);
    public static void Apply(ICoreAPI api, RecipeProfile profile);
}
```

## The adjuster (used internally)

You do not need this section to use recipe costs. `ExRecipeProfiles.Apply` drives four
`ExRecipeCosts` helpers, and they are public so that a mod with a pipeline of its own can call them
in a different order or leave one out:

```csharp
public static class ExRecipeCosts
{
    public const string ProfileNormal = "normal";

    public static bool EnsureNormalExtracted(ICoreAPI api, IDictionary<string, RecipeCostEntry> catalogue);
    public static bool EnsureScaledLevel(IDictionary<string, RecipeCostEntry> catalogue, string profile, double factor);
    public static bool Reconcile(IDictionary<string, RecipeCostEntry> live, IReadOnlyDictionary<string, RecipeCostEntry> defaults);
    public static void Apply(ICoreAPI api, IDictionary<string, RecipeCostEntry> catalogue, string profile);
}
```

- `EnsureNormalExtracted` fills the `normal` profile of any entry that lacks one from the live
  recipe's current quantities. That is why you never write `normal` out by hand: the recipe you
  authored is already the baseline. It returns `true` when it changed something, meaning the
  catalogue is worth persisting.
- `EnsureScaledLevel` fills a level by scaling `normal` by `factor`, floored at 1. It skips any
  entry that already carries costs for that level, so a cost you pinned in your defaults survives
  the scaling.
- `Reconcile` repairs a hand-edited catalogue against your shipped defaults: it restores entries a
  player deleted, fixes `Type` and `Match` back to what you shipped, restores pinned defaults that
  were removed, and clamps every quantity to at least 1. Without it, a file a player has trimmed or
  mistyped quietly stops managing the recipes it lost.
- `Apply` is the one that touches the game: it writes a named level into the live grid recipes'
  ingredient and output counts and the live RCC stage costs. Entries with no costs for that level
  are left as authored.

## Registering declaratively with [ExRecipeProfile]

Once the catalogue is itself a config, the `ExRecipeProfiles.Register` call above is boilerplate
that only repeats what the config already says. Add `[ExRecipeProfile]` next to
`[ExConfigRegister]` and `ExConfigGenerator` writes the registration into the generated
`Load(ICoreAPI)` for you; there is then nothing to call from `Start` beyond loading the config.
The catalogue (`Recipes`, `DefaultCatalogue`) and
the active level (`RecipeLevel`) do not have to live on the same config - iiex ships them as two,
the recipe catalogue and the mod's main gameplay config, and points `[ExRecipeProfile]` at the
latter with `LevelConfig`:

```csharp
[ExConfigRegister("ex_recipes.json", "iiex")]
[ExRecipeProfile(LevelConfig = typeof(IiexConfig))]
public class IiexRecipeConfig : IExVersionedConfig
{
    public string? ConfigVersion { get; set; }
    public Dictionary<string, RecipeCostEntry> Recipes { get; set; } = DefaultCatalogue(); // Catalogue

    public static Dictionary<string, RecipeCostEntry> DefaultCatalogue() => new() { /* ... */ }; // Defaults
}

[ExConfigRegister("ex_values.json", "iiex")]
public class IiexConfig : IExVersionedConfig
{
    public string? ConfigVersion { get; set; }
    public string RecipeLevel { get; set; } = "normal"; // GetLevel/SetLevel, via IiexValues
}
```

The generator finds `Catalogue` as `[ExRecipeProfile]`'s own config's sole
`Dictionary<string, RecipeCostEntry>` property and `Defaults` as the matching static
`DefaultCatalogue()`; `Code` is the mod id already passed to `[ExConfigRegister]`, and
`SaveCatalogue` is the catalogue accessor's generated `Save`. `GetLevel`/`SetLevel` read and write a
property named `RecipeLevel` by default, on `LevelConfig` when set, else on the same class - set
`[ExRecipeProfile(RecipeLevelProperty = "...")]` when that config names it differently. A config
missing one of these members fails the build with `#error`, naming what is missing; a class carrying
`[ExRecipeProfile]` with no `[ExConfigRegister]` of its own reports build error `EXLIB0001` instead,
since the generator never runs at all without it.

## Related pages

- [Config System](Config-System) - store the active level and edit it with `/exmod config`.
- [Construction (RCC)](Construction) - `rcc`-type entries adjust construction stage costs.
- [Commands](Commands) - `/exmod recipes`.
