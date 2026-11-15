using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The views a declared pile gets, on the iiex rivet and bolt heap shapes.</summary>
[Collection("ItemViews")]
public class ItemViewsPileTests {
  private const double Tolerance = 1e-3;

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

  private static ViewBandData Bands => ViewBands.Embedded;

  private static Item ItemOf(string code) =>
    World.World.GetItem(new AssetLocation(code));

  private static void AssertTransform(
    ModelTransform actual,
    double[] translation,
    double[] rotation,
    double[] origin,
    double scale
  ) {
    float[] t =
    [
      actual.Translation.X,
      actual.Translation.Y,
      actual.Translation.Z,
    ];
    float[] r = [actual.Rotation.X, actual.Rotation.Y, actual.Rotation.Z];
    float[] o = [actual.Origin.X, actual.Origin.Y, actual.Origin.Z];
    for (int k = 0; k < 3; k++) {
      Assert.Equal(translation[k], t[k], Tolerance);
      Assert.Equal(rotation[k], r[k], Tolerance);
      Assert.Equal(origin[k], o[k], Tolerance);
    }
    Assert.Equal(scale, actual.ScaleXYZ.X, Tolerance);
  }

  // Fails when the pile's grip is the plate kind's: the hand translation moves by more than 1e-3.
  // The tp and ground values are those solved for the vanilla nails' grip; the gui values are the rule at the plate's slot span 0.8.
  [Theory]
  [InlineData(
    "viewsfixture:rivetheap",
    -0.7467,
    -0.1112,
    -0.5965,
    1.7657,
    -1.4813,
    2.1944,
    0.0343,
    0.5439,
    0.0517,
    0.4746
  )]
  [InlineData(
    "viewsfixture:boltheap",
    -0.6919,
    -0.0541,
    -0.5765,
    1.4057,
    -6.2442,
    3.0786,
    0.0169,
    0.6164,
    0.0256,
    0.4162
  )]
  public void The_pass_lays_a_pile_out_by_the_pile_rule(
    string code,
    double tpX,
    double tpY,
    double tpZ,
    double guiX,
    double guiY,
    double guiScale,
    double groundY,
    double groundOriginX,
    double groundOriginY,
    double groundOriginZ
  ) {
    Item item = ItemOf(code);

    Assert.Equal(ItemViewKind.Pile, ItemViews.Compute(World.Api, item)!.Kind);
    AssertTransform(
      item.TpHandTransform,
      [tpX, tpY, tpZ],
      [94, 0, 0],
      [0.5, 0.1, 0.5],
      0.75
    );
    AssertTransform(
      item.GuiTransform,
      [guiX, guiY, 0],
      [60, 45, 0],
      [0.5, 0.05, 0.5],
      guiScale
    );
    Assert.True(item.GuiTransform.Rotate);
    AssertTransform(
      item.GroundTransform,
      [0, groundY, 0],
      [0, 0, 0],
      [groundOriginX, groundOriginY, groundOriginZ],
      3.31
    );
  }

  // Fails when the shape overload drops the pile branch: it then throws for a kind with no fixed reference.
  [Fact]
  public void A_pile_shape_computes_as_the_item_does() {
    Item item = ItemOf("viewsfixture:rivetheap");

    ItemViewSet views = ItemViews.Compute(
      World.Api,
      item.Shape,
      ItemViewKind.Pile
    );

    Assert.Equal(item.TpHandTransform.Translation, views.TpHand.Translation);
    Assert.Equal(item.GuiTransform.ScaleXYZ, views.Gui.ScaleXYZ);
  }

  // Fails when the pile is judged against the plate's slot fill: both heaps fill about a third of the slot, under the plate band.
  [Theory]
  [InlineData("viewsfixture:rivetheap")]
  [InlineData("viewsfixture:boltheap")]
  public void A_pile_passes_every_view_gate(string code) {
    ViewGroup group = ViewGates.GroupOf(World, ItemOf(code))!;

    GroupMetrics metrics = ViewGates.Measure(World, group);

    Assert.Equal(ItemViewKind.Pile, group.Kind);
    Assert.Empty(ViewGates.Judge(Bands, group, metrics));
  }
}
