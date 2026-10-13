using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
  internal static IReadOnlyList<SweepRow> Rows(TestWorld world, string domain) {
    Dictionary<string, ViewGroup> groups = [];
    foreach (ViewGroup group in ViewGates.Groups(world, domain))
      foreach (string code in group.Codes)
        groups[Key(group.IsBlock, code)] = group;
    Dictionary<
      ViewGroup,
      (string[] Failed, string Note, string? Error)
    > measured = [];
    ViewBandData bands = ViewBands.Embedded;
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
        if (!measured.TryGetValue(group, out var m))
          measured[group] = m = Measure(world, bands, group);
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
          else
            kind = Name(
              CollectibleViews.ByBox(box.Extents(), false, item.Tool != null)
            );
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

  // Items and blocks have their own registries, so a code can name one of each.
  private static string Key(bool block, string code) =>
    (block ? "block " : "item ") + code;

  private static (string[] Failed, string Note, string? Error) Measure(
    TestWorld world,
    ViewBandData bands,
    ViewGroup group
  ) {
    try {
      GroupMetrics metrics = ViewGates.Measure(world, group);
      string[] failed =
      [
        .. ViewGates
          .Readings(bands, group, metrics)
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

  private sealed record TypeFile(string Path, string[] VariantCodes);

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
                [
                  .. (file["variantgroups"] as JArray ?? [])
                .Select(g => (string?)g["code"] ?? "")
                .Order(StringComparer.Ordinal),
                ]
              )
            );
          } catch (Newtonsoft.Json.JsonException) { }
        }
    return sources;
  }

  // Several files can share a code (ore-graded and ore-ungraded); the one whose variant groups are the collectible's
  // variant keys wins, else the first by path.
  private static string SourceOf(
    Dictionary<string, List<TypeFile>> sources,
    CollectibleObject c
  ) {
    string prefix = c is Block ? "blocktypes/" : "itemtypes/";
    string[] keys = [.. c.Variant.Keys.Order(StringComparer.Ordinal)];
    string path = c.Code.Path;
    while (true) {
      if (sources.TryGetValue(prefix + path, out List<TypeFile>? files))
        return (
          files.FirstOrDefault(f => f.VariantCodes.SequenceEqual(keys))
          ?? files.OrderBy(f => f.Path, StringComparer.Ordinal).First()
        ).Path;
      int cut = path.LastIndexOf('-');
      if (cut < 0)
        return "";
      path = path[..cut];
    }
  }
}
