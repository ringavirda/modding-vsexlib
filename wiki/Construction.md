# Construction (Right-Click Constructable)

Vintage Story can raise a block in stages instead of placing it whole. The player sets down the
first piece, right-clicks it holding the next material, and each accepted material reveals another
part of the block's shape. The game calls this right-click construction, and it is how a player
assembles a machine too large to carry as one item.

Building one from a mod meets two problems. The game's own construction behaviour exists only from
version 1.22 on, so one block JSON cannot serve 1.20 and 1.21 too. And a half-built machine is a
state your mod must answer for: broken, it should hand back some of what went into it; unfinished,
its machine tick should usually not run.

`Blocks/Construction/` answers both. exlib registers one behaviour under the JSON name
`ExRightClickConstructable` on every supported version - a thin subclass of the game's own on
**1.22**, a full port of it on **1.20 / 1.21** - so your block JSON does not change with the game
version. On top of the vanilla flow it replaces the break handler, scattering the materials of
every completed stage at a fraction each mod can expose in its config, and it answers a
[production tick](Production-Machines) with "not yet" while the build is unfinished. You list the
behaviour on your block with the stages it builds through, and that one entry carries all three.

## `ExRightClickConstructable`

The behaviour name carries no mod prefix: exlib owns `ExRightClickConstructable` on every version,
so one JSON entry loads against the vanilla subclass or the port alike.

```jsonc
"entityBehaviors": [ { "name": "ExRightClickConstructable", "properties": { /* stages */ } } ]
```

A stage names the shape elements it adds or removes and the material stacks it consumes; on 1.20
and 1.21 exlib's ported `ExConstructionStage`, `ExConstructionIngredient` and
`ExRightClickConstruction` read that same table.

Public surface:

```csharp
// Available on all versions:
public ItemStack[] GetConstructionDrops(float ratio, Random rand);   // materials scattered at `ratio` (0..1)
public WorldInteraction[]? GetConstructionInteractionHelp();         // next-stage build-material hover help

// 1.20 / 1.21 only (the reimplementation exposes extra state):
public bool IsComplete { get; }
public CompositeShape shape { get; }
public event Action<CompositeShape>? OnShapeChanged;
public static WorldInteraction[] AppendConstructionHelp(IWorldAccessor world, BlockSelection selection, WorldInteraction[] baseHelp);
```

`GetConstructionDrops(ratio, rand)` returns the materials this block would scatter at the given
fraction of consumed stacks, summed across every completed stage - call it to salvage a partly built
construction yourself, for a burst boiler or a demolition tool.
`GetConstructionInteractionHelp` is the hover text telling the player which stack the next stage
wants, and `AppendConstructionHelp` prepends it to the help your block already offers. `IsComplete`
is what rendering, `GetBlockInfo` and the production gate below read; `shape` is the block narrowed
to the elements built so far, and `OnShapeChanged` fires when a stage changes that set.

The behaviour draws no mesh of its own, so a constructable block is invisible without a companion
animator (`BEBehaviorAnimatable`) running an always-on, looping idle animation. `ConstructedAnimator`
wires the two together: it follows `OnShapeChanged`, rebuilds the animator's mesh from the elements
built so far and re-applies your pose. Call its `Initialize` with that pose callback, its `Dispose`
from `OnBlockRemoved` and `OnBlockUnloaded`.

A stage whose `requireStacks` ingredient uses a wildcard code must set `storeWildCard`: drop
resolution needs the variant the player actually fed it, and without that record breaking the block
throws inside vanilla `GetDrops`.

## Construction gates production

exlib's production tick asks every publisher of `IProductionReadiness` on a block entity - the block
entity itself and each of its behaviours - whether work may run (see
[Production Machines](Production-Machines)). `ExRightClickConstructable` is one: `IsReadyToProduce`
is `IsComplete` and `StopsProductionWhenNotReady` is `true`, so a host that carries the behaviour
and a production tick waits for the build with no gate written by hand.

```jsonc
"entityBehaviors": [
  { "name": "ExRightClickConstructable", "properties": { "stages": [ /* ... */ ], "gatesProduction": false } }
]
```

A machine that must keep ticking while unfinished (rendering a break, holding a status message) sets
`gatesProduction: false`, either in raw JSON or through the definition builder:

```csharp
.Construction(c => c.Stage(s => s.AddElements("frame")).GatesProduction(false))
```

With the opt-out, `IsReadyToProduce` is always `true` and `StopsProductionWhenNotReady` is `false` -
the behaviour publishes nothing that blocks or stops the tick, while `IsComplete` still answers
whatever else reads it directly (rendering, `GetBlockInfo`).

## `ExRccSettings`

How much of a broken mega-block the player gets back is a balance question each mod answers for
itself. `ExRccSettings` is the registry for that salvage fraction, resolved at break time from the
block's `Code.Domain`, so two mods on one server can hold different values.

```csharp
public static class ExRccSettings
{
    public static void RegisterBrokenDropsRatio(string domain, Func<float> ratio);
    public static float? BrokenDropsRatio(string domain);   // null if the domain didn't register
}
```

Register at startup, wiring the getter to your [config](Config-System) so players can tune it live:

```csharp
ExRccSettings.RegisterBrokenDropsRatio("iiex", () => IiexValues.BoilerSalvageRatio);
```

That is the whole integration: the behaviour reads the getter on every break, falls back to the
block's own `brokenDropsRatio` JSON property when the domain registered nothing, and scatters the
materials itself from its `OnBlockBroken` - not from `Block.GetDrops`, which returns nothing for a
constructable block. A break in creative mode drops nothing.

A mega-block that should not also drop its own frame item needs `GetDrops` overridden to return
`[]` on the controller block: a JSON `drops: []` is not honoured for variant blocks.

## Related pages

- [Config System](Config-System) - back the salvage ratio with a live-editable value.
- [Recipe Costs](Recipe-Costs) - RCC stage costs are also adjustable per cost profile.
- [Helpers & Renderers](Helpers-and-Renderers) - `ToggleAnimator`, the same animator plumbing for a
  machine that is animated but not built in stages.
