# Installing

This page is for a modder who already has a project, or is about to make one, and wants exlib in
it. It covers what exlib ships, where each piece comes from, and the handful of lines that wire it
into a mod. If you would rather start from a working repository, clone
[exmod-starter](https://github.com/ringavirda/exmod-starter) instead; everything below is already
done there.

## What ships, and where

exlib is two things at once: a mod the game loads, and a set of libraries your project compiles
against. Players only ever see the first. Modders need both.

| Piece | Where to get it | What it is for |
| --- | --- | --- |
| `exlib_<version>.zip` | the [releases page](https://github.com/ringavirda/modding-vsexlib/releases) | the mod itself: one folder holding `exlib.dll` and `exlib.industry.dll`, installed into the game's `Mods` folder like any other mod. Your mod declares it as a dependency and the game loads it first. |
| `ExpandedLib` | [nuget.org](https://www.nuget.org/packages/ExpandedLib) | the framework you compile against: `exlib.dll`, the config and lang source generators, and the MSBuild plumbing that finds the game, copies your assets and stamps your version. |
| `ExpandedLib.Industry` | [nuget.org](https://www.nuget.org/packages/ExpandedLib.Industry) | the industry layer: pipe networks, molten metal, mechanical-power ports, heat balance. Reference it when your blocks carry gas, liquid, metal or shaft power. |
| `ExpandedLib.Testing` | [nuget.org](https://www.nuget.org/packages/ExpandedLib.Testing) | the headless test harness. A test project references it and drives your blocks under `dotnet test`, with no game running. |
| `ExpandedLib.Templates` | [nuget.org](https://www.nuget.org/packages/ExpandedLib.Templates) | `dotnet new` templates for a block, item, recipe, node, structure, config, command, migration or test project. `exmod scaffold` installs and runs them for you. |

The packages are built for the current game version only (1.22 on .NET 10). The zips on the
releases page also cover 1.21 and 1.20, named `exlib_<version>_1.21.0.zip` and
`exlib_<version>_1.20.0.zip`. None of the packages carries the game's own assemblies; your project
references `VintagestoryAPI.dll` and friends from a game install, the way every mod does.

## Adding exlib to an existing mod

Four edits, then a build.

**1. Reference the framework package.** In your mod's `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="ExpandedLib" Version="0.8.2" ExcludeAssets="runtime" />
  <PackageReference Include="ExpandedLib.Industry" Version="0.8.2" ExcludeAssets="runtime" />
</ItemGroup>
```

Leave `ExpandedLib.Industry` out if you use none of the industry systems. `ExcludeAssets="runtime"`
is required on both: the player installs exlib as its own mod, and a second copy of `exlib.dll`
inside your mod's folder makes the game refuse the whole folder with "Found multiple .dll files with
ModSystems". The refusal is silent from the player's side; your mod is simply not in the list.

**2. Name your asset domain.** In the same `.csproj`:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <AssetDomain>yourmod</AssetDomain>
</PropertyGroup>
```

`AssetDomain` is your mod id. It switches on the two things the package does for you at build time:
copying your `assets/` tree into the output, and generating a typed `YourmodLang` class from
`assets/yourmod/lang/en.json` (see [Source Generators](Source-Generators)). The package also
resolves the game install for you, from the `VINTAGE_STORY` environment variable or
`-p:GamePath=...` on the command line, so there is no `GamePath` property to write.

**3. Declare the runtime dependency.** In `modinfo.json`:

```json
{
  "type": "Code",
  "modid": "yourmod",
  "name": "Your Mod",
  "version": "1.0.0",
  "dependencies": {
    "game": "1.22.0",
    "exlib": "0.8.2"
  }
}
```

The number is a floor, not a pin: the game accepts any installed exlib at or above it. Write the
version you compiled against and raise it whenever you start calling something newer, or a player
with an older exlib gets a missing-member crash at world load instead of a clear dependency error.

**4. Install the mod into the game.** Put `exlib_<version>.zip` in the game's `Mods` folder, next
to your own mod's output, for every install you run or test in. `exmod provision mods` does this
for you when you use the [exmod](#the-exmod-tool) tool.

Then build. `dotnet build` restores the packages, finds the game, compiles and copies your assets;
the output folder under `bin/` is a complete mod.

## Registering your first class

With the package in place, exlib registers your classes from attributes. Tag them and derive one
empty mod system:

```csharp
using ExpandedLib.Registries;
using Vintagestory.API.Common;

[BlockRegister]
public class BlockMachine : Block { }

[BlockEntityRegister]
public class BlockEntityMachine : BlockEntity { }

public class YourModSystem : ExModSystem { }
```

There is no `RegisterBlockClass` list to keep in step with your classes. [Getting Started](Getting-Started)
carries on from here with a complete block, its saved state, a config value and a test.

## A test project

Reference the harness from a separate test project, never from the mod itself:

```xml
<ItemGroup>
  <PackageReference Include="ExpandedLib.Testing" Version="0.8.2" />
  <ProjectReference Include="../src/YourMod.csproj" />
</ItemGroup>
```

The harness loads the real game assemblies from your install and gives you a `TestWorld` to place
blocks in, tick, and assert on. Its API still moves between releases, so pin the version you build
against. [Testing Harness](Testing-Harness) shows what a first test looks like.

## The exmod tool

Nothing above needs it, but the whole loop (build, test, boot a server with your mod, scaffold a
new block) is one script when you copy `scripts/exmod.sh` and `scripts/exmod.ps1` from the exlib
repository, or from the starter, into yours and add an `exmod.json` naming your mod. `exmod provision game` downloads
the dedicated server so you can build and test without a game purchase on that machine;
`exmod provision mods` fetches the exlib zip your `modinfo.json` names; `exmod smoke` boots the
server against your built mod and fails on any error in the log. The commands are documented in
[extools](https://github.com/ringavirda/modding-vsextools).

## Building from source instead

If you clone the exlib repository beside your own, a `Directory.Build.props` above both that sets
`ExlibRoot` to the checkout switches your project from the packages to a plain project reference.
That is the loop for changing exlib and your mod together without a release in between; nothing
about it is part of the package's public contract.

## Versions

exlib follows semantic versioning from 0.8.0 on: a minor bump may change a supported signature,
a patch bump does not. [Supported API](Supported-API) lists which types are the supported
contract and which are internal plumbing that may move without notice.
