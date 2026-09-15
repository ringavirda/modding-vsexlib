# Extending Processes

The machines in Iron Industry Expanded and Steel Industry Expanded do not know what they make. A
shear knows how to crop a piece of stock into shorter pieces, and a rolling mill knows how to thin a
bar a gauge at a time, but neither names a single product in its C#. What a machine can turn out is
read at world load from a catalogue, and a catalogue is a table any mod can add rows to.

That matters the moment you want your own metal worked by someone else's machine. A machine that
named its outputs in code could only learn about your bronze by being edited: you fork the mod, or
you ship a second machine that does the same job for your own items. Neither survives the other
mod's next release.

exlib does the reading for those machines. At world load it reads every mod's files under one asset
path, merges them into one registry - an in-memory table the game holds for the session - and names
every clash in the log instead of resolving it silently. A product you declare can even have its
item built for you, in your own domain: the part of an asset code before the colon, which is your
mod id.

So extending a machine is one JSON file in your own `assets/` tree. No reference to this library, no
patch against another mod, no C#. The catalogue machinery belongs to exlib rather than to those
mods, so a machine of your own can own a catalogue on the same terms; the last sections show that.

> **Tooling carries its own spec. The machine reads it and names no product.**

## The two shapes

A machine's catalogue takes one of two shapes, and the process decides which. A machine that takes a
piece, does one job to it and hands back a product reads a job table. A machine that walks a piece
down a ladder of states, each state the input to the next, reads a process route.

| Shape | Means | Machines |
|---|---|---|
| **Terminal** | one input, one job, one output - x a count | shear, drill, lathe, shaper, planer, nail and rivet machines, sand casting, the design table |
| **Sequence** | a ladder the work walks, carrying state between steps | the rolling mill and the bending roller, and nothing else |

The count is what keeps a terminal job from being a plain one-to-one recipe. The shear crops one rod
into four rods, so a job that could name only one output could not express the crop table at all.

Both shapes are merged catalogues. Your file and the family's land in the same registry, so adding a
machine and adding a stock family cost the same - neither is a patch against the other.

---

## Terminal: a job table

A job table says what one machine turns each input into. Drop a file at
`assets/<yourdomain>/config/processjobs/<anything>.json`; the loader reads every file under that
path in every domain, so the file name is yours to pick. One file per machine is the convention;
nothing enforces it.

```json
{
  "schema": 1,
  "machine": "shear",
  "jobs": [
    { "input": "yourmod:bronzestrip", "output": "yourmod:bronzerivet", "count": 6 }
  ]
}
```

| Field | Meaning |
|---|---|
| `machine` | which machine these jobs belong to, by the name that machine files its jobs under, such as `shear`. Required |
| `input` / `output` | item codes, domain and all, as the game writes them: `yourmod:bronzestrip`. Required |
| `count` | how many outputs one job yields. Defaults to 1; must be at least 1. On a **staged** job this is the whole piece's yield and one leaves per stroke; on a whole-item job they all leave at once |
| `stage` / `family` | optional, and go together: take a piece part way down a ladder, at that gauge on that branch. Omit both to take the whole item |
| `minTorque` | drive torque the machine needs for this job, from whatever turns it. 0 when the job is not gated on power |
| `minTier` | the hardness tier the machine's fitted tooling must reach for this job. 0 when any tool will do |
| `seconds` | how long one job takes. Defaults to 1 second |

A key the schema does not read is an error, not a silent skip: the loader audits every key in the
file, and one it does not recognise costs you the whole file with a line in the log naming the key.
A typo never half-loads.

A second job on an input another job already claims - the same input at the same stage and branch -
is ignored, and reported in the log when it would have yielded something different. The first
declaration stands. Taking the last writer instead would make the outcome depend on the order the
game happened to load mods in, which nobody can reproduce.

### A staged job crops; a whole-item job converts

Whether you gave the job a `stage` decides what happens to the input, and it is the only thing that
does. Leave `stage` out and the job takes a finished item and turns it into another one. Name a
`stage` and the job takes a piece part way through a rolling mill's ladder - stock at a particular
gauge, on a particular branch, named by `stage` and `family` - and crops pieces off it while the
input itself survives:

```json
{ "input": "yourmod:bronzebar", "stage": 2.0, "family": "grooved",
  "output": "yourmod:bronzerod", "count": 4, "minTorque": 0.3 }
```

| Job | The input | `count` reads as |
|---|---|---|
| **staged** (`stage` present) | **survives**, still stock at the same gauge, with one more crop tallied against it | the whole piece's yield |
| **whole-item** (no `stage`) | **consumed** | what one conversion produces |

Your `count` is your number and nothing checks it against geometry. What a piece divides into is a
design choice, not something exlib can calculate for you. Two things follow from that, and both
matter before you pick a number:

- **The count stays live.** The piece tallies crops *taken*, so raising a `count` from 4 to 6 gives
  every piece already in a player's world the two extra crops rather than stranding it on the old
  number.
- **A part-worked piece cannot re-enter a sequence.** Crop a bar twice and the rolling mill refuses
  it until it is cut out. The tally is against *that stage's* count, so a part piece carried to the
  next stage would be worth the next stage's whole count again. Finish the cut, then roll the pieces
  on.

---

## Sequence: a process route

A route is one stock family's ladder: every gauge a piece of it can be worked to, and which tooling
takes it there. A stock family is the piece itself, `bronzebar` and every state it passes through on
the way to a plate or a rod, rather than the item codes along the way. Drop a file at
`assets/<yourdomain>/config/processroutes/<anything>.json`.

```json
{
  "schema": 1,
  "family": "bronzebar",
  "shape": "yourmod:item/bronze-bar",
  "stages": [
    { "thickness": 2.50, "element": "Grooved250",  "acceptedBy": ["grooved", "flat"] },
    { "thickness": 2.00, "element": "Grooved200",  "acceptedBy": ["grooved"], "code": "yourmod:bronzerod" }
  ]
}
```

`family` is the name the registry merges on. `shape` is the shape file the stages are drawn from.
Each entry in `stages` is one state a piece can be in: `thickness` is its gauge in block-space
units, `element` names the element of that shape file that draws it, and `acceptedBy` lists the
machine families that take it - the tooling a machine can be fitted with, such as grooved or flat
rolls.

`code` present means a stopping point: the piece can be claimed at that gauge, and an item is
generated for it. `code` absent means a render-only intermediate, the same piece at a different
gauge, drawn from the stack's own thickness. One field drives the item catalogue, the machine's
stopping points and the held-item appearance together.

`acceptedBy` is what makes the ladder a graph rather than a line. A stage several families accept is
a fork: the same piece at the same gauge continues one way on grooved rolls and another on flat
ones. That is the mill's whole point, and a line could not express it.

Declare one rung per gap, not one per pass. A gap does cost two trips - in, turned, and back - but
the mill lands the half-step between them by arithmetic, and you do not declare it.
`3.00 -> 2.75 -> 2.50` is the *walk*; `2.50` is the *rung*. Declare the half-steps and the mill will
offer them as gaps of their own, which is a barrel with twice the grooves you meant.

A gauge no rung names is drawn by scaling the family's base shape, so an undeclared half-step still
looks part-worked in the hand.

### Why `config/` and not an item attribute

An itemtype is the JSON declaration the game builds an item from, and the obvious home for a ladder
would be an attribute on the family's own itemtype. It cannot live there. Items are generated from
stopping points, and that has to happen before the game builds itemtypes: you cannot build an
itemtype from data that lives on an itemtype. A file under `config/` is read in time; an attribute
is not.

One consequence follows. A JSON patch - the game's own mechanism for editing another mod's
JSON at load - lands after generation has run, so patching the family's catalogue adds a route but
no item to go with it. To add a stopping point, ship your own file; the merge puts it in the same
family.

The same caveat applies to metals. A JSON patch to `config/metals/` does reach `MetalRegistry` at
`AssetsFinalize`, the load phase that runs once every mod's assets are read and patched, but it
emits no item family: generation (`MetalFamilyEmitter`) runs at `AssetsLoaded` 0.04, before the
patcher. Ship your own `config/metals/` file instead.

---

## What gets built for you

A stage naming a `code` becomes an itemtype automatically, so for most routes the catalogue file is
the only thing you ship.

- The item belongs to your domain, not the framework's: the owning domain is read off the code you
  declared.
- Everything else has a default. A sparse declaration still yields an item that loads and is
  reachable: the family's shape drawn at the stage's `element`, a stack size, a creative-tab entry.
- `"generate": false` says the code already exists. The stage is wired to that item and nothing is
  built - what you want when the route ends at an item you declared yourself.
- The `game:` domain is never built into. Pointing a stage at a vanilla item such as `game:rod-iron`
  wires it up and generates nothing; declare `"generate": false` anyway to say so deliberately and
  keep the line out of the log.

Lang keys are the one part that is not automatic. A lang key is the display name the game looks up
for an item, and an item with no `item-<code>` entry in your `lang/en.json` shows its raw code in
the tooltip instead of a name. Ship your own strings.

A generated code is save data. Once an item exists in someone's world, the rule that produced its
code is frozen for as long as that save lives. That is why you state the code yourself and exlib
never computes one from the family and the gauge.

### Renames are declared, not detected

```json
{ "code": "yourmod:nailplate", "formerCodes": ["yourmod:oldnailplate"] }
```

Rename a stopping point and every stack of the old item already in a player's world points at a code
nothing declares any more. exlib sees only the current catalogue, so a code that vanished and one
that appeared are indistinguishable from a rename unless you say so. Declare the old code in
`formerCodes` and the stacks are rewritten to the new one at server start, with no migration of your
own to write - see [Migrations and Healing](Migrations-and-Healing).

---

## Shapes: both conventions work

`element` is optional, and both ways of writing a stage's model are supported.

A shape file is what the game draws the item from, and the parts inside it are named elements. With
`element` present, the stage is one element of a shared family shape file. That is right for a
progression: thickness falls and length grows across the elements, and a reader can check the whole
relationship at a glance in one file. With `element` absent, the whole shape file is the stage,
which is right for a finished product with a model of its own.

Drawing one element of a file means naming it in `selectiveElements`, and the engine matches those
names by a per-segment prefix rule rather than exactly. Naming an ancestor keeps more than you
intended, and naming an element exactly drops its children. Flat, distinctly-named top-level
elements are safe.

By convention an element drawn off the shared origin is not a stage. It is another machine's output,
and it belongs in that machine's registry.

---

## The C# route

JSON is the primary path, and it needs no reference to this library at all. C# is for a spec you
cannot write down ahead of time: stages derived from a config value the player can change, or jobs
built over whatever another mod turned out to register. Take the dependency and call the same
surface:

```csharp
using ExpandedLib.Catalogues;

// Sequence
ProcessExtensions.Shared.AddStages("bronzebar", [
    new ProcessStage(2.0f, "Bronze200", ["flat"], null),
    new ProcessStage(1.0f, "Bronze100", ["flat"], "yourmod:bronzeplate"),
], shape: "yourmod:item/bronze-bar");

// Terminal
ProcessExtensions.Shared.AddJobs("shear", [
    new ProcessJob("yourmod:strip", "yourmod:rivet", Count: 6, Stage: null, Family: null, MinTorque: 0f),
]);
```

Both return the clashes they hit, empty when the contribution was taken whole, and both throw
`ArgumentException` on a declaration the JSON route would also have refused. The code path builds
the same declaration and runs it through the same parser, so there is one set of rules to learn
rather than two.

This surface is deliberately no wider than the JSON schema. Anything expressible only in C# is a gap
in the schema, and the schema should grow instead -
[open an issue](https://github.com/ringavirda/modding-vsexlib/issues).

Called directly, these are a one-time effect. `AssetsFinalize` clears every registry before
repopulating it, JSON's entries included, so a call you make from `Start` is wiped by the next world
load. To survive that, register through `Contributors` instead, one section down.

## From C#, and surviving the next load

Every catalogue is contributed to rather than owned, and every registry exposes the same seam: a
static `Contributors` property, of type `ExpandedLib.Catalogues.CatalogueContributors`. Register
your contribution once, from your mod system's `Start`, the first phase the game calls. The owning
loader re-runs every registered contributor after its own JSON read, on every `AssetsFinalize`, so a
C# entry outlives the clear that would otherwise erase it.

A metal, with the item it pours as:

```csharp
MetalRegistry.Contributors.Register(api =>
    MetalRegistry.Register(new MetalDef { Code = "hadfield", MoltenItem = "yourmod:ingot-hadfield" }));
```

A medium the pipes and canals can carry:

```csharp
ExLiquids.Contributors.Register(api => ExLiquids.Register(new LiquidDef { Code = "Brine" }));
```

A material role - how machines tell fuel, flux, ore, scrap and charge apart without a hand-written
item list:

```csharp
MaterialRoleRegistry.Contributors.Register(api =>
    MaterialRoleRegistry.Register(new MaterialRoleDef { Role = Roles.Fuel, Code = "yourmod:coke" }));
```

A stage on an existing stock family's ladder:

```csharp
ProcessRouteRegistry.Contributors.Register(api =>
    ProcessExtensions.Shared.AddStages("bronzebar",
        [new ProcessStage(2.0f, "Bronze200", ["flat"], null)]));
```

A terminal job on an existing machine:

```csharp
ProcessJobRegistry.Contributors.Register(api =>
    ProcessExtensions.Shared.AddJobs("shear",
        [new ProcessJob("yourmod:strip", "yourmod:rivet", 6, null, null, 0f)]));
```

How much room one of your items takes in a store's bay:

```csharp
BayOccupancyRegistry.Contributors.Register(api =>
    BayOccupancyRegistry.Shared.Contribute(
        new BayOccupancySet("storagerack", [new BayOccupancy("yourmod:stock-rod", 1)])));
```

A contributor that throws is logged with its target type and skipped. One bad C# contribution never
costs the others theirs, the same guarantee a bad JSON file gets.

## Adding a catalogue of your own

The two rungs above extend catalogues that already exist. The third is owning one: a machine of your
own that other mods can fill the same way, without you writing the loading, the merging and the
reporting again. Derive `ContributedCatalogueLoader<TSet, TRegistry>` once and you get the whole
contract - one asset path read across every domain, an unknown-key audit, a merge that reports its
clashes, and C# contributors that survive a reload. You supply the parse and the merge; the base
supplies everything around them.

```csharp
public sealed class YourCatalogueLoader : ContributedCatalogueLoader<YourEntry, YourRegistry> {
    protected override string AssetPath => "config/yourcatalogue/";
    protected override string CatalogueName => "yourcatalogue";
    protected override IReadOnlyList<string> UnknownKeys(JsonObject root) => ...;
    protected override bool TryParse(JsonObject root, out YourEntry set, out string? error) => ...;
    protected override IReadOnlyList<string> Contribute(YourRegistry registry, YourEntry set) =>
        registry.Contribute(set);
    protected override int CountEntries(YourEntry set) => set.Count;
    protected override CatalogueContributors Contributors(YourRegistry registry) => registry.Contributors;
    protected override void Clear(YourRegistry registry) => registry.Clear();
}
```

The base parses each file exactly once - the same parse counts entries and merges them, so the
report and the registry never disagree - and carries each accepted entry's own source through to any
clash it causes, rather than a file name recovered after the fact. `ProcessRouteLoader`,
`ProcessJobLoader` and `BayOccupancyLoader` are this base with their own schema, and are worth
reading as three worked examples.

## What the log tells you

Whether your file was taken is settled in the log, and nowhere else: a catalogue that refused a file
carries on loading the rest of the world. At `AssetsFinalize` each catalogue's loader result goes to
`CatalogueLoadReport.Log`, which writes one summary line, then one `Error` line per malformed file
or clash:

```
[exlib] processroutes: 3 file(s), 12 entr(ies), 0 error(s)
```

The numbers are files read, entries accepted and errors, and every error names the asset it came
from. Zero errors is not "probably fine"; it is the count of everything the loader turned away, so
it is the one line worth checking after you add a file.

---

## Schema stability

Every spec carries a `schema` number: the version of the declaration form, not of your content.
exlib reads every form that has shipped, so a file you write today keeps loading after the schema
grows a field.

| You declare | Read as |
|---|---|
| nothing | schema 1 - the form that shipped before the field existed |
| an older schema | itself, through the fallback for that form |
| a newer schema | refused, with an error naming both numbers - the file was written for a newer exlib than the one installed |

The alternative, a schema frozen at release, would break your content on exlib's release schedule
instead of on yours.

---

## When something does not appear

| Symptom | Cause |
|---|---|
| the machine refuses your piece | the route names no stage the fitted tooling's family accepts, or that tooling does not take your stock at all |
| your item shows its raw code | the item has no `item-<code>` lang entry; lang keys are never generated, so ship your own strings |
| your patch added a route but no item | JSON patches land after item generation has run; ship your own catalogue file instead of patching the family's |
| your patched metal has no ingot/plate/rod family | metal item generation runs at `AssetsLoaded` 0.04, before the patcher; ship your own `config/metals/` file instead |
| nothing at all, and the log has an `Error` line under `processroutes` | the file was refused; the message names the file and the field that did it |
| your stage was ignored | another file declared that `(thickness, family)` first and the first declaration stands; the log names the clash |
