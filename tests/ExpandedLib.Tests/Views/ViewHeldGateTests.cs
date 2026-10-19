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

  // The vanilla barrel's hold, upright, with its hand z rotation moved by dz degrees.
  private static ViewGroup Barrel(float dz) {
    ViewGroup barrel = GroupOf("game", "game:barrel");
    ModelTransform hand = barrel.Hand.Clone();
    hand.Rotation = new FastVec3f(
      hand.Rotation.X,
      hand.Rotation.Y,
      hand.Rotation.Z + dz
    );
    return barrel with { Hand = hand };
  }

  // Hay held with the game's default block hold, which carries it on its side and authors no hold of its own.
  private static ViewGroup DefaultHeldHay() =>
    GroupOf("game", "game:hay-normal-ud");

  // Hay with a hold of its own: the default block hold turned on its side.
  private static ViewGroup OwnHeldHay() {
    ViewGroup hay = DefaultHeldHay();
    ModelTransform hand = hay.Hand.Clone();
    hand.Rotation = new FastVec3f(0, 0, 70);
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

  private static bool Passes(ViewGroup group, string name) =>
    ViewGates.Rules(World, group).Single(r => r.Name == name).Pass;

  // Fails when G15 is not read: the sheet turned a quarter about the vertical then passes.
  [Fact]
  public void A_sheet_turned_edge_on_fails_G15() {
    Assert.True(Passes(Sheet(0.5, 270, 210), "hand.sheet"));

    ViewGroup turned = Sheet(0.5, 270, 120);

    Assert.Equal(["G15", "G15fp"], Failing(turned));
    Assert.True(Value(turned, "hand.sheet") > ViewGates.SheetDegrees);
    Assert.True(Value(turned, "hand.sheet.fp") > ViewGates.SheetDegrees);
  }

  // Fails when G15 measures the wrong axis of the box, or in first person against the forward axis instead of the line
  // from the eye to the sheet: the third-person value then reads another angle and the first-person one reads 21.
  [Fact]
  public void G15_reads_the_angle_of_the_face_to_the_line_of_sight() {
    ViewGroup facing = Sheet(0.5, 270, 210);

    Assert.InRange(Value(facing, "hand.sheet"), 13 - 1, 13 + 1);
    Assert.InRange(Value(facing, "hand.sheet.fp"), 75.6 - 1, 75.6 + 1);
  }

  // Fails when SheetDegrees moves off 40: the sheets at 38.8 and 40.8 degrees in third person, and at 37.7 and 41.5 in
  // first, then pass or fail together.
  [Fact]
  public void G15_limit_is_forty_degrees() {
    Assert.True(Passes(Sheet(0.5, 270, 4), "hand.sheet"));
    Assert.False(Passes(Sheet(0.5, 270, 2), "hand.sheet"));
    Assert.True(Passes(Sheet(0.5, 105, 0), "hand.sheet.fp"));
    Assert.False(Passes(Sheet(0.5, 100, 0), "hand.sheet.fp"));
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

  // Fails when G16 is not read, or reads the angle to the hold's up instead of to world vertical: hay with a hold of
  // its own turned on its side then reads 0 and passes, and the barrel's upright hold fails.
  [Fact]
  public void A_block_with_its_own_hold_on_its_side_fails_G16() {
    Assert.Empty(Failing(Barrel(0)));

    ViewGroup tipped = OwnHeldHay();

    Assert.Equal(["G16", "G16fp"], Failing(tipped));
    Assert.True(Value(tipped, "hand.up") > ViewGates.HeldUpDegrees);
    Assert.True(Value(tipped, "hand.up.fp") > ViewGates.HeldUpDegrees);
  }

  // Fails when the default-hold skip is removed, which reads G16 on hay in vanilla's default block hold (69 and 88
  // degrees), or widened to every block, which drops the rows of the barrel and of a hold turned on its side.
  [Fact]
  public void A_block_in_the_default_hold_reads_no_G16() {
    Assert.DoesNotContain(
      ViewGates.Rules(World, DefaultHeldHay()),
      r => r.Gate == "G16"
    );
    Assert.Contains(ViewGates.Rules(World, Barrel(0)), r => r.Gate == "G16");
    Assert.Contains(ViewGates.Rules(World, OwnHeldHay()), r => r.Gate == "G16");
  }

  // Fails when the skip compares the whole transform: hay in the default block hold's rotation, rescaled and moved as
  // the item views do, then reads G16.
  [Fact]
  public void A_block_in_the_default_rotation_rescaled_reads_no_G16() {
    ViewGroup hay = DefaultHeldHay();
    ModelTransform hand = hay.Hand.Clone();
    hand.Scale = hand.ScaleXYZ.X * 0.5f;
    hand.Translation = new FastVec3f(0.3f, -0.2f, 0.1f);

    Assert.DoesNotContain(
      ViewGates.Rules(World, hay with { Hand = hand }),
      r => r.Gate == "G16"
    );
  }

  // Fails when HeldUpDegrees moves off 20: the barrel at 18.3 and 22.3 degrees in third person, and at 17.7 and 21.7 in
  // first, then pass or fail together.
  [Fact]
  public void G16_limit_is_twenty_degrees() {
    Assert.True(Passes(Barrel(-14), "hand.up"));
    Assert.False(Passes(Barrel(-18), "hand.up"));
    Assert.True(Passes(Barrel(-2), "hand.up.fp"));
    Assert.False(Passes(Barrel(2), "hand.up.fp"));
  }

  // Fails when G16 is read on an item: the fixture sheet then carries a hand.up row.
  [Fact]
  public void An_item_has_no_G16_and_a_thick_block_no_G15() {
    Assert.DoesNotContain(
      ViewGates.Rules(World, Sheet(0.5, 270, 210)),
      r => r.Gate == "G16"
    );
    Assert.DoesNotContain(
      ViewGates.Rules(World, Barrel(0)),
      r => r.Gate == "G15"
    );
  }
}
