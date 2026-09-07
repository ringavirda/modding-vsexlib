# ExpandedLib.Industry

The Industry module of [`exlib`](https://www.nuget.org/packages/ExpandedLib): pipe networks
carrying gas and liquid, molten-metal canals and cells, mechanical-power nodes for machines and
the invisible cells of a mega-block, heat sources and sinks, and the metals catalogue
(`config/metals/*.json`) with the item families it emits. It ships inside the exlib mod folder as
`exlib.industry.dll` and is driven by exlib's module system, so a mod that depends on `exlib` has
it loaded already; this package is the compile-time reference.

Full docs on the wiki: [Block Networks](https://github.com/ringavirda/modding-vsexlib/wiki/Block-Networks)
(pipes, molten metal, mechanical power),
[Extending Processes](https://github.com/ringavirda/modding-vsexlib/wiki/Extending-Processes)
(routes, jobs, metals) and the Industry section of the
[Supported API](https://github.com/ringavirda/modding-vsexlib/wiki/Supported-API).

Reference it beside `ExpandedLib`, not instead of it: the build plumbing exlib ships (the game
reference, the asset globs, the generators' inputs) does not flow through this package's
dependency edge.

```xml
<ItemGroup>
  <PackageReference Include="ExpandedLib" ExcludeAssets="runtime" />
  <PackageReference Include="ExpandedLib.Industry" ExcludeAssets="runtime" />
</ItemGroup>
```

`ExcludeAssets="runtime"` keeps the framework's assemblies out of your mod's output: the game
loads them from the exlib mod folder.
