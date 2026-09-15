# Commands

A chat command is how a player or a server admin reaches into a mod while the game runs: a line
typed into chat that your code answers. The game lets any mod claim any top-level name, and that is
the problem. Install six technical mods and a player has six roots to learn, each spelled to its
author's taste, and the seventh mod that wants `/config` finds the name taken.

exlib claims one root for the whole family and hands it out: **`/exmod`** on the server and
**`.exmod`** on the client. The game keeps a separate command table per side, and by its convention
a leading slash goes to the server while a dot stays in the player's own client. Any mod can hang a
sub-command off that root, not only the family ones. The root is created on demand by whichever mod
registers first, so load order between mods does not matter.

Two of the built-in sub-commands need no code from you at all. Declare a
[manageable config](Config-System) or a [recipe profile](Recipe-Costs) and it shows up under
`/exmod config` or `/exmod recipes`, listed by your mod id. This page covers what ships, how to add
a sub-command of your own, and the shortcut for the shape most of them take.

## The `exmod` root

`ExmodCommand` builds the root itself, on both sides: its `[CommandRegister(Side = Universal)]` tag
is what tells exlib to register it on each. Typed on its own the root prints help, because all the
behaviour lives in sub-commands. exlib ships these:

| Command | Side | What it does |
| --- | --- | --- |
| `/exmod config [<mod> [<value> [<new>]]]` | server | Edit any [manageable config](Config-System) value live. |
| `/exmod recipes [<mod> [<level>]]` | server | Switch a mod's [recipe-cost level](Recipe-Costs). |
| `/exmod verify [<mod>]` | server | Run the [content checks](Checks) - dangling codes, missing lang, pinned network nodes - for one mod or all. |
| `/exmod heal` | server | Sweep loaded chunks and recreate orphaned block entities ([healing](Migrations-and-Healing)). |
| `.exmod network hi` / `.exmod network unhi` | client | Toggle the transparent per-network colour highlight ([block networks](Block-Networks)). |
| `.exmod measure [metric\|imperial]` | client | Switch the display unit system ([preference](Registries)). |

The server root requires the `controlserver` privilege, so an ordinary player on a multiplayer
server cannot reach any of it; the client root requires only `chat`, since it changes nothing
outside the player's own client.

`/exmod config` and `/exmod recipes` are mod-agnostic. They list mods, not commands: any mod that
registers a manageable config or a recipe profile appears in them by its mod id, and there is no
per-mod command code anywhere.

## Adding your own sub-command

A sub-command is a class, not a call you make from somewhere. Implement `IExSubCommand`, tag it
with the side it belongs on, and point `ParentName` at `"exmod"`. exlib finds the class, resolves
the parent and hands it to you to build onto:

```csharp
[SubCommandRegister(Side = EnumAppSide.Server)]
public sealed class StatusSubCommand : IExSubCommand
{
    public string ParentName => "exmod";
    public void Register(ICoreAPI api, Mod mod, IChatCommand parent) =>
        parent.BeginSubCommand("status").HandleWith(args => TextCommandResult.Success("ok")).EndSubCommand();
}
```

Two more calls chain onto `BeginSubCommand` the same way.
`.WithDescription(Lang.Get(mod.Info.ModID + ":command-status-desc"))` is the line a player reads in
the help listing, pulled from your lang file so it translates.
`.RequiresPrivilege(Privilege.controlserver)` gates a sub-command that changes server state, on top
of whatever the parent root already requires.

A client-side sub-command carries `[SubCommandRegister(Side = EnumAppSide.Client)]` instead and
casts `api` to `ICoreClientAPI` for the client-only API. Put it on the client when it changes only
what this one player sees, such as a rendering toggle or a display unit.

Nothing registers itself: something has to scan your assembly for the tagged classes. A mod system
derived from `ExModSystem` already does, in its `StartServerSide` and `StartClientSide`. Keep an
entry point of your own and you make the call yourself, once per side:
`CommandRegistry.RegisterAll(api, Mod, GetType().Assembly)` - see **[Registries](Registries)**.

## Your own /exmod sub-command for a registry

A sub-command that manages a set of things nearly always has the same three moves: list every code,
show one, set something on it. `/exmod config` and `/exmod recipes` are both exactly that, over an
`ExKeyedRegistry` - the framework's string-keyed lookup, one entry per mod. Derive
`RegistrySubCommand<T>` instead of implementing `IExSubCommand` and the three moves, their argument
parsing and their messages come with it; you write only what differs:

```csharp
public sealed class MyThingsSubCommand() : RegistrySubCommand<MyThing>(
    "mythings", "mymod:command-mythings-desc",
    () => MyThings.Codes, code => MyThings.TryGet(code, out var t) ? t : null) {
    protected override string Describe(MyThing t) => t.Code;
    protected override TextCommandResult Set(MyThing t, string[] args) => /* ... */;
    // NoneRegisteredKey, ListHeaderKey, UnknownCodeKey: three more lang-key overrides, same shape.
}
```

`Set` sees the words typed after the code - none of them means "show this entry", so a command that
needs no separate show step can treat that case as the read. `.exmod measure` is not built this way:
it edits one preference chosen at registration, not an entry picked from a registry by code, so it
stays a plain `IExSubCommand` alongside `verify`, `heal` and `network`.

## VTML pitfall in command output

Chat output is rendered as **VTML**, the game's own markup for chat and handbook text. A literal
`<` opens a tag and silently eats the rest of the message, so a usage string like
`Usage: /exmod config <mod>` truncates at `<mod>` and the player sees half a sentence, with no error
anywhere to explain it. Use square brackets instead - `Usage: /exmod config [mod]` - in any
command-result or usage text. Vanilla's `<hk>` is a hotkey-code tag and renders `?` for command
strings, so do not reach for it to wrap a command; `<strong>` is the one you want.

## Related pages

- [Registries](Registries) - `IExCommand` / `IExSubCommand` and `CommandRegistry`.
- [Config System](Config-System) - what `/exmod config` edits.
- [Recipe Costs](Recipe-Costs) - what `/exmod recipes` switches.
