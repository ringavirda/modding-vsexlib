using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ExpandedLib.Helpers;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>Writes the views of a collectible, and every gate's and rule's (G4, G9, G11, G14, G15, G16, G17) reading of them, as an <c>item-views/1</c> file that
/// extools renders. The gates measure the views given, never the collectible's resolved fields.</summary>
public static class ViewDump {
  /// <summary>The environment variable that names the folder the files go under.</summary>
  public const string DefaultVariable = "EX_ITEM_VIEWS";

  /// <summary>Writes the views of <paramref name="collectible"/> as <c>&lt;domain&gt;-&lt;code&gt;.views.json</c>, where the
  /// code is <paramref name="view"/> with its <c>:</c> and <c>/</c> turned to <c>-</c>.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>; its shape and textures are read from it.</param>
  /// <param name="directory">The folder under the one the variable names; empty for that folder itself.</param>
  /// <param name="view">The name the file carries: the collectible's code.</param>
  /// <param name="collectible">The item or block the views are for.</param>
  /// <param name="views">The views to measure and write, from <see cref="ItemViews.Compute(Vintagestory.API.Common.ICoreAPI, CollectibleObject)"/>
  /// or authored; a target whose transform differs from the collectible's resolved one is written as computed.</param>
  /// <param name="variable">The environment variable that names the folder; unset or empty writes nothing.</param>
  /// <returns>The path written; null when <paramref name="variable"/> is not set.</returns>
  /// <exception cref="ArgumentException"><paramref name="collectible"/> has no kind or no drawn box.</exception>
  public static string? Write(
    TestWorld world,
    string directory,
    string view,
    CollectibleObject collectible,
    ItemViewSet views,
    string variable = DefaultVariable
  ) =>
    Write(
      world,
      directory,
      view,
      ViewGates.GroupOf(world, collectible)
        ?? throw new ArgumentException(
          $"{collectible.Code} has no kind or no drawn box",
          nameof(collectible)
        ),
      views,
      variable
    );

  /// <summary>Writes the views of a route stage, a shape drawn as an item, as
  /// <c>&lt;domain&gt;-&lt;code&gt;@&lt;family&gt;-&lt;element&gt;.views.json</c> for a <paramref name="view"/> of the form
  /// <c>&lt;domain&gt;:&lt;code&gt;@&lt;family&gt;/&lt;element&gt;</c>; every target is written as computed.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAsGame"/>; the shape is read from it.</param>
  /// <param name="directory">The folder under the one the variable names; empty for that folder itself.</param>
  /// <param name="view">The name the file carries.</param>
  /// <param name="shape">The shape the stage draws.</param>
  /// <param name="textures">The stage's texture keys and the assets they name.</param>
  /// <param name="views">The views to measure and write.</param>
  /// <param name="variable">The environment variable that names the folder; unset or empty writes nothing.</param>
  /// <returns>The path written; null when <paramref name="variable"/> is not set.</returns>
  /// <exception cref="ArgumentException"><paramref name="shape"/> does not load or has no drawn face.</exception>
  public static string? Write(
    TestWorld world,
    string directory,
    string view,
    CompositeShape shape,
    IReadOnlyDictionary<string, AssetLocation> textures,
    ItemViewSet views,
    string variable = DefaultVariable
  ) {
    if (Root(variable) == null)
      return null;
    CollectibleViews.ViewBox box =
      CollectibleViews.ItemBox(world.Api, shape)
      ?? throw new ArgumentException(
        $"shape {shape.Base} does not load or has no drawn face",
        nameof(shape)
      );
    var group = new ViewGroup(
      view,
      views.Kind,
      box,
      new Item { Shape = shape },
      [view],
      views.Gui,
      views.TpHand,
      views.Ground,
      "helditemready"
    );
    return Emit(
      world,
      directory,
      view,
      ViewGates.Measured(group, views),
      shape,
      textures.ToDictionary(t => t.Key, t => t.Value.ToString()),
      views,
      ["gui", "tp", "ground"],
      variable
    );
  }

  internal static string? Write(
    TestWorld world,
    string directory,
    string view,
    ViewGroup group,
    ItemViewSet views,
    string variable
  ) {
    if (Root(variable) == null)
      return null;
    CollectibleObject c = group.Representative;
    CompositeShape? shape = c is Block block
      ? block.ShapeInventory ?? block.Shape
      : ((Item)c).Shape;
    IDictionary<string, CompositeTexture>? own = c is Block b
      ? b.Textures
      : ((Item)c).Textures;
    string[] computed =
    [
      .. new[]
      {
        ("gui", views.Gui, ViewTarget.Gui),
        ("tp", views.TpHand, ViewTarget.Hand),
        ("ground", views.Ground, ViewTarget.Ground),
      }
        .Where(t =>
          ViewGates.TransformKey(t.Item2)
          != ViewGates.TransformKey(ViewMatrices.Transform(c, t.Item3))
        )
        .Select(t => t.Item1),
    ];
    return Emit(
      world,
      directory,
      view,
      ViewGates.Measured(group, views),
      shape,
      own?.ToDictionary(t => t.Key, t => t.Value.Base.ToString())
        ?? new Dictionary<string, string>(),
      views,
      computed,
      variable
    );
  }

  private static string? Root(string variable) =>
    Environment.GetEnvironmentVariable(variable) is { Length: > 0 } root
      ? root
      : null;

  private static string Emit(
    TestWorld world,
    string directory,
    string view,
    ViewGroup group,
    CompositeShape? shape,
    Dictionary<string, string> textures,
    ItemViewSet views,
    string[] computed,
    string variable
  ) {
    string folder = Path.Combine(Root(variable)!, directory);
    Directory.CreateDirectory(folder);
    GroupMetrics metrics = ViewGates.Measure(world, group);
    IReadOnlyList<GateReading> gates =
    [
      .. ViewGates.Readings(ViewBands.Embedded, group, metrics),
      .. ViewGates.Rules(world, group),
    ];
    double[] footprint = ViewGates.Footprint(group);
    double[]? grip = group.IsBlock
      ? null
      : ViewGates.GripPoint(
        group.Hand,
        SeraphPose.At(world.Api, ViewGates.HeldClip(world, group.Held))
      );

    var w = new StringBuilder();
    w.Append("{\n");
    w.Append("  \"format\": \"item-views/1\",\n");
    w.Append($"  \"game\": {Str(GameVersion.ShortGameVersion)},\n");
    w.Append($"  \"view\": {Str(view)},\n");
    w.Append(
      $"  \"codes\": {List(group.Codes.Order(StringComparer.Ordinal), 2)},\n"
    );
    w.Append($"  \"type\": {Str(group.IsBlock ? "block" : "item")},\n");
    w.Append($"  \"kind\": {Str(group.Kind.ToString().ToLowerInvariant())},\n");
    w.Append($"  \"computed\": {List(computed, 2)},\n");
    w.Append("  \"shape\": {\n");
    w.Append($"    \"base\": {Str(shape?.Base.ToString())},\n");
    w.Append(
      $"    \"selectiveElements\": {(shape?.SelectiveElements == null ? "null" : List(shape.SelectiveElements, 4))},\n"
    );
    w.Append(
      $"    \"overlays\": {List((shape?.Overlays ?? []).Select(o => o.Base.ToString()), 4)},\n"
    );
    w.Append($"    \"rotateX\": {Num(shape?.rotateX ?? 0)},\n");
    w.Append($"    \"rotateY\": {Num(shape?.rotateY ?? 0)},\n");
    w.Append($"    \"rotateZ\": {Num(shape?.rotateZ ?? 0)}\n");
    w.Append("  },\n");
    w.Append("  \"textures\": {");
    w.Append(
      textures.Count == 0
        ? "},\n"
        : "\n"
          + string.Join(
            ",\n",
            textures
              .OrderBy(t => t.Key, StringComparer.Ordinal)
              .Select(t => $"    {Str(t.Key)}: {Str(t.Value)}")
          )
          + "\n  },\n"
    );
    w.Append("  \"box\": {\n");
    w.Append($"    \"from\": {Vec(group.Box.From)},\n");
    w.Append($"    \"to\": {Vec(group.Box.To)}\n");
    w.Append("  },\n");
    w.Append($"  \"gui\": {Transform(group.Gui)},\n");
    w.Append($"  \"tp\": {Transform(group.Hand)},\n");
    w.Append($"  \"ground\": {Transform(group.Ground)},\n");
    w.Append("  \"clips\": {\n");
    w.Append($"    \"idle\": {Str(views.IdleClip)},\n");
    w.Append($"    \"ready\": {Str(views.ReadyClip)},\n");
    w.Append($"    \"hit\": {Str(views.HitClip)},\n");
    w.Append($"    \"use\": {Str(views.UseClip)}\n");
    w.Append("  },\n");
    w.Append(
      $"  \"footprint\": [{string.Join(", ", footprint.Select(Num))}],\n"
    );
    w.Append(
      $"  \"grip\": {(grip == null ? "null" : $"[{string.Join(", ", grip.Select(Num))}]")},\n"
    );
    w.Append("  \"gates\": [\n");
    w.Append(string.Join(",\n", gates.Select(Gate)));
    w.Append("\n  ]\n}\n");

    string path = Path.Combine(
      folder,
      view.Replace(':', '-').Replace('/', '-') + ".views.json"
    );
    File.WriteAllText(path, w.ToString());
    return path;
  }

  private static string Gate(GateReading g) =>
    "    {\n"
    + $"      \"gate\": {Str(g.Gate)},\n"
    + $"      \"name\": {Str(g.Name)},\n"
    + $"      \"target\": {Str(g.Target)},\n"
    + $"      \"value\": {Num(g.Value)},\n"
    + $"      \"min\": {(g.Min is { } min ? Num(min) : "null")},\n"
    + $"      \"max\": {(double.IsPositiveInfinity(g.Max) ? "null" : Num(g.Max))},\n"
    + $"      \"ends\": {(g.Ends == null ? "null" : $"[{string.Join(", ", g.Ends.Select(Str))}]")},\n"
    + $"      \"pass\": {(g.Pass ? "true" : "false")}\n"
    + "    }";

  private static string Transform(ModelTransform t) =>
    "{\n"
    + $"    \"translation\": {Xyz(t.Translation.X, t.Translation.Y, t.Translation.Z)},\n"
    + $"    \"rotation\": {Xyz(t.Rotation.X, t.Rotation.Y, t.Rotation.Z)},\n"
    + $"    \"origin\": {Xyz(t.Origin.X, t.Origin.Y, t.Origin.Z)},\n"
    + $"    \"scaleXyz\": {Xyz(t.ScaleXYZ.X, t.ScaleXYZ.Y, t.ScaleXYZ.Z)},\n"
    + $"    \"rotate\": {(t.Rotate ? "true" : "false")}\n"
    + "  }";

  private static string Xyz(double x, double y, double z) =>
    $"{{ \"x\": {Num(x)}, \"y\": {Num(y)}, \"z\": {Num(z)} }}";

  private static string Vec(Vec3d v) => $"[{Num(v.X)}, {Num(v.Y)}, {Num(v.Z)}]";

  private static string List(IEnumerable<string> items, int indent) {
    string[] all = [.. items];
    if (all.Length == 0)
      return "[]";
    string pad = new(' ', indent + 2);
    return "[\n"
      + string.Join(",\n", all.Select(i => pad + Str(i)))
      + "\n"
      + new string(' ', indent)
      + "]";
  }

  private static string Str(string? s) =>
    s == null ? "null" : JsonConvert.ToString(s);

  private static string Num(double v) =>
    (Math.Round(v, 4) + 0.0).ToString("0.0000", CultureInfo.InvariantCulture);
}
