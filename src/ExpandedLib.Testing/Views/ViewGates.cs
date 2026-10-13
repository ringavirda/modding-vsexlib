using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>Collectibles that draw and are held the same way, so one evaluation speaks for all of them.</summary>
/// <param name="Key">The shape, overlays, selective elements, transforms, held clips and kind the members share.</param>
/// <param name="Kind">The kind of the group's box.</param>
/// <param name="Box">The drawn box, in voxels.</param>
/// <param name="Representative">The first member in code order; the metrics are composed from it.</param>
/// <param name="Codes">Every member's code, in code order.</param>
/// <param name="Gui">The slot transform the group is measured on.</param>
/// <param name="Hand">The third-person hand transform the group is measured on.</param>
/// <param name="Ground">The ground transform the group is measured on.</param>
/// <param name="Held">The animation code the group is held with.</param>
internal sealed record ViewGroup(
  string Key,
  ItemViewKind Kind,
  CollectibleViews.ViewBox Box,
  CollectibleObject Representative,
  IReadOnlyList<string> Codes,
  ModelTransform Gui,
  ModelTransform Hand,
  ModelTransform Ground,
  string Held
) {
  internal bool IsBlock => Representative is Block;
}

/// <summary>G1 slot.span, G2 slot.centre and G3 slot.face of a group.</summary>
/// <param name="Span">The largest projected extent of the box, over the 48 px slot.</param>
/// <param name="Centre">The distance from the footprint's centre to the slot's centre, in pixels.</param>
/// <param name="Face">The area the drawn faces cover in the slot, over the box's largest face at that scale.</param>
internal sealed record SlotMetrics(double Span, double Centre, double Face);

/// <summary>G5 ground.rest and G6 ground.size of a group.</summary>
/// <param name="Rest">The lowest point of the posed box above the item's position, in blocks.</param>
/// <param name="Size">An item's ground scale in blocks per mesh unit; a block's longest ground extent, in blocks.</param>
internal sealed record GroundMetrics(double Rest, double Size);

/// <summary>G7, G8, G10 and G13 of a group in a hand.</summary>
/// <param name="Size">The hand scale; a longtool's held length over the seraph's height; a block's longest held side, in blocks.</param>
/// <param name="Grip">The distance from the point of the shape that lands on the hand to the nearest drawn element's box, in voxels; null for a block.</param>
/// <param name="Axis">The unit vector of the working end's direction in the pose's frame: the longest axis of <c>Lay.Of</c>, oriented to the working end.</param>
/// <param name="Centre">The distance from the posed box centre to where the block defaults put a cube's centre, in blocks; null for an item.</param>
internal sealed record HandMetrics(
  double Size,
  double? Grip,
  Vec3d Axis,
  double? Centre
);

/// <summary>Every gate's metrics of a group.</summary>
/// <param name="Slot">G1, G2 and G3.</param>
/// <param name="Ground">G5 and G6.</param>
/// <param name="Hand">G7, G8, G10's axis and G13 in third person.</param>
/// <param name="HandFp">The hand metrics with the seraph at the first-person clip; null for a block.</param>
/// <param name="Axis">G10, in degrees; null unless the group is a longtool.</param>
/// <param name="AxisFp">G10 in first person, in degrees; null unless the group is a longtool.</param>
internal sealed record GroupMetrics(
  SlotMetrics Slot,
  GroundMetrics Ground,
  HandMetrics Hand,
  HandMetrics? HandFp,
  double? Axis,
  double? AxisFp
);

/// <summary>One gate's row of a group, as the dump writes it.</summary>
/// <param name="Gate">The gate id.</param>
/// <param name="Name">The gate's name.</param>
/// <param name="Target">The view it is read on: <c>gui</c>, <c>tp</c>, <c>fp</c> or <c>ground</c>.</param>
/// <param name="Value">The group's value.</param>
/// <param name="Min">The lower limit; null for a gate judged on its upper end only.</param>
/// <param name="Max">The upper limit.</param>
/// <param name="Ends">The reference codes at the band's lower and upper end, without domain; null for G8.</param>
/// <param name="Pass">Whether the value is within the limits.</param>
/// <param name="Detail">What a failing row says after its key: the value and the limits.</param>
internal sealed record GateReading(
  string Gate,
  string Name,
  string Target,
  double Value,
  double? Min,
  double Max,
  string[]? Ends,
  bool Pass,
  string Detail
);

/// <summary>A gate a group fails.</summary>
/// <param name="Key">The identity <c>&lt;view&gt; [&lt;kind&gt;] &lt;gate&gt; &lt;name&gt;</c>, which a known-findings list matches.</param>
/// <param name="Message">The key followed by the value, the band or limit and the group's codes.</param>
internal sealed record ViewFinding(string Key, string Message);

/// <summary>The view gates, composed from the resolved collectibles of a loaded game world.</summary>
internal static class ViewGates {
  /// <summary>The side of an inventory slot at GUI scale 1, in pixels.</summary>
  internal const double SlotPx = 48;

  /// <summary>The seraph's height, in voxels.</summary>
  internal const double SeraphVoxels = 30.3;

  /// <summary>The clip a block is carried with.</summary>
  internal const string BlockHeldClip = "holdbothhandslarge";

  /// <summary>The asset whose animation list maps animation codes to the seraph's clips.</summary>
  internal static readonly AssetLocation PlayerAsset = new(
    "game:entities/humanoid/player.json"
  );

  /// <summary>The itemtype whose held long axis G10 compares against.</summary>
  internal const string SpearReference = "game:spear-generic-iron";

  /// <summary>The collectibles of <paramref name="domain"/> that have a drawn box, grouped by what decides their views.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="domain">The domain of the collectible codes.</param>
  /// <returns>The groups in the code order of their first member; a collectible with no box or no kind belongs to none.</returns>
  internal static IReadOnlyList<ViewGroup> Groups(
    TestWorld world,
    string domain
  ) {
    IWorldAccessor w = world.World;
    IEnumerable<CollectibleObject> all = w
      .Collectibles.Where(c => c.Code != null && c.Code.Domain == domain)
      .Where(c => c is not Block { BlockId: 0 })
      .OrderBy(c => c.Code.ToString(), StringComparer.Ordinal);
    var groups = new Dictionary<string, List<CollectibleObject>>();
    foreach (CollectibleObject c in all) {
      string key = KeyOf(c);
      if (!groups.TryGetValue(key, out List<CollectibleObject>? members))
        groups[key] = members = [];
      members.Add(c);
    }
    var result = new List<ViewGroup>();
    foreach ((string key, List<CollectibleObject> members) in groups)
      if (Build(world, key, members) is { } group)
        result.Add(group);
    return result;
  }

  /// <summary>The group of one collectible, measured on its resolved transforms and held code.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="collectible">The collectible to group.</param>
  /// <returns>The group, whose only code is the collectible's; null when it has no kind or no drawn box.</returns>
  internal static ViewGroup? GroupOf(
    TestWorld world,
    CollectibleObject collectible
  ) => Build(world, KeyOf(collectible), [collectible]);

  private static ViewGroup? Build(
    TestWorld world,
    string key,
    IReadOnlyList<CollectibleObject> members
  ) {
    CollectibleObject first = members[0];
    ItemViewKind? kind = CollectibleViews.KindOf(world.Api, first);
    CollectibleViews.ViewBox? box = CollectibleViews.BoxOf(world.Api, first);
    if (kind == null || box == null)
      return null;
    return new ViewGroup(
      key,
      kind.Value,
      box,
      first,
      members.Select(m => m.Code.ToString()).ToList(),
      ViewMatrices.Transform(first, ViewTarget.Gui),
      ViewMatrices.Transform(first, ViewTarget.Hand),
      ViewMatrices.Transform(first, ViewTarget.Ground),
      HeldCode(first)
    );
  }

  /// <summary>The group measured on <paramref name="views"/> in place of its resolved transforms, kind and clips.</summary>
  /// <param name="group">The group the views are for.</param>
  /// <param name="views">The views to measure; a clip that is null leaves the collectible's own.</param>
  internal static ViewGroup Measured(ViewGroup group, ItemViewSet views) =>
    group with {
      Kind = views.Kind,
      Gui = views.Gui,
      Hand = views.TpHand,
      Ground = views.Ground,
      Held = HeldCode(
        group.IsBlock,
        views.IdleClip ?? group.Representative.HeldRightTpIdleAnimation,
        views.ReadyClip ?? group.Representative.HeldRightReadyAnimation
      ),
    };

  /// <summary>Writes the file of each of <paramref name="groups"/> under the folder <see cref="ViewDump.DefaultVariable"/> names,
  /// measured on the group's resolved views.</summary>
  /// <param name="world">The world the groups come from.</param>
  /// <param name="groups">The groups to write.</param>
  /// <returns>The paths written; none when the variable is not set.</returns>
  internal static IReadOnlyList<string> Dump(
    TestWorld world,
    IEnumerable<ViewGroup> groups
  ) {
    var paths = new List<string>();
    foreach (ViewGroup group in groups) {
      CollectibleObject c = group.Representative;
      CollectibleViews.ViewBox box = group.Box;
      string? Clip(string? s) => string.IsNullOrEmpty(s) ? null : s;
      var views = new ItemViewSet(
        group.Kind,
        group.Gui,
        group.Hand,
        group.Ground,
        Clip(c.HeldRightTpIdleAnimation),
        Clip(c.HeldRightReadyAnimation),
        Clip(c.HeldTpHitAnimation),
        Clip(c.HeldTpUseAnimation),
        box.From,
        box.To,
        box.Axes,
        null
      );
      if (
        ViewDump.Write(
          world,
          "",
          group.Codes[0],
          group,
          views,
          ViewDump.DefaultVariable
        ) is { } path
      )
        paths.Add(path);
    }
    return paths;
  }

  /// <summary>The group's slot footprint at GUI scale 1: the bounds of the projected box as x0, y0, x1, y1 in pixels from
  /// the slot's top left, y down.</summary>
  internal static double[] Footprint(ViewGroup group) {
    (double x0, double y0, double x1, double y1) = SlotBounds(
      group,
      1,
      SlotPx / 2
    );
    return [x0, y0, x1, y1];
  }

  private static (double X0, double Y0, double X1, double Y1) SlotBounds(
    ViewGroup group,
    double guiScale,
    double offset
  ) {
    Mat4 m = ViewMatrices.Slot(
      group.Gui,
      group.IsBlock,
      guiScale,
      offset,
      offset
    );
    Vec3d[] p = Corners(group.Box).Select(m.Apply).ToArray();
    return (p.Min(v => v.X), p.Min(v => v.Y), p.Max(v => v.X), p.Max(v => v.Y));
  }

  /// <summary>G1, G2 and G3 of <paramref name="group"/> in the inventory slot.</summary>
  /// <param name="guiScale">The GUI scale; G3 is sampled at 4 points per slot pixel each way at scale 1.</param>
  internal static SlotMetrics Slot(ViewGroup group, double guiScale = 1) {
    (double x0, double y0, double x1, double y1) = SlotBounds(
      group,
      guiScale,
      0
    );
    return new SlotMetrics(
      Math.Max(x1 - x0, y1 - y0) / (SlotPx * guiScale),
      Math.Sqrt(Math.Pow((x0 + x1) / 2, 2) + Math.Pow((y0 + y1) / 2, 2)),
      FaceCover(group, guiScale)
    );
  }

  private const int FaceSamples = 4;

  // The corner indices of each face of an element, in the game's face order, cyclic around the quad.
  private static readonly int[][] FaceCorners =
  [
    [0, 1, 3, 2],
    [1, 3, 7, 5],
    [4, 5, 7, 6],
    [0, 2, 6, 4],
    [2, 3, 7, 6],
    [0, 1, 5, 4],
  ];

  private static double FaceCover(ViewGroup group, double guiScale) {
    Mat4 m = ViewMatrices.Slot(group.Gui, group.IsBlock, guiScale);
    var covered = new HashSet<(int, int)>();
    for (int e = 0; e < group.Box.Drawn.Count; e++)
      for (int f = 0; f < 6; f++) {
        if (!group.Box.Faces[e][f])
          continue;
        Vec3d[] q = FaceCorners[f]
          .Select(i => m.Apply(group.Box.Drawn[e][i] / 16))
          .ToArray();
        Cover(covered, q[0], q[1], q[2]);
        Cover(covered, q[0], q[2], q[3]);
      }
    double[] side = group.Box.Extents();
    double pxPerVoxel =
      ViewMatrices.SlotItemPx * guiScale * group.Gui.ScaleXYZ.X / 16;
    double largest = side[0] * side[1] * pxPerVoxel * pxPerVoxel;
    double cell = 1.0 / (FaceSamples * FaceSamples);
    return covered.Count * cell / largest;
  }

  private static void Cover(
    HashSet<(int, int)> covered,
    Vec3d a,
    Vec3d b,
    Vec3d c
  ) {
    double area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
    int x0 = (int)
      Math.Ceiling(Math.Min(a.X, Math.Min(b.X, c.X)) * FaceSamples - 0.5);
    int x1 = (int)
      Math.Floor(Math.Max(a.X, Math.Max(b.X, c.X)) * FaceSamples - 0.5);
    int y0 = (int)
      Math.Ceiling(Math.Min(a.Y, Math.Min(b.Y, c.Y)) * FaceSamples - 0.5);
    int y1 = (int)
      Math.Floor(Math.Max(a.Y, Math.Max(b.Y, c.Y)) * FaceSamples - 0.5);
    for (int i = x0; i <= x1; i++)
      for (int j = y0; j <= y1; j++) {
        double x = (i + 0.5) / FaceSamples,
          y = (j + 0.5) / FaceSamples;
        double u = ((b.X - x) * (c.Y - y) - (c.X - x) * (b.Y - y)) / area;
        double v = ((c.X - x) * (a.Y - y) - (a.X - x) * (c.Y - y)) / area;
        if (u >= 0 && v >= 0 && u + v <= 1)
          covered.Add((i, j));
      }
  }

  /// <summary>G5 and G6 of <paramref name="group"/> on the ground, at no yaw and no scale jitter.</summary>
  internal static GroundMetrics Ground(ViewGroup group) {
    ModelTransform tf = group.Ground;
    Vec3d[] p = Corners(group.Box)
      .Select(ViewMatrices.Ground(tf).Apply)
      .ToArray();
    double k = 0.2 * tf.ScaleXYZ.X;
    return new GroundMetrics(
      p.Min(v => v.Y),
      group.IsBlock ? k * group.Box.Extents()[0] / 16 : k
    );
  }

  /// <summary>The animation code a stack is held with: a block's is <c>holdbothhandslarge</c>, an item's its idle, else
  /// its ready, else <c>helditemready</c>.</summary>
  internal static string HeldCode(CollectibleObject collectible) =>
    HeldCode(
      collectible is Block,
      collectible.HeldRightTpIdleAnimation,
      collectible.HeldRightReadyAnimation
    );

  private static string HeldCode(bool block, string? idle, string? ready) =>
    block ? BlockHeldClip
    : !string.IsNullOrEmpty(idle) ? idle
    : !string.IsNullOrEmpty(ready) ? ready
    : "helditemready";

  /// <summary>The seraph shape's clip the game plays for <paramref name="code"/>: the player's animation list maps the
  /// code, ignoring case, to a clip, and the first-person hands take that clip's <c>-fp</c> twin.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="code">An animation code, such as an item's <c>heldRightTpIdleAnimation</c>.</param>
  /// <param name="firstPerson">Whether to take the <c>-fp</c> twin.</param>
  /// <returns>The clip code; null when the player has no such animation or its shape lacks the clip, which plays
  /// nothing and leaves the seraph at rest.</returns>
  internal static string? HeldClip(
    TestWorld world,
    string code,
    bool firstPerson = false
  ) {
    IAsset player = world.Api.Assets.Get(PlayerAsset);
    AnimationMetaData? meta = JsonObject
      .FromJson(player.ToText())["client"]["animations"]
      .AsArray<AnimationMetaData>()
      ?.FirstOrDefault(a =>
        string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase)
      );
    if (meta?.Animation == null)
      return null;
    string clip = meta.Animation + (firstPerson ? "-fp" : "");
    return SeraphPose
      .Clips(world.Api)
      .FirstOrDefault(c =>
        string.Equals(c, clip, StringComparison.OrdinalIgnoreCase)
      );
  }

  /// <summary>G7, G8, G10 and G13 of <paramref name="group"/>, with the seraph at frame 0 of its held clip, or at rest
  /// when the game plays none.</summary>
  /// <param name="world">The world the group comes from.</param>
  /// <param name="group">The group to measure.</param>
  /// <param name="firstPerson">Whether to pose the clip's <c>-fp</c> twin and take the first-person hand matrix; the
  /// block centre is then not measured.</param>
  internal static HandMetrics Hand(
    TestWorld world,
    ViewGroup group,
    bool firstPerson = false
  ) {
    CollectibleObject c = group.Representative;
    ModelTransform tf = group.Hand;
    SeraphPose pose = SeraphPose.At(
      world.Api,
      HeldClip(world, group.Held, firstPerson)
    );
    Mat4 m = firstPerson
      ? ViewMatrices.HandFp(tf, pose)
      : ViewMatrices.Hand(tf, pose);

    double[] side =
    [
      group.Box.To.X - group.Box.From.X,
      group.Box.To.Y - group.Box.From.Y,
      group.Box.To.Z - group.Box.From.Z,
    ];
    int longest = Array.IndexOf(side, side.Max());
    Vec3d axis =
      m.Apply(Lay.Of(group.Box, true, TipOf(c)).Axes[0]) - m.Apply(new Vec3d());
    axis /= axis.Length();
    double? grip = group.IsBlock ? null : GripGap(group.Box, tf, pose);

    double scale = tf.ScaleXYZ.X;
    double size =
      group.IsBlock ? scale * side[longest] / 16
      : group.Kind == ItemViewKind.Longtool
        ? scale * side[longest] / SeraphVoxels
      : scale;
    if (group.IsBlock) {
      Vec3d centre =
        Corners(group.Box).Aggregate(new Vec3d(), (sum, v) => sum + v) / 8;
      Vec3d cube = ViewMatrices
        .Hand(ViewMatrices.Default(true, ViewTarget.Hand), pose)
        .Apply(new Vec3d(0.5, 0.5, 0.5));
      return new HandMetrics(
        size,
        null,
        axis,
        firstPerson ? null : (m.Apply(centre) - cube).Length()
      );
    }
    return new HandMetrics(size, grip, axis, null);
  }

  /// <summary>G10 of <paramref name="group"/>: the angle between the held directions of its working end and of the iron
  /// spear's.</summary>
  /// <returns>Degrees, 0 to 180.</returns>
  internal static double AxisDegrees(
    TestWorld world,
    ViewGroup group,
    bool firstPerson = false
  ) {
    CollectibleObject spear = world.World.GetItem(
      new AssetLocation(SpearReference)
    );
    ViewGroup reference = GroupOf(world, spear)!;
    double dot = Dot(
      Hand(world, group, firstPerson).Axis,
      Hand(world, reference, firstPerson).Axis
    );
    return Math.Acos(Math.Clamp(dot, -1, 1)) * 180 / Math.PI;
  }

  /// <summary>Every banded gate's metric of <paramref name="group"/>.</summary>
  internal static GroupMetrics Measure(TestWorld world, ViewGroup group) =>
    new(
      Slot(group),
      Ground(group),
      Hand(world, group),
      group.IsBlock ? null : Hand(world, group, true),
      group.Kind == ItemViewKind.Longtool ? AxisDegrees(world, group) : null,
      group.Kind == ItemViewKind.Longtool
        ? AxisDegrees(world, group, true)
        : null
    );

  /// <summary>The value of the banded gate <paramref name="gate"/> in <paramref name="metrics"/>.</summary>
  /// <param name="gate">A gate id of view-bands.json: G1, G2, G3, G5, G6, G7, G10, G10fp or G13.</param>
  /// <returns>NaN when the group has no such metric.</returns>
  internal static double ValueOf(GroupMetrics metrics, string gate) =>
    gate switch {
      "G1" => metrics.Slot.Span,
      "G2" => metrics.Slot.Centre,
      "G3" => metrics.Slot.Face,
      "G5" => metrics.Ground.Rest,
      "G6" => metrics.Ground.Size,
      "G7" => metrics.Hand.Size,
      "G10" => metrics.Axis ?? double.NaN,
      "G10fp" => metrics.AxisFp ?? double.NaN,
      "G13" => metrics.Hand.Centre ?? double.NaN,
      _ => throw new ArgumentException($"{gate} is not a banded gate"),
    };

  private static readonly (
    string Id,
    string Gate,
    string Name,
    string Target
  )[] Judged =
  [
    ("G1", "G1", "slot.span", "gui"),
    ("G2", "G2", "slot.centre", "gui"),
    ("G3", "G3", "slot.face", "gui"),
    ("G5", "G5", "ground.rest", "ground"),
    ("G6", "G6", "ground.size", "ground"),
    ("G7", "G7", "hand.size", "tp"),
    ("G10", "G10", "hand.axis", "tp"),
    ("G10fp", "G10", "hand.axis.fp", "fp"),
    ("G13", "G13", "hand.centre", "tp"),
  ];

  /// <summary>The game item a mod item takes its views from, with the metrics those views give.</summary>
  /// <param name="Code">The twin's code without its domain.</param>
  /// <param name="Metrics">The twin's metrics from <see cref="Measure"/>.</param>
  internal sealed record ViewTwin(string Code, GroupMetrics Metrics);

  /// <summary>The game item whose shipped views <paramref name="group"/> takes, measured on its own views.</summary>
  /// <param name="world">The world the group comes from.</param>
  /// <param name="group">The group; only an item that the vanilla-shape rule matches has a twin.</param>
  /// <returns>The twin; null when the group is a block, declares its own views, or draws no game item's shape.</returns>
  internal static ViewTwin? TwinOf(TestWorld world, ViewGroup group) =>
    group.Representative is Item item
    && ItemViews.VanillaShape(world.Api, item) is { } twin
    && GroupOf(world, twin) is { } twinGroup
      ? new ViewTwin(twin.Code.Path, Measure(world, twinGroup))
      : null;

  /// <summary>Every gate <paramref name="group"/> is judged on against <paramref name="bands"/>, as the dump writes it: each
  /// banded gate in its kind's band (G2, G10 and G13 on the upper end only) and G8 against its limit, in third and first
  /// person. With a twin each banded gate is judged against the twin's value widened as a band widens it, and G8 against the
  /// larger of the limit and the twin's grip plus 10%.</summary>
  /// <param name="bands">The bands to judge against.</param>
  /// <param name="group">The group; a workpiece has no band and no reading.</param>
  /// <param name="metrics">The metrics of <paramref name="group"/> from <see cref="Measure"/>.</param>
  /// <param name="twin">The group's twin from <see cref="TwinOf"/>, or null to judge by the kind.</param>
  /// <returns>One reading per judged gate, in gate order; a gate the group has no metric for has none.</returns>
  internal static IReadOnlyList<GateReading> Readings(
    ViewBandData bands,
    ViewGroup group,
    GroupMetrics metrics,
    ViewTwin? twin = null
  ) {
    var readings = new List<GateReading>();
    if (group.Kind == ItemViewKind.Workpiece)
      return readings;
    string kind = group.Kind.ToString().ToLowerInvariant();

    foreach ((string id, string gate, string name, string target) in Judged) {
      Band? band;
      if (twin == null) {
        if (!bands.Bands[kind].TryGetValue(id, out band))
          continue;
      } else {
        double own = ValueOf(twin.Metrics, id);
        if (double.IsNaN(own))
          continue;
        band = ViewBands.BandOf(id, [(own, twin.Code)]);
      }
      double v = ValueOf(metrics, id);
      if (double.IsNaN(v))
        continue;
      string[] ends = [band.MinCode, band.MaxCode];
      string said =
        twin == null
          ? $"({band.MinCode} {Fmt(band.Min)}, {band.MaxCode} {Fmt(band.Max)})"
          : $"({twin.Code} {Fmt(band.Min)})";
      if (ViewBands.UpperOnly.Contains(id))
        readings.Add(
          new GateReading(
            gate,
            name,
            target,
            v,
            null,
            band.High,
            ends,
            v <= band.High,
            twin == null
              ? $"{Fmt(v)} above {Fmt(band.High)} ({band.MaxCode} {Fmt(band.Max)})"
              : $"{Fmt(v)} above {Fmt(band.High)} {said}"
          )
        );
      else
        readings.Add(
          new GateReading(
            gate,
            name,
            target,
            v,
            band.Low,
            band.High,
            ends,
            v >= band.Low && v <= band.High,
            $"{Fmt(v)} outside {Fmt(band.Low)}..{Fmt(band.High)} {said}"
          )
        );
    }

    double limit = bands.Limits[
      group.Kind == ItemViewKind.Bulk ? "G8Bulk" : "G8"
    ];
    if (metrics.Hand.Grip is { } grip)
      readings.Add(
        Grip("hand.grip", "tp", grip, limit, twin, twin?.Metrics.Hand.Grip)
      );
    if (metrics.HandFp?.Grip is { } gripFp)
      readings.Add(
        Grip(
          "hand.grip.fp",
          "fp",
          gripFp,
          limit,
          twin,
          twin?.Metrics.HandFp?.Grip
        )
      );
    return readings;
  }

  private static GateReading Grip(
    string name,
    string target,
    double grip,
    double limit,
    ViewTwin? twin,
    double? twinGrip
  ) {
    if (twin != null && twinGrip is { } own)
      limit = Math.Max(limit, own * (1 + ViewBands.Share));
    return new(
      "G8",
      name,
      target,
      grip,
      null,
      limit,
      null,
      grip <= limit,
      twin != null && twinGrip is { } shown
        ? $"{Fmt(grip)} above {Fmt(limit)} ({twin.Code} {Fmt(shown)})"
        : $"{Fmt(grip)} above {Fmt(limit)} voxel"
    );
  }

  /// <summary>The gates <paramref name="group"/> fails against <paramref name="bands"/>: the readings that do not pass.</summary>
  /// <param name="bands">The bands to judge against.</param>
  /// <param name="group">The group; a workpiece has no band and fails none.</param>
  /// <param name="metrics">The metrics of <paramref name="group"/> from <see cref="Measure"/>.</param>
  /// <param name="twin">The group's twin from <see cref="TwinOf"/>, or null to judge by the kind.</param>
  /// <returns>One finding per failing gate, in gate order.</returns>
  internal static IReadOnlyList<ViewFinding> Judge(
    ViewBandData bands,
    ViewGroup group,
    GroupMetrics metrics,
    ViewTwin? twin = null
  ) {
    string kind = group.Kind.ToString().ToLowerInvariant();
    string view = group.Codes[0];
    string codes =
      group.Codes.Count > 1
        ? $" for {group.Codes.Count} codes: {string.Join(", ", group.Codes)}"
        : "";
    return
    [
      .. Readings(bands, group, metrics, twin)
        .Where(r => !r.Pass)
        .Select(r =>
        {
          string key = $"{view} [{kind}] {r.Gate} {r.Name}";
          return new ViewFinding(key, $"{key} {r.Detail}{codes}");
        }),
    ];
  }

  private static string Fmt(double v) =>
    v.ToString("0.00", CultureInfo.InvariantCulture);

  private static double Dot(Vec3d a, Vec3d b) =>
    a.X * b.X + a.Y * b.Y + a.Z * b.Z;

  /// <summary>The solved third-person grip of <paramref name="group"/>: the point of its shape that lands on the hand, as
  /// fractions of its lay's extents, and its gap to the nearest drawn element.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>.</param>
  /// <param name="group">The group to solve.</param>
  /// <returns>Three fractions, longest lay axis first, each along its own lay axis from the low face (0) to the high
  /// face (1), so a value below 0 or above 1 lies off the drawn box; then the gap in voxels to the nearest drawn element.</returns>
  internal static (double[] Fractions, double Gap) GripOf(
    TestWorld world,
    ViewGroup group
  ) {
    ModelTransform tf = group.Hand;
    SeraphPose pose = SeraphPose.At(world.Api, HeldClip(world, group.Held));
    double[] at = GripPoint(tf, pose);
    Lay lay = Lay.Of(
      group.Box,
      group.Kind is ItemViewKind.Longtool or ItemViewKind.Tool,
      TipOf(group.Representative)
    );
    double[] u = lay.Coords(new Vec3d(at[0], at[1], at[2]));
    return (
      [
        .. Enumerable
          .Range(0, 3)
          .Select(k => (u[k] - lay.Lo[k]) / lay.Extent(k)),
      ],
      GripGap(group.Box, tf, pose)
    );
  }

  /// <summary>The working end a group's longest axis points at: the collectible's declared <c>itemViews.tip</c>, else
  /// the vanilla shape's entry in <see cref="ViewBands.TipByShape"/>, else null, which leaves the end to the count rule.</summary>
  private static string? TipOf(CollectibleObject c) =>
    c.Attributes?["itemViews"]?["tip"]?.AsString()
    ?? (
      c is Item { Shape.Base: { } shapeBase }
        ? ViewBands.TipByShape.GetValueOrDefault(shapeBase.ToString())
        : null
    );

  private static double GripGap(
    CollectibleViews.ViewBox box,
    ModelTransform tf,
    SeraphPose pose
  ) {
    double[] g = GripPoint(tf, pose);
    var grip = new Vec3d(g[0], g[1], g[2]);
    box.Nearest(grip, out double gap);
    return gap;
  }

  // The shape point that lands on the hand solves hand(p) = anchor origin, so the anchor and the pose drop out.
  internal static double[] GripPoint(ModelTransform tf, SeraphPose pose) {
    AttachmentPoint ap = pose.Attachment;
    Mat4 turn = Mat4.Identity.RotateXYZ(
      ap.RotationX + tf.Rotation.X,
      ap.RotationY + tf.Rotation.Y,
      ap.RotationZ + tf.Rotation.Z
    );
    double[] w =
    [
      -(ap.PosX / 16 + tf.Translation.X) - tf.Origin.X / tf.ScaleXYZ.X,
      -(ap.PosY / 16 + tf.Translation.Y) - tf.Origin.Y / tf.ScaleXYZ.Y,
      -(ap.PosZ / 16 + tf.Translation.Z) - tf.Origin.Z / tf.ScaleXYZ.Z,
    ];
    double[] o = [tf.Origin.X, tf.Origin.Y, tf.Origin.Z];
    return
    [
      16 * (o[0] + turn[0, 0] * w[0] + turn[1, 0] * w[1] + turn[2, 0] * w[2]),
      16 * (o[1] + turn[0, 1] * w[0] + turn[1, 1] * w[1] + turn[2, 1] * w[2]),
      16 * (o[2] + turn[0, 2] * w[0] + turn[1, 2] * w[1] + turn[2, 2] * w[2]),
    ];
  }

  /// <summary>The eight corners of <paramref name="box"/> in mesh units (blocks), in the shape's frame.</summary>
  internal static Vec3d[] Corners(CollectibleViews.ViewBox box) {
    double[] from = [box.From.X, box.From.Y, box.From.Z];
    double[] to = [box.To.X, box.To.Y, box.To.Z];
    var corners = new Vec3d[8];
    for (int i = 0; i < 8; i++) {
      double a = (i & 1) == 0 ? from[0] : to[0],
        b = (i & 2) == 0 ? from[1] : to[1],
        c = (i & 4) == 0 ? from[2] : to[2];
      corners[i] =
        new Vec3d(
          box.Axes[0].X * a + box.Axes[1].X * b + box.Axes[2].X * c,
          box.Axes[0].Y * a + box.Axes[1].Y * b + box.Axes[2].Y * c,
          box.Axes[0].Z * a + box.Axes[1].Z * b + box.Axes[2].Z * c
        ) / 16;
    }
    return corners;
  }

  private static string KeyOf(CollectibleObject c) {
    CompositeShape? shape = c is Block block
      ? block.ShapeInventory ?? block.Shape
      : ((Item)c).Shape;
    return string.Join(
      "|",
      ShapeKey(shape),
      string.Join(",", shape?.Overlays?.Select(ShapeKey) ?? []),
      string.Join(",", shape?.SelectiveElements ?? []),
      TransformKey(ViewMatrices.Transform(c, ViewTarget.Gui)),
      TransformKey(ViewMatrices.Transform(c, ViewTarget.Hand)),
      TransformKey(ViewMatrices.Transform(c, ViewTarget.Ground)),
      c.HeldRightTpIdleAnimation,
      c.HeldRightReadyAnimation,
      c.HeldTpHitAnimation,
      c.HeldTpUseAnimation,
      c.Attributes?["itemViews"]?["kind"]?.AsString(),
      c.Tool,
      c is ItemWorkItem
    );
  }

  private static string ShapeKey(CompositeShape? s) =>
    s == null
      ? "-"
      : string.Create(
        CultureInfo.InvariantCulture,
        $"{s.Base}@{s.rotateX},{s.rotateY},{s.rotateZ}"
      );

  internal static string TransformKey(ModelTransform t) =>
    string.Create(
      CultureInfo.InvariantCulture,
      $"{t.Translation}/{t.Rotation}/{t.Origin}/{t.ScaleXYZ}/{t.Rotate}"
    );
}
