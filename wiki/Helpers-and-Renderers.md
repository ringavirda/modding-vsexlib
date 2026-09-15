# Helpers & Renderers

Writing a machine block for Vintage Story, you meet the same small problems every time. The block
can be placed facing any of four directions, so every port, collision box and particle has to be
turned to match. It draws a custom mesh, which must not be rebuilt each frame and must not leak the
copy it uploaded to the GPU. Its code runs twice, once on the server and once on the client, and
half of what it does belongs to one side only. It reads a click, counts the player's items, writes a
tooltip line, plays a sound. None of that is what your machine is about, and all of it is fiddly
enough to get subtly wrong.

This page is the drawer of small parts that answer those, pulled out of the family mods once each
had written its own. They are static classes and extension methods: nothing to derive from, nothing
to register. Add `using ExpandedLib.Helpers;` and call them. Everything here ships in the base
`ExpandedLib` package except `ExParticles` and `ExSounds`, which live in
`ExpandedLib.Industry.Helpers` and need the [industry package](Installing) referenced too.

## `ExOrientation` - rotation math

A block that can face four ways is usually one definition with a `side` variant group - a variant
group being the game's way of generating a family of blocks from one definition, here
`yourmod:pipe-north`, `-east`, `-south`, `-west`. A placed block reads `Variant["side"]` to learn
which one it is, and everything it does in world space then has to be turned by the same amount:
where its output port is, where its collision boxes sit, which face a particle leaves from. Written
by hand that is a `switch` on the side name in a dozen places, and one of the dozen is always
rotated the wrong way. `ExOrientation` turns the side name into an angle once and rotates offsets,
boxes and facings by it.

```csharp
int angle = ExOrientation.AngleFromSide(Variant["side"]);
BlockPos port = ExOrientation.GlobalPos(Pos, 0, 0, 1, angle);   // one step "north" in the block's own frame
```

```csharp
public static class ExOrientation
{
    public static int AngleFromSide(string? side);                 // north 0, west 90, south 180, east 270
    public static Vec3i RotateOffset(Vec3i off, int angle);        // rotate a structure-local offset (Y untouched)
    public static Vec3i RotateOffset(int x, int y, int z, int angle);
    public static BlockPos GlobalPos(BlockPos origin, int localX, int localY, int localZ, int angle);
    public static Vec3i ReadOffset(JsonObject? node, Vec3i fallback);     // read { x, y, z } from JSON
    public static Vec3d ReadOffsetD(JsonObject? node, Vec3d fallback);    // double-precision (particle anchors)
    public static BlockPos WorldPosFromAttr(BlockPos origin, JsonObject? node, Vec3i fallback, int angle);
    public static Cuboidf[] RotateBoxes(Cuboidf[] boxes, int angle);     // rotate collision/selection boxes around centre
    public static BlockFacing RotateFacing(BlockFacing baseFace, int angle);
    public static void RotateAroundCenter(ref float x, ref float z, int angle, float center = 0.5f);
}
```

`AngleFromSide` is where almost every use starts. `WorldPosFromAttr` saves the most: a block's JSON
attributes name a position in the block's own frame (the cell its power port sits in, say), and this
reads it, rotates it and adds it to the origin in one call - exactly
`origin + RotateOffset(ReadOffset(node), angle)`.

`RotateBoxes` returns copies rather than rotating in place (and returns the input unchanged for
angle 0), so cache what it gives you in a field rather than recomputing it each frame.
`AllowedOrientations`-style lookups should likewise be cached props.

`ExOrientation.SegmentedCode` is for the rarer job of rewriting a code rather than a position. It
splits a block code's path on `-` so a caller can index, rewrite and rejoin one dash-segment at a
time: that is how a multiblock layout finds which segments of a code name an orientation, and
rotates just those to a placed structure's angle while leaving the rest of the code alone.

## `ExMeshCache` - mesh and mesh-ref cache

A block entity that draws something the blocktype's own shape cannot - a barrel with a visible fill
line, a mold with metal in it - builds its own `MeshData` and uploads it to the GPU as a
`MultiTextureMeshRef`. Both are expensive to build, so both must be cached rather than rebuilt per
frame, and the uploaded ref must be disposed or the video memory is lost. Left alone, every such
block grows its own dictionary and its own disposal loop, and the disposal loop is the part people
forget. `ExMeshCache` is that dictionary written once. `GetOrCreate` caches a plain `MeshData`;
`GetOrCreateRef(capi, group, key, build)` caches the uploaded ref, so the first call for a key
uploads and later calls reuse. Refs are grouped, so `DisposeGroup(api, group)` disposes every ref a
blocktype ever uploaded in one call from `OnUnloaded`. Build the key from everything that changes
the mesh, or two states render as one.

```csharp
renderinfo.ModelRef = ExMeshCache.GetOrCreateRef(
  capi, "moltenBarrelMeshRefs:" + Code, key, () => GenMeshWithContent(capi, metal, fillRatio, glow)
);
```

## `ExHighlightSlots` - highlight slot ids

`world.HighlightBlocks` draws a coloured outline over a set of positions - a multiblock's build
outline, a network run traced from a wrench. It takes a slot id, an integer you choose, and a second
call with the same id replaces the first call's highlight. Pick literals by hand and sooner or later
two features pick the same one, each erasing the other with no error to explain it. `Reserve(key)`
hands out an id per key instead: the same id for the same key every time, a fresh one for a new key.
Reserve it once into a static field and nothing else can take it.

```csharp
private static readonly int HighlightSlotId = ExHighlightSlots.Reserve("yourmod:network-highlight");
```

## `ExParticles` - particle catalogue

The game spawns particles from a `SimpleParticleProperties` object you fill in field by field:
colour, a position box, a velocity range, quantity, lifetime, gravity, size, and how opacity and size
evolve over a particle's life. Steam that looks like steam rather than like snow means getting every
one of them right, with no reference to copy from. `ExParticles` is the family's answers as named
presets and one-line effects: `ChimneySmoke` or `WaterJet` for the effect the family mods use,
`Spawn` for the full set of knobs without assembling the object. It ships in `ExpandedLib.Industry`.

```csharp
ExParticles.WaterJet(Api.World, Pos, outFace);
```

```csharp
public static class ExParticles
{
    // Colour presets (int ARGB):
    public static readonly int Vapor, Exhaust, GasLeakTint, Water, Smoke, GlowSpark, Dust, AirTint;

    public static void Spawn(IWorldAccessor world, int color, Vec3d minPos, Vec3d maxPos,
        Vec3f minVelocity, Vec3f maxVelocity, float minQuantity, float maxQuantity,
        float lifeLength, float gravityEffect, float minSize, float maxSize,
        EnumParticleModel model = EnumParticleModel.Quad,
        EvolvingNatFloat? opacityEvolve = null, EvolvingNatFloat? sizeEvolve = null,
        bool shouldDieInLiquid = false);

    public static Vec3d FaceCenter(BlockPos pos, BlockFacing face);
    public static Vec3f OutVel(BlockFacing face, float speed, float spread);
    public static int? GasColor(string gasType, bool ventAir = true);

    // High-level effects:
    public static void RisingPlume(/* box bounds + timing */);
    public static void ChimneySmoke(IWorldAccessor world, BlockPos chimneyPos, string gasType);
    public static void SteamPlume(IWorldAccessor world, BlockPos cell, int count);
    public static void SteamPuff(IWorldAccessor world, Vec3d pos, int count);
    public static void AirInhale(IWorldAccessor world, Vec3d mouth, int count);
    public static void SmokeCloud(IWorldAccessor world, Vec3d pos, int count);
    public static void GasLeak(IWorldAccessor world, BlockPos pos, BlockFacing face, float intensity = 1f);
    public static void GasVent(IWorldAccessor world, BlockPos pos, BlockFacing face, string gasType);
    public static void WaterJet(IWorldAccessor world, BlockPos pos, BlockFacing face, float intensity = 1f);
    public static void WaterSpill(IWorldAccessor world, BlockPos cell);
    public static void FallingDust(IWorldAccessor world, BlockPos pos);
}
```

## `ExSounds` - sound catalogue

Sound has two traps. Finding one: the game ships hundreds of sound assets under paths nobody
remembers, and a mistyped `AssetLocation` plays nothing and reports nothing. And which side plays it:
a sound the server plays reaches every player near the position, a sound the client plays is heard by
that player alone. `ExSounds` answers both, with `AssetLocation` constants for every sound the family
reuses (many of them repurposed vanilla sounds) and play helpers that each state the side they are
for. It ships in `ExpandedLib.Industry`.

```csharp
ExSounds.Play(world.Api, blockSel.Position, ExSounds.Ingot, 0.7f);
```

```csharp
public static class ExSounds
{
    // Constants (AssetLocation), grouped: molten/heat (Sizzle, MoltenMetal, PourMetal, Embers, Fire,
    // Extinguish, Ignite), mechanical (Latch, Bellows, Ingot, AnvilHit, Build, StoneCrush, ToggleSwitch,
    // CokeOvenDoorOpen/Close, ...), fluids/venting (SmallSplash, WaterPour, Watering, ExtinguishHiss),
    // steam ambience (Cooking, Lava, Creek, MetalGrinding, Swoosh, PlanetaryGears, *Explosion, ...).

    public static void Play(ICoreAPI? api, BlockPos pos, AssetLocation sound, float volume = 1f, float range = 24f);  // server only
    public static void PlayThrottled(ICoreAPI? api, BlockPos pos, AssetLocation sound, ref long lastMs, long intervalMs, float volume = 1f, float range = 24f);
    public static void PlayLocal(IWorldAccessor world, BlockPos pos, AssetLocation sound, float volume = 1f, float range = 16f, bool randomizePitch = true);  // client-safe, no side gate
    public static void PlayLoop(IWorldAccessor world, BlockPos pos, AssetLocation sound, ref long lastMs, long intervalMs, float volume = 1f, float range = 16f);  // client-safe
    public static void PlayAt(IWorldAccessor world, BlockPos pos, AssetLocation sound, IPlayer? byPlayer = null, bool randomizePitch = true, float range = 32f, float volume = 1f);
    public static void PlayChance(IWorldAccessor world, BlockPos pos, AssetLocation sound, double chance, bool randomizePitch = true, float range = 32f, float volume = 1f);
    public static ILoadedSound? CreateLoop(ICoreAPI? api, BlockPos pos, AssetLocation sound, float volume = 1f, float range = 16f, float pitch = 1f);  // client only, gapless loop
    public static void SplashSound(IWorldAccessor world, BlockPos pos);   // quiet splash ~30% of the time
    public static void HissSound(IWorldAccessor world, BlockPos pos);     // soft steam/gas hiss ~30% of the time
}
```

`Play` and `PlayThrottled` do nothing unless called on the server; `PlayLocal` and `PlayLoop` carry
no side gate and are the ones for client code. The throttled pair take a `ref long lastMs` you keep
in a field and only play once that many milliseconds have passed, which is what stops a sound called
from a tick handler turning into a buzz. `CreateLoop` instead returns the `ILoadedSound` itself, for
a continuous machine hum you start, stop and dispose by hand.

## `ExInventory` - counting & consuming items

"Does the player have four iron plates, and if so take them" sounds like one question. In the API it
means walking every inventory the player owns - hotbar, backpack, each bag - asking each slot whether
its stack matches, then walking them again to remove the right number. `ExInventory` is that walk,
written once and driven by a predicate you supply.

```csharp
int had = ExInventory.Count(byPlayer, stack => Matches(stack, r.Codes));
```

```csharp
public static class ExInventory
{
    public static int Count(IPlayer player, Func<ItemStack, bool> matches);            // all inventories
    public static int Take(IPlayer player, Func<ItemStack, bool> matches, int quantity);
    public static int CountHotbar(IPlayer player, Func<ItemStack, bool> matches);      // hotbar only
    public static int TakeHotbar(IPlayer player, Func<ItemStack, bool> matches, int quantity);
}
```

`Take` and `TakeHotbar` remove up to `quantity` matching items and return how many were actually
taken, which may be fewer. Check that return: a build cost that assumes it got everything it asked
for hands the player a free machine when they were one plate short. The hotbar-only pair is for a
cost the player should be holding rather than carrying in a bag.

## Finding block entities

A block entity is the object the game keeps beside a block to hold state and to tick - the thing
that remembers how hot your furnace is. Asking for one gives you the base type or null, so every
call site writes the same cast and the same null guard, four times over in a machine that reaches
for four neighbours. `accessor.BlockEntity<T>(pos)` is `GetBlockEntity(pos) is X be` with the null
guard already done:

```csharp
public static class ExBlockAccess
{
    public static T? BlockEntity<T>(this IBlockAccessor accessor, BlockPos pos) where T : class;
    public static bool TryGetBlockEntity<T>(this IBlockAccessor accessor, BlockPos pos, [NotNullWhen(true)] out T? be) where T : class;
    public static T? Neighbour<T>(this IBlockAccessor accessor, BlockPos pos, BlockFacing facing) where T : class;
    public static IEnumerable<(BlockFacing Facing, T Entity)> Neighbours<T>(this IBlockAccessor accessor, BlockPos pos, IEnumerable<BlockFacing>? facings = null) where T : class;
}
```

`T` can be an interface as well as a concrete type, so a port asks for `IMyPort` rather than naming
a block entity class it should not have to know. `Neighbours` walks `BlockFacing.ALLFACES` by
default, or a subset such as `BlockFacing.HORIZONTALS`, and yields only the faces that carry a
match, which makes it a straight substitution for a hand-rolled loop over the facings.

## Which side

The game runs your mod twice. The server owns the world and decides what actually happens; the
client draws it and handles input. Most of a block entity's code executes on both, so "spend the
fuel" has to happen on the server only and "spawn the smoke" on the client only. Skip the guard and
the client changes state it does not own, which the next sync quietly undoes, or the server spawns
particles no player will ever see. `ExSide` decides none of that for you; it just makes the question
short enough that you write it at every branch that needs it:

```csharp
public static class ExSide
{
    public static bool IsServer(this ICoreAPI api);
    public static bool IsClient(this ICoreAPI api);
    public static bool IsServer(this IWorldAccessor world);
    public static bool IsClient(this IWorldAccessor world);
}
```

These read the same `Side` the engine exposes, so `api.IsServer()` is `Api.Side ==
EnumAppSide.Server` and nothing more. There is a world-accessor overload as well, for the handlers
that are handed an `IWorldAccessor` rather than an API.

## Reading a click

An interact handler is handed the world, the player and a `BlockSelection`, and the facts it wants
are spread across all three: what is in the player's active hotbar slot, whether that is a wrench,
whether they were sneaking, which face they hit. Written inline, each guard digs them out again and
each dig needs its own null check. `ExInteraction.Of(world, byPlayer, blockSel)` answers all of it
once, as an `Interaction`:

```csharp
public readonly struct Interaction
{
    public Interaction(IWorldAccessor world, IPlayer player, BlockSelection selection);

    public ItemStack? Held { get; }                 // active hotbar stack, or null
    public CollectibleObject? HeldCollectible { get; }
    public bool HeldIs(EnumTool tool);
    public bool HeldIs(AssetLocation code);         // exact code, or a wildcard with '*'
    public bool HandEmpty { get; }
    public bool Sneaking { get; }                   // the player's sneak control
    public BlockFacing? Face { get; }               // selection.Face
    public bool IsServer { get; }
    public bool IsClient { get; }
}
```

`Interaction` only answers questions; it never decides which side runs a mutation, so the handler
still writes its own `if (interaction.IsServer) { ... }` around whatever the click triggers. A null
player or null selection reads as empty-handed / not-sneaking / no-face rather than throwing.

## Block info lines

`GetBlockInfo` fills the panel a player sees when they look at a block. Every line in it is a
translation key put through `Lang.Get` and appended to a `StringBuilder`, so the method becomes a
column of near-identical `dsc.AppendLine(Lang.Get(...))` calls with the occasional `if` round one.
Lines carrying a number have a second problem: the player picks their display units, so 800 deg C
must become 1472 deg F before it reaches the format string, and the block should not have to know
that. `ExInfo` is three extension methods on `StringBuilder` that fold those into one call each:

```csharp
public static class ExInfo
{
    public static StringBuilder Lang(this StringBuilder dsc, string key, params object[] args);
    public static StringBuilder LangIf(this StringBuilder dsc, bool condition, string key, params object[] args);
    public static StringBuilder Measure(this StringBuilder dsc, string key, float value, string unit);
}
```

`dsc.Lang("iiex:some-key")` is `dsc.AppendLine(Lang.Get("iiex:some-key"))`; `LangIf` skips the line
when the condition is false. `Measure` folds the value through `ExMeasure` in the named unit
(`volume`, `pressure`, `temperature`, `power`, `speed`, `flowrate` or `energy`) before handing it to
`Lang.Get`, so the line reads correctly in whichever measurement system the player has chosen,
metric or imperial, without the block calling `ExMeasure` itself.

## `ExItems`, `ExCreativeTabs`, `ExBlockNames`

Three one-liners for three chores too small for pages of their own. `ExItems.WrenchStacks` feeds
interaction help, the hint that tells a player which tool to hold: one cached stack per registered
wrench across every loaded mod, so the hint stays right when another mod adds one.
`ExCreativeTabs.EnsureTab` adds your mod's own creative-inventory tab, which the API keeps internal;
call it once from `Start`. `ExBlockNames.Decorate` appends the material a block was made from to its
display name as a parenthetical suffix, so one lang key covers every material it ships in, and
`ExBlockNames.AddVariantQualifier` adds a variant group of your own to the ones it recognises.

```csharp
ItemStack[] wrench = ExItems.WrenchStacks(world);        // interaction-help stacks
ExCreativeTabs.EnsureTab(Mod.Info.ModID);                // once, from Start
string name = ExBlockNames.Decorate(this, base.GetHeldItemName(itemStack));
```

```csharp
public static class ExItems
{
    public static ItemStack[] WrenchStacks(IWorldAccessor world);   // one stack per registered wrench, cached, for interaction help
}

public static class ExCreativeTabs
{
    public static void EnsureTab(string tabCode);   // append a custom creative tab (reflection; no-op if internal type absent)
}

public static class ExBlockNames
{
    public static string Decorate(Block block, string baseName);    // append material/rock/brick variant + refractory tier
}
```

## `ExContentGate` - disabling content

Sooner or later a mod ships something a pack does not want: a block that overlaps another mod's, a
tool mold the pack replaces. The obvious fix, not registering it when a config says so, breaks every
world that already has one placed, since a block the save references and the game no longer knows is
a hole in the world. `ExContentGate` leaves it registered, so old saves keep working, and hides it
from the two places a player finds new things: the creative inventory and the handbook. It returns
how many collectibles it matched, which is worth logging - a gate that hides nothing usually means
the code pattern is wrong.

```csharp
ExContentGate.HideFromCreativeAndHandbook(api, c => c.Code?.Path.StartsWith("toolmold-") == true);
```

```csharp
public static class ExContentGate
{
    public static int HideFromCreativeAndHandbook(ICoreAPI api, Func<CollectibleObject, bool> match);
}
```

Call it after content has resolved, from `StartServerSide`/`StartClientSide` rather than `Start`, or
there is nothing registered yet to match against. Pair it with a config toggle, the way `siex` gates
its tool molds behind `/exmod molds`.

## `SurfaceRenderer` - flat fluid surfaces

A tank with a visible water line, a canal with molten metal in it: the fluid is a flat horizontal
quad drawn inside the block at a height that changes as it fills. A blocktype's shape cannot do that,
since the height is state rather than geometry, so it falls to an `IRenderer` registered with the
client - which means shader setup, a vertex buffer and a disposal path before anything is drawn at
all. `SurfaceRenderer`, under `Helpers/Rendering/`, is that scaffolding as a base class: it owns the
quad geometry built from the block's footprint boxes and the standard-shader plumbing, and you
answer four questions - draw this frame or not, at what height, in what tint, with what texture.

```csharp
_renderer = new MoltenRenderer(Pos, capi, boxes, rotationY: 0f, fillStartY: 0f, fillHeightLevels: 16);
api.Event.RegisterRenderer(_renderer, EnumRenderStage.Opaque);
```

```csharp
public abstract class SurfaceRenderer : IRenderer
{
    protected SurfaceRenderer(BlockPos pos, ICoreClientAPI api, Cuboidf[] footprintBoxes, float rotationY, bool combine);

    public abstract double RenderOrder { get; }
    public virtual int RenderRange => 24;

    // Implement:
    protected abstract bool ShouldRender { get; }                     // draw this frame?
    protected abstract float SurfaceY { get; }                        // absolute surface height in block units
    protected abstract void ConfigureShader(IStandardShaderProgram shader, IRenderAPI render);  // tint/glow
    protected abstract bool BindSurfaceTexture(IRenderAPI render);    // bind texture; false to skip drawing

    // Optionally override:
    protected virtual int SelectMeshIndex() => 0;                     // which box-mesh to draw (combine == false)
    protected virtual bool UseBlend => false;                         // alpha blending for translucent surfaces

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage);
    public virtual void Dispose();
}
```

`combine: true` merges all footprint boxes into one always-drawn mesh; `combine: false` keeps one
mesh per box and lets `SelectMeshIndex` pick the cross-section by fill level.

> **Re-init on `OnExchanged`.** `rotationY` is captured at construction. A wrench-rotated block
> renders its fluid surface in the pre-rotation orientation unless you rebuild the renderer in the
> block entity's `OnExchanged`. Surface glow is push-based, so a hot mold/tap/barrel also needs a
> client tick calling its update path or it freezes hot and snaps cold on interaction.

## Related pages

- [Multiblock Structures](Multiblock-Structures) - `ExOrientation` drives `SetStructureAngle`.
- [Config System](Config-System) - back `ExContentGate` toggles with a live config value.
