using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The dump file of a group's views, on the whole vanilla game with the views fixture mod.</summary>
[Collection("ItemViews")]
public class ViewDumpTests {
  private const string Variable = "EXLIB_TEST_VIEW_DUMP";

  private static string Fixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Helpers",
      "Views",
      "Fixtures",
      "viewsfixture"
    );

  private static TestWorld World => GameWorlds.Get([Fixture]);

  private static Item Sheet =>
    World.World.GetItem(new AssetLocation("viewsfixture:uprightsheet"));

  private static ItemViewSet SheetViews => ItemViews.Compute(World.Api, Sheet)!;

  private static T With<T>(string variable, string? value, Func<T> run) {
    string? before = Environment.GetEnvironmentVariable(variable);
    Environment.SetEnvironmentVariable(variable, value);
    try {
      return run();
    } finally {
      Environment.SetEnvironmentVariable(variable, before);
    }
  }

  private static string TempDir() =>
    Directory.CreateTempSubdirectory("viewdump").FullName;

  private static JObject Read(string? path) =>
    JObject.Parse(File.ReadAllText(path!));

  // Fails when the footprint is taken at GUI scale 2: the slot bounds then move off the scale-1 matrices.
  [Fact]
  public void Dump_writes_every_key_in_order() {
    string dir = TempDir();
    ItemViewSet views = SheetViews;
    JObject file = Read(
      With(
        Variable,
        dir,
        () =>
          ViewDump.Write(
            World,
            "",
            "viewsfixture:uprightsheet",
            Sheet,
            views,
            Variable
          )
      )
    );
    Assert.Equal(
      [
        "format",
        "game",
        "view",
        "codes",
        "type",
        "kind",
        "computed",
        "shape",
        "textures",
        "box",
        "gui",
        "tp",
        "ground",
        "clips",
        "footprint",
        "grip",
        "gates",
      ],
      file.Properties().Select(p => p.Name)
    );
    Assert.Equal("item-views/1", (string?)file["format"]);
    Assert.Equal("plate", (string?)file["kind"]);

    Mat4 slot = ViewMatrices.Slot(views.Gui, false, 1, 24, 24);
    Vec3d[] p = ViewGates
      .Corners(CollectibleViews.BoxOf(World.Api, Sheet)!)
      .Select(slot.Apply)
      .ToArray();
    double[] expected =
    [
      p.Min(v => v.X),
      p.Min(v => v.Y),
      p.Max(v => v.X),
      p.Max(v => v.Y),
    ];
    double[] footprint = file["footprint"]!.Select(t => (double)t).ToArray();
    for (int i = 0; i < 4; i++)
      Assert.Equal(expected[i], footprint[i], 0.001);

    double[] grip = file["grip"]!.Select(t => (double)t).ToArray();
    Assert.Equal(views.Grip!.X, grip[0], 0.05);
    Assert.Equal(views.Grip.Y, grip[1], 0.05);
    Assert.Equal(views.Grip.Z, grip[2], 0.05);
  }

  // Fails when the gates measure the collectible's resolved transforms: G7 then reads the default's hand scale, not the written one.
  [Fact]
  public void Dump_gates_measure_the_views_it_writes() {
    ItemViewSet computed = SheetViews;
    double resolved = ViewMatrices.Transform(Sheet, ViewTarget.Hand).ScaleXYZ.X;
    ModelTransform wide = computed.TpHand.Clone();
    float scale = (float)(computed.TpHand.ScaleXYZ.X * 3);
    wide.ScaleXYZ = new FastVec3f(scale, scale, scale);
    Assert.NotEqual(resolved, scale, 0.001);
    ItemViewSet views = computed with { TpHand = wide };

    JObject file = Read(
      With(
        Variable,
        TempDir(),
        () =>
          ViewDump.Write(
            World,
            "",
            "viewsfixture:uprightsheet",
            Sheet,
            views,
            Variable
          )
      )
    );
    JToken g7 = file["gates"]!.Single(g => (string?)g["gate"] == "G7");
    Assert.Equal(scale, (double)g7["value"]!, 0.001);
    Assert.False((bool)g7["pass"]!);

    ViewGroup group = ViewGates.Measured(
      ViewGates.GroupOf(World, Sheet)!,
      views
    );
    string[] failing =
    [
      .. ViewGates
        .Readings(ViewBands.Embedded, group, ViewGates.Measure(World, group))
        .Where(r => !r.Pass)
        .Select(r => r.Gate + " " + r.Name),
    ];
    Assert.Contains("G7 hand.size", failing);
    Assert.Equal(
      failing,
      file["gates"]!
        .Where(g => !(bool)g["pass"]!)
        .Select(g => (string)g["gate"]! + " " + (string)g["name"]!)
    );
  }

  // Fails when the variable check is removed: the unset variable then still writes.
  [Fact]
  public void Dump_writes_only_on_request() {
    string dir = TempDir();
    string? path = With(
      Variable,
      null,
      () =>
        ViewDump.Write(
          World,
          dir,
          "viewsfixture:uprightsheet",
          Sheet,
          SheetViews,
          Variable
        )
    );
    Assert.Null(path);
    Assert.Empty(Directory.GetFileSystemEntries(dir));
  }

  // Fails when the "/" of the view stays in the file name: the file then lands in a folder of the family's name.
  [Fact]
  public void Dump_writes_a_stage_file() {
    string dir = TempDir();
    string? path = With(
      Variable,
      dir,
      () =>
        ViewDump.Write(
          World,
          "",
          "viewsfixture:uprightsheet@plate/Top",
          Sheet.Shape,
          new Dictionary<string, AssetLocation> {
            ["tex"] = new("viewsfixture:item/tex"),
          },
          SheetViews,
          Variable
        )
    );
    Assert.Equal(
      Path.Combine(dir, "viewsfixture-uprightsheet@plate-Top.views.json"),
      path
    );
    JObject file = Read(path);
    Assert.Equal("viewsfixture:uprightsheet@plate/Top", (string?)file["view"]);
    Assert.Equal("viewsfixture:item/tex", (string?)file["textures"]!["tex"]);
  }

  // Fails when the default variable name is misspelt: ViewGates then writes nothing under EX_ITEM_VIEWS, which a run may preset to keep the files.
  [Fact]
  public void Dump_writes_under_the_default_variable() {
    string dir = Environment.GetEnvironmentVariable("EX_ITEM_VIEWS")
      is { Length: > 0 } set
      ? set
      : TempDir();
    IReadOnlyList<string> paths = With(
      "EX_ITEM_VIEWS",
      dir,
      () => ViewGates.Dump(World, ViewGates.Groups(World, "viewsfixture"))
    );
    Assert.Contains(
      Path.Combine(dir, "viewsfixture-uprightsheet.views.json"),
      paths
    );
    Assert.All(paths, p => Assert.True(File.Exists(p)));
  }
}
