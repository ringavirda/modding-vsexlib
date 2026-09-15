# Migrations & Healing

A placed block is stored in a save by its code - the `domain:path` string, like `iiex:pipe-iron-ns`,
that names the block your mod registered. Rename that block, move it to another domain or drop it,
and every world that holds it keeps codes that resolve to nothing: a placeholder where the machine
was, and stale stacks in chests and inventories. Players do not rebuild their base because you
tidied up a code, so a mod that ships more than once needs a way to rewrite the past.

An old save goes wrong a second way. A block entity - the object the game keeps beside a placed
block to hold its state and run its code - can be lost while the block survives, when loading its
saved data throws or a client and server fall out of step. The player sees a door that will not open
or a machine that cannot be used, often not even broken, because the thing that answers interactions
is gone.

exlib carries one server-side system for each. `BlockMigrationModSystem` rewrites old codes into
current ones, in the world and in inventories. `BlockEntityHealModSystem` recreates block entities
that went missing under blocks that did not. Both are `ModSystem`s, the game's entry-point class a
mod hangs code on, and both load themselves: there is nothing to register and no call to make at
startup. What you write is one small class per change: implement `IBlockCodeMigration`, or one of
the three interfaces beside it, anywhere in your assembly with a public parameterless constructor,
and exlib finds it by reflection and applies it as the world streams in. Healing you get for
nothing.

## Block migrations

`BlockMigrationModSystem` collects every `IBlockCodeMigration` and `IBlockRemoval` implementation
into one table and applies it as chunk columns load - a chunk column being the stack of blocks the
server streams in and out as players move. It rewrites matching stacks in container block entities
and in the inventory a joining player carries, so a migrated block does not survive as a dead item
in a chest. It matches on `Block.Code` rather than on the numeric id a world assigns, so renumbered
ids and missing-block placeholders are both handled.

Renames chain: declare one this version and another next version, and the table follows the hops to
the code that exists now, stopping if a declaration loops back on itself.

### Rename / re-variant a block

The common case: the block still exists, under a different code.

```csharp
public interface IBlockCodeMigration
{
    string Name { get; }   // short label for log output
    IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(ICoreServerAPI api);
}
```

```csharp
public class PipeRenameMigration : IBlockCodeMigration
{
    public string Name => "pipe rename 0.6";

    public IEnumerable<(AssetLocation, AssetLocation)> GetRemaps(ICoreServerAPI api)
    {
        yield return (new("iiex:pipe-iron-ns"), new("iiex:pipe-straight-iron-ns"));
        // Return the full set unconditionally - pairs whose old or new code is absent in this
        // world are skipped, so a superset is safe.
    }
}
```

Listing every variant by hand gets long: three materials and six orientations is eighteen pairs.
`CodeRelocation` writes them when the block kept its shape and only changed identity, pairing each
live block whose code starts with the new base against the old base plus the same suffix:

```csharp
public IEnumerable<(AssetLocation, AssetLocation)> GetRemaps(ICoreServerAPI api) =>
    CodeRelocation.Remap(api, "ppex", "iiex", "pipe-straight");
```

`Remap` takes an old and a new base code when the name changed too, and `RemapToDefault` maps a
single historical code with no variants onto a full code you name.

### Migrate block-entity state too

A plain remap swaps the block id and lets the new block entity start empty, which loses an
inventory or a smelt in progress. If the renamed block carried state worth preserving, implement
`IBlockEntityMigration` on the **same class**. It runs after the new block is placed, with the old
BE's serialized tree:

```csharp
public interface IBlockEntityMigration
{
    void MigrateBlockEntity(AssetLocation oldCode, AssetLocation newCode,
        ITreeAttribute? oldState, BlockEntity newBlockEntity, IWorldAccessor world);
}
```

Mutate `newBlockEntity` directly - it is marked dirty for you, and
`newBlockEntity.FromTreeAttributes(oldState, world)` copies the old state verbatim when its shape
did not change. `oldState` is `null` if the old block had no BE.

### Remove a block entirely

When a block is gone for good with no successor, declare it removed rather than leaving it to
resolve into nothing.

```csharp
public interface IBlockRemoval
{
    string Name { get; }
    IEnumerable<AssetLocation> GetRemovals(ICoreServerAPI api);   // full domain-qualified codes to delete
}
```

Removals delete the block in place **and** strip matching items from containers and player
inventories. You can read config to decide what to purge - the table is built once at startup.

### Rename / move a (non-block) item

Items are never placed in the world, so they only live in inventories, containers and ground
storage. Implement `IItemCodeMigration` to rewrite their stacks there:

```csharp
public interface IItemCodeMigration
{
    string Name { get; }
    IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(ICoreServerAPI api);
}
```

`BlockMigrationModSystem` discovers these alongside the block migrations and keeps a separate item
table. When a held stack matches, the replacement is chosen by the stack's class - so a code that
exists as **both** a block and an item (e.g. `slag`) migrates each independently and never turns one
into the other.

## Block-entity healing

`BlockEntityHealModSystem` repairs the second failure above: a block that is still placed but lost
its BE after a deserialization failure or desync. Symptom: a door (or machine) that is
un-interactable and unbreakable because the controlling BE is gone. The healer recreates a fresh
BE as chunk columns load, making the block functional again.

```csharp
public class BlockEntityHealModSystem : ModSystem
{
    public int HealLoadedChunks();                                  // sweep all loaded chunks; returns count healed
    public bool HealOrphanAt(IBlockAccessor ba, BlockPos pos);      // heal a single position; returns true if healed
}
```

It runs automatically (spawn-chunk sweep at `RunGame`, then on every `ChunkColumnLoaded`), and the
**`/exmod heal`** command or the two methods above run a pass on demand. Its scope is restricted
to
types carrying `[BlockEntityRegister]`, so it never touches vanilla or foreign BEs. Recreated BEs
start from default state; multiblock anchors re-detect their structure on the next monitor tick.

> A healed BE starts empty, so anything you can't reconstruct from the block alone (an inventory)
> is lost; healing restores *function*, not prior contents.

## Side-band data

A migration that must run once, a per-world counter, a marker on a chunk: state like this belongs to
neither a block entity's tree nor a ModConfig file. Two thin wrappers give it a home, each key
prefixed with a domain so two mods' unqualified keys never collide:

```csharp
public static class ExWorldData
{
    public static T Get<T>(ICoreServerAPI api, string domain, string key, T defaultValue = default!);
    public static void Set<T>(ICoreServerAPI api, string domain, string key, T value);
    public static void OnSave(ICoreServerAPI api, Action action);   // subscribes to Event.GameWorldSave
}

public static class ExChunkData
{
    public static T Get<T>(IWorldChunk chunk, string domain, string key, T defaultValue = default!);
    public static void Set<T>(IWorldChunk chunk, string domain, string key, T value);
}
```

`ExWorldData` wraps `ISaveGame.GetData`/`StoreData`; `ExChunkData` wraps
`IWorldChunk.GetModdata`/`SetModdata`. Write from `OnSave` or before the chunk is sent, not on every
change.

## The chunk-column sweep's completion marker

`ChunkColumnSweeperModSystem` is the walk both systems above are built on, and takes a subclass of
your own: you supply the work table and the per-cell action, it supplies the startup sweep and the
per-column pass. It sweeps every loaded column on every world load, which is what a migrator wants;
a subclass whose work only needs doing once per column overrides `Version`:

```csharp
protected override string? Version => "1";
```

Once set, a column that already carries this sweeper's marker for that version - stamped through
`ExChunkData`, keyed by the sweeper's type name and the mod id - is skipped; bumping `Version`
writes a new marker key, under which no column is marked yet, so every column is swept once more.
The old version's marker is never removed and stays on every chunk for the life of the save.
Leaving `Version` null or empty (the default) keeps today's behaviour exactly: every column, every
load.

## Related pages

- [Registries](Registries) - `[BlockEntityRegister]` is what scopes the healer.
- [Commands](Commands) - `/exmod heal`.
- [Lifecycle](Lifecycle) - when the startup sweep and the per-column pass run during world load.
