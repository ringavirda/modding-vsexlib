using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>A gate's band over a kind's reference set.</summary>
/// <param name="Min">The set's lowest value.</param>
/// <param name="Max">The set's highest value.</param>
/// <param name="MinCode">The vanilla code at the lowest value, without its domain.</param>
/// <param name="MaxCode">The vanilla code at the highest value, without its domain.</param>
/// <param name="Low">The lower end of the band, <paramref name="Min"/> widened; 0 for a gate with an upper end only.</param>
/// <param name="High">The upper end of the band, <paramref name="Max"/> widened.</param>
internal sealed record Band(
  double Min,
  double Max,
  string MinCode,
  string MaxCode,
  double Low,
  double High
);

/// <summary>The contents of view-bands.json.</summary>
/// <param name="Bands">Per kind name in lower case, per gate id, the band.</param>
/// <param name="Limits">The gates judged against a fixed limit rather than a band.</param>
/// <param name="Carriers">Per <c>tool</c> value in lower case, the code of the vanilla item a tool follows.</param>
/// <param name="Rules">Per reference name, the numbers the view rule copies from the reference item.</param>
internal sealed record ViewBandData(
  SortedDictionary<string, SortedDictionary<string, Band>> Bands,
  SortedDictionary<string, double> Limits,
  SortedDictionary<string, string> Carriers,
  SortedDictionary<string, SortedDictionary<string, double>> Rules
);

/// <summary>The bands the view gates judge against: per kind and gate, the reference set's minimum to
/// maximum, each end widened by the larger of 10% of its magnitude and the gate's floor.</summary>
internal static class ViewBands {
  /// <summary>The environment variable that makes <see cref="Write"/> the bands' source file.</summary>
  internal const string WriteSwitch = "EXLIB_WRITE_VIEW_BANDS";

  /// <summary>The gates whose band has an upper end only.</summary>
  internal static readonly IReadOnlySet<string> UpperOnly = new HashSet<string>
  {
    "G2",
    "G10",
    "G10fp",
    "G13",
  };

  /// <summary>The widening floor of each gate, in the gate's unit; a gate absent here widens by the percentage only.</summary>
  internal static readonly IReadOnlyDictionary<string, double> Floors =
    new Dictionary<string, double> {
      ["G1"] = 0.02,
      ["G2"] = 1,
      ["G5"] = 0.02,
      ["G6"] = 0.02,
      ["G7"] = 0.02,
      ["G13"] = 0.02,
    };

  /// <summary>The farthest a grip may lie from a drawn element, in voxels.</summary>
  internal const double GripLimit = 1;

  /// <summary>The farthest a bulk item's grip may lie from a drawn element, in voxels.</summary>
  internal const double BulkGripLimit = 2.1;

  private const double Share = 0.1;

  /// <summary>A kind's reference set: codes by exact match, or by prefix when an entry ends in a dash.</summary>
  /// <param name="Kind">The kind the set calibrates.</param>
  /// <param name="Entries">The codes, without domain, of the vanilla collectibles in the set.</param>
  /// <param name="First">Whether a prefix entry takes only its first code in load order, the lowest block or item id.</param>
  /// <param name="AnyKind">Whether a member's group may box as another kind than <paramref name="Kind"/>.</param>
  /// <param name="Gates">The gates the set calibrates.</param>
  internal sealed record ReferenceSet(
    ItemViewKind Kind,
    string[] Entries,
    bool First,
    bool AnyKind,
    string[] Gates
  );

  private static readonly string[] SlotGates = ["G1", "G2", "G5", "G6"];

  private static readonly string[] ItemGates = [.. SlotGates, "G7"];

  private static readonly string[] BlockGates = [.. ItemGates, "G13"];

  /// <summary>The reference sets. The spear set alone calibrates the longtool hand gates G7, G10 and G10fp.</summary>
  internal static readonly IReadOnlyList<ReferenceSet> Sets =
  [
    new(
      ItemViewKind.Longtool,
      ["spear-"],
      false,
      false,
      ["G7", "G10", "G10fp"]
    ),
    new(ItemViewKind.Longtool, ["spear-", "oar-"], false, false, SlotGates),
    new(
      ItemViewKind.Tool,
      [
        "axe-chert",
        "axe-scrap-scrap",
        "blade-falx-",
        "bow-simple",
        "chisel-copper",
        "hammer-copper",
        "hoe-chert",
        "pickaxe-copper",
        "prospectingpick-copper",
        "saw-copper",
        "scythe-copper",
        "shears-copper",
        "shovel-chert",
        "sling",
        "wrench-copper",
      ],
      true,
      false,
      ItemGates
    ),
    new(ItemViewKind.Plate, ["metalplate-"], false, false, ItemGates),
    new(
      ItemViewKind.Bar,
      ["rod-", "stick", "plank-aged"],
      false,
      false,
      ItemGates
    ),
    new(ItemViewKind.Lump, ["ingot-", "metalbit-"], false, false, ItemGates),
    new(
      ItemViewKind.Bulk,
      [
        "rawcheese-salted",
        "bracket-heavy-iron",
        "grindstone-sandstone",
        "hub-light4way-iron",
        "ironbloom",
      ],
      false,
      true,
      ItemGates
    ),
    new(
      ItemViewKind.Cube,
      [
        "devastatedsoil-0",
        "hay-normal-ud",
        "mudbrick-light",
        "soil-verylow-none",
        "cobblestone-andesite",
        "gravel-andesite",
        "rock-andesite",
        "sand-andesite",
        "stonebricks-suevite",
        "log-grown-aged-ud",
        "planks-aged-ns",
      ],
      false,
      false,
      BlockGates
    ),
    new(
      ItemViewKind.Partial,
      [
        "brickslabs-fire-north-free",
        "clayshinglelabs-black-north-free",
        "tileslab-2x2-center-blue-north-free",
        "woodenaxle-ud",
        "transmission-ns",
        "chute-cross-ns",
        "chute-straight-ns",
        "chute-t-ns",
        "chute-elbow-up-north",
        "mudbrickslab-dark-north-free",
        "tileslab-2x2-plain-travertine-north-free",
      ],
      false,
      false,
      BlockGates
    ),
    new(
      ItemViewKind.Multicell,
      [
        "doorkiln-tier1",
        "door-devastation-towergate",
        "door-plank-north-down-closed-left",
        "irondoor-north-down-closed-left",
        "largegear3",
        "pulverizerframe-north",
        "metaldoor-barred-iron",
        "door-ruined-barred1",
        "verticalboiler-north",
        "bed-wood-head-north",
        "trunk-north",
        "door-crude",
      ],
      false,
      false,
      BlockGates
    ),
  ];

  /// <summary>The working end of the vanilla spears that <see cref="Lay.Of"/>'s count rule gets wrong, by shape base; each is <c>+x</c>.</summary>
  internal static readonly IReadOnlyDictionary<string, string> TipByShape =
    new Dictionary<string, string> {
      ["game:item/tool/spear/ruined/boar"] = "+x",
      ["game:item/tool/spear/ruined/ranseur"] = "+x",
    };

  /// <summary>The reference item each of the view rule's references copies from, by name.</summary>
  private static readonly (string Name, string Code)[] RuleItems =
  [
    ("spear", "game:spear-generic-iron"),
    ("metalplate", "game:metalplate-iron"),
    ("ingot", "game:ingot-iron"),
    ("rod", "game:rod-iron"),
  ];

  private static ViewBandData? _embedded;

  /// <summary>The bands shipped in this assembly.</summary>
  /// <exception cref="InvalidOperationException">The assembly carries no view-bands.json.</exception>
  internal static ViewBandData Embedded {
    get {
      if (_embedded != null)
        return _embedded;
      using Stream stream =
        typeof(ViewBands).Assembly.GetManifestResourceStream(
          "ExpandedLib.Testing.Views.view-bands.json"
        )
        ?? throw new InvalidOperationException(
          "view-bands.json is not embedded"
        );
      using var reader = new StreamReader(stream);
      return _embedded =
        JsonConvert.DeserializeObject<ViewBandData>(
          reader.ReadToEnd(),
          Settings
        ) ?? throw new InvalidOperationException("view-bands.json is empty");
    }
  }

  private static JsonSerializerSettings Settings =>
    new() {
      ContractResolver = new CamelCasePropertyNamesContractResolver {
        NamingStrategy = new CamelCaseNamingStrategy {
          ProcessDictionaryKeys = false,
        },
      },
      Formatting = Formatting.Indented,
    };

  /// <summary>The widened band of a gate over values with their codes.</summary>
  /// <param name="gate">The gate id, which says whether the band has a lower end and what floor widens it.</param>
  /// <param name="values">The set's values, each with the code it belongs to.</param>
  internal static Band BandOf(
    string gate,
    IReadOnlyList<(double Value, string Code)> values
  ) {
    (double min, string minCode) = values
      .OrderBy(v => v.Value)
      .Select(v => (v.Value, v.Code))
      .First();
    (double max, string maxCode) = values
      .OrderByDescending(v => v.Value)
      .Select(v => (v.Value, v.Code))
      .First();
    double floor = Floors.GetValueOrDefault(gate);
    double Widen(double x) => Math.Max(Share * Math.Abs(x), floor);
    return new Band(
      Round(min),
      Round(max),
      minCode,
      maxCode,
      UpperOnly.Contains(gate) ? 0 : Round(min - Widen(min)),
      Round(max + Widen(max))
    );
  }

  /// <summary>The groups of <paramref name="set"/>, each with the first of its member codes the set names, in load order.</summary>
  /// <exception cref="InvalidOperationException">An entry names no collectible of the set's kind.</exception>
  internal static IReadOnlyList<(ViewGroup Group, string Code)> Resolve(
    TestWorld world,
    IReadOnlyList<ViewGroup> groups,
    ReferenceSet set
  ) {
    var ids = new Dictionary<string, int>();
    foreach (CollectibleObject c in world.World.Collectibles)
      if (c.Code != null)
        ids.TryAdd(c.Code.ToString(), c.Id);
    var found = new List<(ViewGroup Group, string Code)>();
    foreach (string entry in set.Entries) {
      bool prefix = entry.EndsWith('-');
      var hits = groups
        .Where(g => set.AnyKind || g.Kind == set.Kind)
        .SelectMany(g =>
          g.Codes.Where(c => Matches(c, entry, prefix)).Select(c => (g, c))
        )
        .OrderBy(h => ids[h.c])
        .ToList();
      if (hits.Count == 0)
        throw new InvalidOperationException(
          $"the {set.Kind.ToString().ToLowerInvariant()} reference set names {entry}, which no {set.Kind.ToString().ToLowerInvariant()} collectible matches"
        );
      foreach (var hit in set.First ? hits.Take(1) : hits)
        if (!prefix || found.All(f => f.Group != hit.g))
          found.Add(hit);
    }
    return found;
  }

  private static bool Matches(string code, string entry, bool prefix) {
    string path = code[(code.IndexOf(':') + 1)..];
    return prefix
      ? path.StartsWith(entry, StringComparison.Ordinal)
      : path == entry;
  }

  /// <summary>Computes the bands from the vanilla collectibles of <paramref name="world"/>.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  internal static ViewBandData Compute(TestWorld world) {
    IReadOnlyList<ViewGroup> groups = ViewGates.Groups(world, "game");
    var metrics = new Dictionary<ViewGroup, GroupMetrics>();
    GroupMetrics Of(ViewGroup g) =>
      metrics.TryGetValue(g, out GroupMetrics? m)
        ? m
        : metrics[g] = ViewGates.Measure(world, g);

    var bands = new SortedDictionary<string, SortedDictionary<string, Band>>(
      StringComparer.Ordinal
    );
    foreach (ReferenceSet set in Sets) {
      var members = Resolve(world, groups, set);
      string kind = set.Kind.ToString().ToLowerInvariant();
      if (!bands.TryGetValue(kind, out var gates))
        bands[kind] = gates = new SortedDictionary<string, Band>(
          StringComparer.Ordinal
        );
      foreach (string gate in set.Gates)
        gates[gate] = BandOf(
          gate,
          [
            .. members
              .Select(m =>
                (ViewGates.ValueOf(Of(m.Group), gate), Short(m.Code))
              )
              .Where(v => !double.IsNaN(v.Item1)),
          ]
        );
    }

    var carriers = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (ReferenceSet set in Sets.Where(s => s.Kind == ItemViewKind.Tool))
      foreach (var (group, _) in Resolve(world, groups, set)) {
        var tool = ((Item)group.Representative).Tool;
        Item? carrier = ItemViews.CarrierOf(world.Api, tool, null);
        if (tool != null && carrier != null)
          carriers[tool.Value.ToString().ToLowerInvariant()] =
            carrier.Code.ToString();
      }

    var rules = new SortedDictionary<string, SortedDictionary<string, double>>(
      StringComparer.Ordinal
    );
    foreach ((string name, string code) in RuleItems) {
      ViewGroup group = groups.Single(g => g.Codes.Contains(code));
      ModelTransform hand = ViewMatrices.Transform(
        group.Representative,
        ViewTarget.Hand
      );
      ModelTransform gui = ViewMatrices.Transform(
        group.Representative,
        ViewTarget.Gui
      );
      rules[name] = new SortedDictionary<string, double>(StringComparer.Ordinal) {
        ["handScale"] = Round(hand.ScaleXYZ.X),
        ["groundScale"] = Round(
          ViewMatrices
            .Transform(group.Representative, ViewTarget.Ground)
            .ScaleXYZ.X
        ),
        ["handRotationX"] = Round(hand.Rotation.X),
        ["handRotationY"] = Round(hand.Rotation.Y),
        ["handRotationZ"] = Round(hand.Rotation.Z),
        ["guiRotationX"] = Round(gui.Rotation.X),
        ["guiRotationY"] = Round(gui.Rotation.Y),
        ["guiRotationZ"] = Round(gui.Rotation.Z),
      };
      (double[] grip, double gap) = ViewGates.GripOf(world, group);
      rules[name]["gripX"] = Round(grip[0]);
      rules[name]["gripY"] = Round(grip[1]);
      rules[name]["gripZ"] = Round(grip[2]);
      rules[name]["offDrawn"] = Round(gap);
      rules[name]["thickness"] = Round(
        Lay.Of(group.Box, false, null).Extent(2)
      );
      if (name == "spear") {
        rules[name]["slotScale"] = Round(gui.ScaleXYZ.X);
        Lay lay = Lay.Of(group.Box, true, null);
        rules[name]["slotOrigin"] = Round(
          (
            lay.Coords(
              new Vec3d(16 * gui.Origin.X, 16 * gui.Origin.Y, 16 * gui.Origin.Z)
            )[0] - lay.Lo[0]
          ) / lay.Extent(0)
        );
        rules[name]["length"] = Round(
          new[]
          {
            group.Box.To.X - group.Box.From.X,
            group.Box.To.Y - group.Box.From.Y,
            group.Box.To.Z - group.Box.From.Z,
          }.Max()
        );
      }
    }
    foreach (
      ItemViewKind kind in new[]
      {
        ItemViewKind.Partial,
        ItemViewKind.Multicell,
      }
    ) {
      var members = Sets.Where(s => s.Kind == kind)
        .SelectMany(s => Resolve(world, groups, s))
        .ToList();
      double MedianOf(string gate) =>
        UpperMiddle([
          .. members.Select(m => ViewGates.ValueOf(Of(m.Group), gate)),
        ]);
      rules[kind.ToString().ToLowerInvariant()] = new SortedDictionary<
        string,
        double
      >(StringComparer.Ordinal) {
        ["slotSpan"] = Round(MedianOf("G1")),
        ["groundLong"] = Round(MedianOf("G6")),
        ["heldLong"] = Round(MedianOf("G7")),
      };
    }
    return new ViewBandData(
      bands,
      new SortedDictionary<string, double>(StringComparer.Ordinal) {
        ["G8"] = GripLimit,
        ["G8Bulk"] = BulkGripLimit,
      },
      carriers,
      rules
    );
  }

  /// <summary>The text of view-bands.json for <paramref name="data"/>: indented, keys in ordinal order, line feeds.</summary>
  internal static string Serialize(ViewBandData data) =>
    JsonConvert.SerializeObject(data, Settings).Replace("\r\n", "\n") + "\n";

  /// <summary>Where view-bands.json lives in the source tree.</summary>
  internal static string SourcePath =>
    Path.Combine(
      RepoPaths.Root,
      "src",
      "ExpandedLib.Testing",
      "Views",
      "view-bands.json"
    );

  /// <summary>True when <see cref="WriteSwitch"/> is <c>1</c>.</summary>
  internal static bool WriteRequested =>
    Environment.GetEnvironmentVariable(WriteSwitch) == "1";

  /// <summary>Writes <paramref name="data"/> over <see cref="SourcePath"/>.</summary>
  internal static void Write(ViewBandData data) =>
    File.WriteAllText(SourcePath, Serialize(data));

  /// <summary>Every band, limit, carrier and rule entry on which <paramref name="actual"/> departs from <paramref name="expected"/>, one line each, naming the vanilla code at the end that moved.</summary>
  internal static IReadOnlyList<string> Differences(
    ViewBandData expected,
    ViewBandData actual
  ) {
    var lines = new List<string>();
    foreach (string kind in expected.Bands.Keys.Union(actual.Bands.Keys)) {
      expected.Bands.TryGetValue(kind, out var e);
      actual.Bands.TryGetValue(kind, out var a);
      foreach (
        string gate in ((IEnumerable<string>?)e?.Keys ?? []).Union(
          (IEnumerable<string>?)a?.Keys ?? []
        )
      ) {
        Band? eb = e?.GetValueOrDefault(gate),
          ab = a?.GetValueOrDefault(gate);
        if (eb == ab)
          continue;
        string Show(Band? b) =>
          b == null
            ? "none"
            : string.Create(
              CultureInfo.InvariantCulture,
              $"{b.Min} ({b.MinCode}) .. {b.Max} ({b.MaxCode}), band {b.Low}..{b.High}"
            );
        lines.Add($"{kind} {gate}: computed {Show(eb)}, shipped {Show(ab)}");
      }
    }
    if (!expected.Limits.SequenceEqual(actual.Limits))
      lines.Add("limits differ");
    if (!expected.Carriers.SequenceEqual(actual.Carriers))
      lines.Add(
        "carriers: computed "
          + string.Join(
            ", ",
            expected.Carriers.Select(c => $"{c.Key} {c.Value}")
          )
          + "; shipped "
          + string.Join(", ", actual.Carriers.Select(c => $"{c.Key} {c.Value}"))
      );
    foreach (string name in expected.Rules.Keys.Union(actual.Rules.Keys)) {
      expected.Rules.TryGetValue(name, out var e);
      actual.Rules.TryGetValue(name, out var a);
      if (e == null || a == null || !e.SequenceEqual(a))
        lines.Add($"rules {name} differ");
    }
    return lines;
  }

  // The upper middle of the ascending values, as sweep_blocks.py takes its median.
  private static double UpperMiddle(double[] values) {
    double[] sorted = [.. values.Order()];
    return sorted[sorted.Length / 2];
  }

  private static string Short(string code) => code[(code.IndexOf(':') + 1)..];

  private static double Round(double v) => Math.Round(v, 4);
}
