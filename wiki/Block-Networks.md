# Block Networks

A run of pipe is not one block. It is a line of blocks the player laid one at a time, and the gas
inside it has a single pressure along the whole line: break the run in the middle and each half
keeps a share, join two runs and the two shares become one. Vintage Story knows nothing about
that. It gives you a block, and a block entity - the object the game attaches to one block
position to hold that block's saved state and tick it - and no notion that a line of them is a
thing.

So every mod with pipes in it writes the same graph code. Placement has to find which of the six
neighbouring cells really couple, and turn the block to face them. Pulling a block out of the
middle has to notice that one run is now two, and divide the contents between them. Two runs that
meet have to fold their contents together. A valve that shuts has to sever the run while it is
shut and heal it when it opens. The sharpest edge is the chunk, the cube of world the game loads
and unloads as players move: while part of a run sits in an unloaded chunk those blocks cannot be
read at all, and a graph walk that does not expect that concludes the run is severed and divides
contents that were never separated.

exlib keeps the graph for you. One `ModSystem` - a class the game creates once per side at startup
and keeps for the life of the world - owns every network in the game, of every type. Your block
entity joins it on placement and leaves it on removal. The manager decides which cells couple,
re-walks the graph on every change, and hands each connected run one `BlockNetwork` instance to
carry the state you care about: pressure, fluid, temperature. It calls that instance when two runs
merge, once per fragment when a run fractures, and once a second so the run can simulate. When a
node sits behind an unloaded chunk the manager suspends the decision and re-decides when the chunk
is back, rather than fracturing a run it cannot see.

Using it means writing four small things: a node block that names its network type, its block
entity, a `BlockNetwork` subclass with merge, split and tick, and one line registering a factory
for the type. If your blocks carry gas, water or molten metal, that work is already done: the
industry layer registers `PipeNetwork` as `"pipe"`, `MoltenNetwork` as `"molten"` and
`MpEnergyNetwork` as `"mpenergy"` when it starts, so a node block on any of the three registers
nothing of its own. Iron Industry Expanded and Steel Industry Expanded run on those three. You
register a type of your own the same way.

> **A block does not have to spend its base class on being a node.** C# gives a class one base
> class, and a block usually has somewhere better to spend it. Form, process and membership are
> three independent axes: the base class belongs to **form** (what the block *is* - a container, a
> multiblock part), while network membership and the production tick are **behaviours**, small
> objects attached to a block entity that the game initialises, saves and ticks along with it.
> `BlockNetworkNode` below is the convenient answer when a block has no other form to be, and it is
> what the shipped pipes use. A block that must also be something else, say a container or a
> filler mega-block, hosts `BEBehaviorNetworkMember` and keeps its base.
> `BlockEntityNetworkNode` is itself only a host for that behaviour, so both routes run the same
> code. `samples/TwinTubBlower` shows both at once: the block itself is a `BlockPipe` (so a
> `BlockNetworkNode`), but the fillers reserved by its footprint are plain `BlockStructureFiller`
> cells, and it is those that host a mechanical-power port and a pipe membership as behaviours
> rather than by inheriting from here. See [Production Machines](Production-Machines) for the same
> split on the process axis.

The model (`BlockNetwork` and every `I*Node`/`I*Connector` interface - no Vintage Story block types
involved) and the engine-facing shell (`BlockNetworkNode`, `BlockEntityNetworkNode`,
`BlockNetworkModSystem`) share one namespace, **`ExpandedLib.Networks`**, so a network of your own
needs one import. **`ExpandedLib.Industry.Pipes`** / **`ExpandedLib.Industry.Molten`** hold
the shipped `PipeNetwork`/`PipeNetworkState` and `MoltenNetwork` themselves, alongside their own node
blocks and block entities.

Network tunables (litres per pipe, leak/evaporation rates, over-pressure grace, molten flow rate
and minimum) are exlib's config, not yours: they live in `ExlibValues`, the `exlib` section of
`ModConfig/ex_values.json`, editable in a running world with `/exmod config exlib ...`, because the
network code that reads them ships here. Numbers that belong to your content - a pipe's burst
pressure, a chimney's draw rate, a molten cooldown - stay in your own mod's config.

## The pieces

| Piece | Type | Role |
| --- | --- | --- |
| `BlockNetworkNode` | abstract `Block` | The block a pipe of yours derives from: it works out its own orientation from the network blocks around it at placement and reports which faces carry a connector. |
| `BlockEntityNetworkNode` | abstract `BlockEntity` | The block entity that goes with it: joins the graph when the block loads, leaves on removal, saves orientation and network state, and hands you each network update. |
| `BlockNetwork` | abstract class | Your simulation, one instance per connected run. It owns a typed `State` object and is told when to merge, split and tick. |
| `BlockNetworkModSystem` | `ModSystem` | The manager, one for the whole game. It keeps the graph, walks it to find fractures, and dispatches the tick. You call it to register your network type, and rarely otherwise. |
| `INetworkNode` / `INetworkConnector` | interfaces | What a block entity implements to be a graph node, and what a block implements to be something a pipe connects to without joining the graph. |

The shape of it: **blocks** expose connector faces, **block entities** are the graph's nodes, and the
**manager** hands each connected component one **`BlockNetwork`**. Of the five, you write two.

## Registering a network type

A network type is just a string, and the manager has to know how to build a network for it. That is
what a factory is: a function the manager calls to make a fresh instance, every time a node is
placed with no run beside it to join.

`IndustryModule` registers `pipe`, `molten` and `mpenergy` with their defaults in its own `Start`,
so a mod placing `BlockNetworkNode`s on any of the three registers nothing itself. A mod that needs
a strategy the default does not carry - a gas-vent, a medium taxonomy - re-registers the type in
its own `Start`, which runs after the host's; `RegisterNetworkType` keeps the later factory and
returns `true` to say so:

```csharp
public override void Start(ICoreAPI api)
{
    var networks = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    // iiex's pipe: the vent strategy draws gas through a chimney-ventable fitting, so the pipe
    // network needs iiex's own factory in place of Industry's default.
    networks.RegisterNetworkType(
        "pipe",
        () => new PipeNetwork(networks, new ChimneyVent(() => IiexValues.ChimneyGasDrawRate))
    );
}
```

The manager creates one network per connected component and calls into it as the topology
and clock advance. The factory runs once **per** network instance, so anything the network needs
per-run (e.g. the chimney-vent's sound-throttle state) can be created fresh in the factory.

Forget the registration and the failure is quiet in game and loud in the log: the block places
normally and joins no network, and the server log names the block, the type it asked for and the
types that are registered.

### Concrete-network seams

exlib ships the pipe and molten networks; your mod ships the blocks that run on them. The network
needs answers only your content has - how much metal is in this cell, what pressure this pipe
bursts at - and it cannot name your types to ask for them. It asks through small interfaces
instead. Implement the one you need and the shipped network finds it:

| Seam | Implemented by | Role |
| --- | --- | --- |
| `IMoltenCell` | the canal block entities | The metal in one cell (amount, type, temperature) plus the flags the flow driver reads: `IsFlowSource` marks a cell metal comes out of, `AcceptsSubMinimumFlow` lets a cell take a trickle below the normal minimum. |
| `IBurstablePipe` | `BlockPipe` | `{ CanBurst, BurstPressure }`. The network walks the whole run for the weakest rating, so one cheap segment sets the pressure the run bursts at. |
| `IPipeVentStrategy` | `ChimneyVent` | Optional. Says which fittings vent gas out of the run and how fast they draw, so a chimney relieves pressure instead of the run leaking. Injected at `RegisterNetworkType`, as the example above does. |
| `INetworkNode.OnLeak` | `BlockEntityPipe` (override) | Called on a node at the open end of a pressurised or flooded run. Override it for particles and sound; the default does nothing. |

## Defining a node block

A node block names a network type and little else. Which way the model points, which faces carry a
connector, what the wrench does: `BlockNetworkNode` works all of that out from the block's own
[code-first definition](Code-First-Definitions).

It reads the variant groups to do it. A variant group is the game's way of getting many blocks out
of one definition: a group named `orientation` with the values `ns`, `we` and `ud` yields three
blocks, one per axis, and the game chooses between them by code. `BlockNetworkNode` reads those
same groups for its orientation table, so the `type` x `orientation` map exists in one place, in
code, instead of being written a second time by hand.

```csharp
[BlockRegister]
public partial class MyPipe : BlockNetworkNode, IExBlockDefProvider
{
    public override string NetworkType => "pipe";

    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
        ExBlockDef.Create(domain, "mypipe")
            .Class<MyPipe>()
            .VariantGroup("type", "straight")
            .VariantGroup("orientation", "ns", "we", "ud"),
    ];
}
```

**Only `NetworkType` is required.** `AllowedOrientations` is `virtual` with a definitions-derived
default (`ExDefinitions.OrientationMap`), so overriding it duplicates the variant groups and is worth
doing only for a block whose orientation is not a `type` x `orientation` pair. Its companion
`GetFallbackOrientation` is `protected virtual` - override it `protected`, not `public`, or the
compiler rejects the widening (CS0507).

`BlockNetworkNode` does the heavy lifting from there, and all of it is player-facing. At placement
(`TryPlaceBlock`) it works out which orientations are valid in that cell and prefers the ones
facing the surface the player clicked, falling back to the direction the player is looking, so
laying a line of pipe never needs turning a piece by hand; when no orientation fits it refuses the
placement with the `exlib-noorientation` failure code. It recalculates on a neighbour change
(`OnNeighbourBlockChange`), and breaks the block when nothing is left to hold it - no connected
network neighbour and no solid face to attach to. It rotates the collision and selection boxes to
match, drops the canonical fallback-orientation item rather than whichever variant was in the
ground, and answers the wrench (`IWrenchOrientable`) so a player can cycle a piece through its
valid orientations in place.

### Orientation convention

`Orientation` is the concatenation of single-character face codes the node connects on -
`"ns"` (north+south), `"we"`, `"ud"` (up+down), `"nswe"` (all four horizontals). The variant
group that drives placement is **`orientation`** (singular); a mis-named variant group silently
breaks placement and wrenching, so keep it exactly that.

### Key `BlockNetworkNode` members to know

You write the first of these; the rest have defaults, and the comments say what you would be
replacing.

```csharp
public abstract string NetworkType { get; }              // the only member you must write

public virtual Dictionary<string, string[]> AllowedOrientations { get; }   // derived from your defs
protected virtual string GetFallbackOrientation(string? type);             // first state, else "ns"

public virtual bool IsNetworkEndPoint { get; }          // fixed endpoint, excluded from neighbour discovery
protected virtual bool IsFullCube { get; }              // full-cell blocks cycle the full topology on wrench
protected virtual bool CanWrenchRotate(IWorldAccessor world, BlockPos pos);

public virtual bool HasConnectorAt(BlockFacing face);   // true if Orientation contains the face code
public virtual BlockFacing[]? GetConnectorFaces();      // all connector faces, or null if unorientated
public virtual bool IsValidNonNetworkConnection(Block neighborBlock, BlockFacing face);  // suppress false leaks

protected virtual void GetRotations(string orientation, out float rotX, out float rotY, out float rotZ);
public virtual void RecalculateAndSyncOrientations(IWorldAccessor world, BlockPos pos);
```

Override `GetRotations` only for custom orientation alphabets; override
`IsValidNonNetworkConnection` to tell `GetOpenConnectorFaces` that an adjacent non-network block (a
machine housing, say) seals the face rather than leaving it open. Nothing in the suite overrides it:
an open face only becomes a leak when the neighbour is air, so a face against a solid block is
already quiet. Overriding it changes which faces are open for every consumer of that set, not just
the leak count.

## Defining the node block entity

The block entity is what actually joins the graph, and for a plain pipe it is this small:

```csharp
[BlockEntityRegister]
public class BlockEntityPipe : BlockEntityNetworkNode
{
    public override string NetworkType { get; set; } = "pipe";
}
```

`BlockEntityNetworkNode` registers the node in `Initialize`, unregisters in `OnBlockRemoved`,
and persists `networkType` / `orientation` / `possibleOrientations` / network state. You usually
override only the state-serialization hooks and the dynamic-connectivity hook:

```csharp
public override string NetworkType { get; set; }
public virtual bool HasConnectorAt(BlockFacing face);
public virtual void OnNetworkUpdate(object? state);     // network pushes its latest state here (client display)
public virtual bool IsConnectionBroken();               // return true to dynamically sever the graph here
public virtual void OnOpenConnectorsChanged(BlockFacing[] openFaces);  // open faces (leaks) changed

// State persistence hooks - override these to round-trip your typed network state:
protected virtual bool IsNetworkStateMeaningful(object? state);
protected virtual object? DeserializeNetworkState(ITreeAttribute tree);
protected virtual void SerializeNetworkState(ITreeAttribute tree, object? state);
```

`OnNetworkUpdate` arrives on every node of the run each time the network broadcasts, and is where a
client-side display reads the pressure or the contents. The three serialization hooks round-trip
your state object through an `ITreeAttribute`, the game's save container: a nested bag of keyed
values a block entity writes on save and reads back on load. A node keeps the last update it was
sent so a run's state survives a reload even if no node has ticked since;
`IsNetworkStateMeaningful` decides which updates are worth keeping, so an empty pipe does not hold
a stale reading.

> **Dynamic severing.** A node that can cut the network (a closed valve) overrides
> `IsConnectionBroken()` to return `true` while closed. The graph re-walks on every state change,
> so toggling it merges or fractures the network live. Restore the broken/closed flag in
> `FromTreeAttributes` **before** `Initialize` runs so `AddNode` sees the right state.

## Writing a `BlockNetwork`

Your network subclass is where the gameplay lives. The manager only moves positions between graphs;
what a run holds, and what that does to the world, is yours. One instance exists per connected run,
and instances are created, merged, split and dropped as players build. The manager calls these:

```csharp
public abstract string NetworkType { get; }
public HashSet<BlockPos> Nodes { get; }                 // every position in this network
public BlockPos? RootPos { get; set; }                  // for root-anchored networks; else null
public object? State { get; protected set; }            // your typed state object

public virtual void RestoreState(object? state);        // injected on world load - cast to your type
public void BroadcastUpdate(IBlockAccessor world);      // push State to every node's OnNetworkUpdate

public virtual bool CanMerge(BlockNetwork other, IBlockAccessor world);   // veto a merge (default: allow)
public abstract void OnMerge(BlockNetwork other, IBlockAccessor world);   // combine state on merge
public abstract void OnSplitFragment(BlockNetwork original, IBlockAccessor world);  // split state after fracture
public abstract void OnTick(IBlockAccessor world, float dt, BlockNetworkModSystem manager);  // per-tick sim
public virtual void InheritStateFrom(BlockNetwork source);   // preserve state across rebuilds
public virtual void OnTopologyChanged();                // Nodes changed - drop caches here

protected virtual void OnBeforeBroadcast(IBlockAccessor world);  // update derived state before broadcast
protected virtual object? GetStatePayload();            // the object actually sent in a broadcast
```

`OnTick` runs once a second, server-side, for every live network, with `dt` capped at two seconds so
a stalled server cannot hand you a huge step. `RestoreState` runs on world load: node block entities
come back before any tick, and the first one to load hands its saved state to the network the
manager has just built for that run.

Three of these carry a contract, and it is the same contract every time state is conserved (fluid,
gas, charge): the topology changes constantly, and each change is a chance to create or destroy
what the run holds.

- **`OnMerge`** - fold `other`'s state into this network (sum volumes, average temperature...).
- **`OnSplitFragment`** - when a fracture produces a new fragment, distribute the original's
  state into it (usually proportional to node count). Clamp to physical ceilings: over-pressure
  is legitimate up to a burst limit, so cap at the burst ceiling, not at nominal capacity, or
  every re-walk dumps the run.
- **`OnTopologyChanged`** - invalidate any cached aggregates after the node set changes.

## Manager API (`BlockNetworkModSystem`)

Most mods touch the manager twice: once to register a network type, and once from a machine that
wants to read the run next to it. The rest is here for the cases that need it.

```csharp
public IServerWorldAccessor? ServerWorld { get; }       // non-null on server during tick
public IEnumerable<BlockNetwork> AllNetworks { get; }   // every live network (server-side)

public void RegisterNetworkType(string networkType, Func<BlockNetwork> factory);
public BlockNetwork? GetNetworkAt(BlockPos pos);
public BlockNetwork? GetConnectedNetworkAcross(IBlockAccessor world, BlockPos connectorPos, BlockFacing connectorFace);

public virtual void AddNode(IBlockAccessor world, BlockPos pos, string networkType, bool broadcast = true);
public virtual void RemoveNode(IBlockAccessor world, BlockPos pos, bool broadcast = true);
public BlockNetwork? RebuildFromRoot(IBlockAccessor world, BlockPos rootPos, string networkType, bool broadcast = true);

public BlockFacing[] GetOpenConnectorFaces(IBlockAccessor world, BlockPos pos, INetworkMember member);
public IEnumerable<BlockPos> GetConnectedNeighbors(IBlockAccessor world, BlockPos pos, string networkType);

public static BlockFacing? SideToFace(string? side);
public static bool IsCompatibleNetworkBlock(Block neighbour, string id);
public static bool IsCompatibleNetworkBlockAt(IBlockAccessor world, BlockPos pos, Block neighbour, string id);
```

`AddNode` and `RemoveNode` are called for you by the membership behaviour, so both routes into the
graph - deriving from `BlockEntityNetworkNode`, or hosting `BEBehaviorNetworkMember` - are
automatic. **A block entity that does neither** and joins the graph on its own terms must call
`AddNode`/`RemoveNode` itself.

The membership deregisters on removal only. A chunk unload is not a removal: the node stays in the
graph, the block entity re-adopts its position when the chunk loads again, and a run that is
half-unloaded is never fractured for it.

`RebuildFromRoot` is the odd one out. A root-anchored network exists only as far as it reaches from
one block - a machine's own plumbing, say - and rebuilding it walks out from that root, replaces
whatever networks overlapped it, and carries the old root network's state into the new one through
`InheritStateFrom`. A network with no such anchor leaves `RootPos` null and never needs it.

## Connectors vs. nodes

A pipe run has to reach machines, and a machine must not become part of the run: a boiler is not a
length of pipe and must not hold the run's steam. Two interfaces keep the roles apart:

```csharp
public interface INetworkNode            // implemented by the block ENTITY (a graph node)
{
    string? Orientation { get; }
    string[] PossibleOrientations { get; }
    string NetworkType { get; }
    bool HasConnectorAt(BlockFacing face);
    void OnOpenConnectorsChanged(BlockFacing[] openFaces);
    void OnLeak(BlockFacing[] leakingFaces, bool isLiquid, float intensity);  // leak feedback; default no-op
    void OnNetworkUpdate(object? state);
}

public interface INetworkConnector       // implemented by the BLOCK (anything a pipe can touch)
{
    string NetworkType { get; }
    bool HasConnectorAt(BlockFacing face);
    string NetworkTypeAt(IBlockAccessor world, BlockPos pos);
    bool HasConnectorAt(IBlockAccessor world, BlockPos pos, BlockFacing face);
}
```

A **node** is a full graph participant. A **connector** is anything a pipe will connect to,
including fixed machine ports that are *not* nodes - a boiler's steam outlet, an engine's intake.
Such ports read/write the network in the cell on the **far side** of their connector face; see
[Production Machines](Production-Machines) for the `MachinePorts` helpers that do exactly that.

Both are `INetworkMember` underneath, which is what the graph walk actually asks. `NetworkMembership`
resolves a cell to its member: a membership behaviour on the block entity answers first, then the
block itself. So a cell joins a network by what it declares, never by what class it is.

## Visualising networks

A graph is invisible in game, which makes "why are these two runs not one network" hard to answer by
looking. `NetworkHighlightModSystem` renders every live network in its own transparent colour,
toggled per player with `.exmod network hi` / `.exmod network unhi`, so a run that did not merge
shows up as a second colour. The graph is server-only, so this is a client->server request plus
server-side `HighlightBlocks`, polled every 250 ms for live updates. See [Commands](Commands).

## Related pages

- [Production Machines](Production-Machines) - fixed machines that tap a network through a port.
- [Multiblock Structures](Multiblock-Structures) - big machines that are also nodes.
- [Testing Harness](Testing-Harness) - `StubNetwork` / `TestNetworkBlock` exercise the graph headlessly.
