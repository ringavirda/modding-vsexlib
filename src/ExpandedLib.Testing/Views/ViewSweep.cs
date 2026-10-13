using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExpandedLib.Helpers;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace ExpandedLib.Testing;

/// <summary>One collectible's line of the sweep.</summary>
/// <param name="Code">The collectible's code with its domain.</param>
/// <param name="Type"><c>item</c> or <c>block</c>.</param>
/// <param name="Source">The game or survival type file that defines it, such as <c>itemtypes/tool/axe.json</c>; empty when no
/// file's code leads into the collectible's.</param>
/// <param name="First">Whether it is the lowest-id collectible of its source file.</param>
/// <param name="Kind">The kind its box gives it, in lower case; null when it is skipped.</param>
/// <param name="Skip">Why it has no kind; null when it has one.</param>
/// <param name="Note">What the measurement could not resolve for a boxed collectible; empty when nothing.</param>
/// <param name="Failed">The rules its views fail, as <c>G7</c> or <c>G10fp</c>.</param>
internal sealed record SweepRow(
  string Code,
  string Type,
  string Source,
  bool First,
  string? Kind,
  string? Skip,
  string Note,
  IReadOnlyList<string> Failed
);

/// <summary>The kind each collectible of a loaded game world boxes as, and the rules its views fail. It judges and
/// counts; nothing in it fails on a kind or a rule.</summary>
internal static class ViewSweep {
  /// <summary>The environment variable that names the file <see cref="Write"/> writes.</summary>
  internal const string DefaultVariable = "EX_ITEM_VIEWS_SWEEP";

  /// <summary>The line of every collectible of <paramref name="domain"/>.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="domain">The domain of the collectible codes.</param>
  /// <returns>One row per collectible, in code order: boxed with a kind, or skipped with the reason. A collectible
  /// whose measurement throws is skipped with the exception's message.</returns>
  /// <exception cref="DirectoryNotFoundException">The repository's configured docs root does not exist.</exception>
  internal static IReadOnlyList<SweepRow> Rows(TestWorld world, string domain) {
    Dictionary<string, ViewGroup> groups = [];
    foreach (ViewGroup group in ViewGates.Groups(world, domain))
      foreach (string code in group.Codes)
        groups[Key(group.IsBlock, code)] = group;
    ViewBandData bands = ViewBands.Embedded;
    ViewCatalogue? catalogue = ViewCatalogue.Load(RepoPaths.DocsRoot, domain);
    ConcurrentDictionary<
      ViewGroup,
      (string[] Failed, string Note, string? Error)
    > measured = [];
    Parallel.ForEach(
      groups.Values.Distinct(),
      group => measured[group] = Measure(world, bands, catalogue, group)
    );
    Dictionary<string, List<TypeFile>> sources = Sources();

    var collectibles = world
      .World.Collectibles.Where(c => c.Code != null && c.Code.Domain == domain)
      .OrderBy(c => c.Code.ToString(), StringComparer.Ordinal)
      .ToList();
    var ids = new Dictionary<string, int>();
    foreach (CollectibleObject c in collectibles)
      ids[Key(c is Block, c.Code.ToString())] = c is Block b
        ? b.BlockId
        : ((Item)c).ItemId;

    var rows = new List<SweepRow>();
    foreach (CollectibleObject c in collectibles) {
      string code = c.Code.ToString();
      string type = c is Block ? "block" : "item";
      string source = SourceOf(sources, c);
      string? kind = null,
        skip = null;
      string note = "";
      string[] failed = [];
      if (c is Block { BlockId: 0 })
        skip = "air";
      else if (groups.TryGetValue(Key(c is Block, code), out ViewGroup? group)) {
        var m = measured[group];
        if (m.Error != null)
          skip = m.Error;
        else {
          kind = Name(group.Kind);
          failed = m.Failed;
          note = m.Note;
        }
      } else if (c is Item item && TextureBox.IsTextureOnly(item))
        try {
          CollectibleViews.ViewBox? box = TextureBox.Of(world.Api, item);
          if (box == null)
            skip = "texture has no opaque pixel";
          else {
            ViewGroup texture = TextureGroup(item, box);
            kind = Name(texture.Kind);
            failed = Failed(world, texture, catalogue);
          }
        } catch (Exception e)
            when (e
                is NotSupportedException
                  or InvalidDataException
                  or InvalidOperationException
            ) {
          skip = $"texture: {e.Message}";
        }
      else
        skip = "shape does not load or has no drawn face";
      rows.Add(
        new SweepRow(code, type, source, false, kind, skip, note, failed)
      );
    }

    var lowest = rows.Where(r => r.Source != "")
      .GroupBy(r => r.Source)
      .ToDictionary(
        g => g.Key,
        g =>
          g.MinBy(r => (r.Type, ids[Key(r.Type == "block", r.Code)]))
          ?? g.First()
      );
    return
    [
      .. rows.Select(r =>
        r with
        {
          First = r.Source != "" && ReferenceEquals(lowest[r.Source], r),
        }
      ),
    ];
  }

  // The group of a texture-only item: its texture's box with the item's own transforms and clips.
  private static ViewGroup TextureGroup(
    Item item,
    CollectibleViews.ViewBox box
  ) =>
    new(
      Key(false, item.Code.ToString()),
      CollectibleViews.ByBox(box.Extents(), false, item.Tool != null),
      box,
      item,
      [item.Code.ToString()],
      ViewMatrices.Transform(item, ViewTarget.Gui),
      ViewMatrices.Transform(item, ViewTarget.Hand),
      ViewMatrices.Transform(item, ViewTarget.Ground),
      ViewGates.HeldCode(item)
    );

  private static string[] Failed(
    TestWorld world,
    ViewGroup group,
    ViewCatalogue? catalogue
  ) =>
    [
      .. ViewGates
        .Rules(world, group, catalogue)
        .Where(r => !r.Pass)
        .Select(r => r.Gate + (r.Target == "fp" ? "fp" : "")),
    ];

  // Items and blocks have their own registries, so a code can name one of each.
  private static string Key(bool block, string code) =>
    (block ? "block " : "item ") + code;

  private static (string[] Failed, string Note, string? Error) Measure(
    TestWorld world,
    ViewBandData bands,
    ViewCatalogue? catalogue,
    ViewGroup group
  ) {
    try {
      GroupMetrics metrics = ViewGates.Measure(world, group);
      string[] failed =
      [
        .. ViewGates
          .Readings(bands, group, metrics)
          .Concat(ViewGates.Rules(world, group, catalogue))
          .Where(r => !r.Pass)
          .Select(r => r.Gate + (r.Target == "fp" ? "fp" : "")),
      ];
      string note =
        ViewGates.HeldClip(world, group.Held) == null
          ? $"held clip {group.Held} resolves to nothing"
          : "";
      return (failed, note, null);
    } catch (Exception e) {
      return ([], "", $"measure: {e.Message}");
    }
  }

  /// <summary>Writes <see cref="Rows"/> of the game domain as a tab-separated file, then the count of each kind and of
  /// each rule's failures over every row and over first variants.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="variable">The environment variable that names the file; unset or empty writes nothing.</param>
  /// <returns>The path written; null when <paramref name="variable"/> is not set.</returns>
  internal static string? Write(
    TestWorld world,
    string variable = DefaultVariable
  ) {
    if (
      Environment.GetEnvironmentVariable(variable) is not { Length: > 0 } path
    )
      return null;
    IReadOnlyList<SweepRow> rows = Rows(world, "game");
    var lines = new List<string>
    {
      "code\ttype\tsource\tfirst\tkind\tskip\tnote\tfailed",
    };
    lines.AddRange(
      rows.Select(r =>
        string.Join(
          "\t",
          r.Code,
          r.Type,
          r.Source,
          r.First ? "1" : "0",
          r.Kind ?? "",
          r.Skip ?? "",
          r.Note,
          string.Join(",", r.Failed)
        )
      )
    );
    lines.Add("#count\tname\tall\tfirst");
    foreach (
      string kind in rows.Where(r => r.Kind != null)
        .Select(r => r.Type + " " + r.Kind!)
        .Distinct()
        .Order(StringComparer.Ordinal)
    )
      lines.Add(
        Count("kind", kind, rows.Where(r => r.Type + " " + r.Kind == kind))
      );
    lines.Add(Count("skipped", "", rows.Where(r => r.Skip != null)));
    foreach (
      string gate in rows.SelectMany(r => r.Failed)
        .Distinct()
        .Order(StringComparer.Ordinal)
    )
      lines.Add(Count("fail", gate, rows.Where(r => r.Failed.Contains(gate))));
    string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
    if (folder != null)
      Directory.CreateDirectory(folder);
    File.WriteAllLines(path, lines);
    return path;
  }

  private static string Count(
    string what,
    string name,
    IEnumerable<SweepRow> rows
  ) {
    SweepRow[] all = [.. rows];
    return string.Create(
      CultureInfo.InvariantCulture,
      $"#{what}\t{name}\t{all.Length}\t{all.Count(r => r.First)}"
    );
  }

  private static string Name(ItemViewKind kind) =>
    kind.ToString().ToLowerInvariant();

  // A variant group with no code loads its code from a properties file, and a group that loads from properties has states
  // the file does not list: both are counted, and the latter is a wildcard.
  private sealed record TypeFile(
    string Path,
    Dictionary<string, string[]> States,
    int Loaded
  );

  private static Dictionary<string, List<TypeFile>> Sources() {
    var sources = new Dictionary<string, List<TypeFile>>();
    string? install = VsAssemblyResolver.InstallPath;
    if (install == null)
      return sources;
    foreach (string origin in (string[])["game", "survival"])
      foreach (
        AssetCategory category in (AssetCategory[])
          [AssetCategory.blocktypes, AssetCategory.itemtypes]
      )
        foreach (
          IAsset asset in new PathOrigin(
            "game",
            Path.Combine(install, "assets", origin)
          ).GetAssets(category, shouldLoad: true)
        ) {
          try {
            var file = JObject.Parse(asset.ToText());
            if (file["code"] is not JValue { Value: string code })
              continue;
            string key = category.Code + "/" + code;
            if (!sources.TryGetValue(key, out List<TypeFile>? files))
              sources[key] = files = [];
            files.Add(
              new TypeFile(
                asset.Location.Path,
                VariantStates(file, out int loaded),
                loaded
              )
            );
          } catch (Newtonsoft.Json.JsonException) { }
        }
    return sources;
  }

  // A later group of a code that an earlier one has adds its states to it.
  private static Dictionary<string, string[]> VariantStates(
    JObject file,
    out int loaded
  ) {
    var states = new Dictionary<string, string[]>();
    loaded = 0;
    foreach (JToken group in file["variantgroups"] as JArray ?? []) {
      if ((string?)group["code"] is not { } code) {
        loaded++;
        continue;
      }
      bool wildcard =
        group["loadFromProperties"] != null
        || (
          states.TryGetValue(code, out string[]? before) && before.Length == 0
        );
      states[code] = wildcard
        ? []
        :
        [
          .. states.GetValueOrDefault(code) ?? [],
          .. (group["states"] as JArray ?? []).Select(s => (string?)s ?? ""),
        ];
    }
    return states;
  }

  // Several files can share a code (coral-brain and coral-tube are both coral): the first by path whose variant states
  // hold each of the collectible's variant values wins, else the first by path.
  private static string SourceOf(
    Dictionary<string, List<TypeFile>> sources,
    CollectibleObject c
  ) {
    string prefix = c is Block ? "blocktypes/" : "itemtypes/";
    string path = c.Code.Path;
    while (true) {
      if (sources.TryGetValue(prefix + path, out List<TypeFile>? files))
        return (
          files
            .Where(f => Holds(f, c))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .FirstOrDefault()
          ?? files.OrderBy(f => f.Path, StringComparer.Ordinal).First()
        ).Path;
      int cut = path.LastIndexOf('-');
      if (cut < 0)
        return "";
      path = path[..cut];
    }
  }

  private static bool Holds(TypeFile file, CollectibleObject c) =>
    c.Variant.Count - file.States.Count == file.Loaded
    && file.States.All(g =>
      c.Variant.TryGetValue(g.Key, out string? value)
      && (g.Value.Length == 0 || g.Value.Contains(value))
    );
}
