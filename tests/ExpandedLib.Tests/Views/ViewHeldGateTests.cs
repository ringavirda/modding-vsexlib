using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The rules G15 hand.sheet and G16 hand.up, on the views fixture mod and the whole vanilla game.</summary>
[Collection("ItemViews")]
public class ViewHeldGateTests {
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

  private static ViewGroup GroupOf(string domain, string code) =>
    ViewGates.Groups(World, domain).Single(g => g.Codes.Contains(code));

  // The fixture's 1 voxel sheet thinned to half a voxel, held with the hand rotation (x, y, 0).
  private static ViewGroup Sheet(double thickness, float x, float y) {
    ViewGroup plate = GroupOf("viewsfixture", "viewsfixture:uprightsheet");
    ModelTransform hand = plate.Hand.Clone();
    hand.Rotation = new FastVec3f(x, y, 0);
    return plate with {
      Box = plate.Box with {
        To = new Vec3d(plate.Box.To.X, plate.Box.To.Y, thickness),
      },
      Hand = hand,
    };
  }

  private static ViewGroup Block(float x) {
    ViewGroup hay = GroupOf("game", "game:hay-normal-ud");
    ModelTransform hand = hay.Hand.Clone();
    hand.Rotation = new FastVec3f(x, hand.Rotation.Y, hand.Rotation.Z);
    return hay with { Hand = hand };
  }

  private static string[] Failing(ViewGroup group) =>
    [
      .. ViewGates
        .Rules(World, group)
        .Where(r => !r.Pass)
        .Select(r => r.Gate + (r.Target == "fp" ? "fp" : "")),
    ];

  private static double Value(ViewGroup group, string name) =>
    ViewGates.Rules(World, group).Single(r => r.Name == name).Value;

  // Fails when G15 is not read: the sheet turned a quarter about the vertical then passes.
  [Fact]
  public void A_sheet_turned_edge_on_fails_G15() {
    Assert.Empty(Failing(Sheet(0.5, 270, 210)));

    ViewGroup turned = Sheet(0.5, 270, 120);

    Assert.Equal(["G15", "G15fp"], Failing(turned));
    Assert.True(Value(turned, "hand.sheet") > ViewGates.SheetDegrees);
    Assert.True(Value(turned, "hand.sheet.fp") > ViewGates.SheetDegrees);
  }

  // Fails when G15 measures the wrong axis of the box: the sheet's face, at 13 and 21 degrees from the camera, then reads
  // as the angle of an edge and the values differ.
  [Fact]
  public void G15_reads_the_angle_of_the_face_to_the_camera() {
    ViewGroup facing = Sheet(0.5, 270, 210);

    Assert.InRange(Value(facing, "hand.sheet"), 13 - 1, 13 + 1);
    Assert.InRange(Value(facing, "hand.sheet.fp"), 21 - 1, 21 + 1);
  }

  // Fails when the sheet bound is inclusive or removed: the 1 voxel fixture then reads G15, and its upright hold
  // fails it.
  [Fact]
  public void A_box_of_a_voxel_is_not_a_sheet() {
    ViewGroup plate = GroupOf("viewsfixture", "viewsfixture:uprightsheet");

    Assert.DoesNotContain(ViewGates.Rules(World, plate), r => r.Gate == "G15");
    Assert.Contains(
      ViewGates.Rules(World, Sheet(0.99, 270, 210)),
      r => r.Gate == "G15"
    );
  }

  // Fails when G16 is not read, or reads the world's up for the block's: a hay block tipped a quarter in the hand then
  // passes.
  [Fact]
  public void A_block_held_on_its_side_fails_G16() {
    Assert.Empty(Failing(Block(0)));

    ViewGroup tipped = Block(90);

    Assert.Equal(["G16", "G16fp"], Failing(tipped));
    Assert.InRange(Value(tipped, "hand.up"), 85 - 1, 85 + 1);
    Assert.InRange(Value(tipped, "hand.up.fp"), 85 - 1, 85 + 1);
  }

  // Fails when G16 is read on an item: the fixture sheet then carries a hand.up row.
  [Fact]
  public void An_item_has_no_G16_and_a_thick_block_no_G15() {
    Assert.DoesNotContain(
      ViewGates.Rules(World, Sheet(0.5, 270, 210)),
      r => r.Gate == "G16"
    );
    Assert.DoesNotContain(
      ViewGates.Rules(World, Block(0)),
      r => r.Gate == "G15"
    );
  }
}
