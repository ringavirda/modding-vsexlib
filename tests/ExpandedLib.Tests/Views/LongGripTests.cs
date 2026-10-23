using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>G17 hand.left over the long items of the views long fixture mod: the left grip of the carry's band against
/// the left hand at frame 0 of the carry.</summary>
[Collection("ItemViews")]
public class LongGripTests {
  private static string Fixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Helpers",
      "Views",
      "Fixtures",
      "viewslong"
    );

  private static TestWorld World => GameWorlds.Get([Fixture]);

  private static ViewGroup[] Longs() =>
    [
      .. ViewGates
        .Groups(World, "viewslong")
        .Where(g =>
          g.IsLong && !g.Codes.Contains("viewslong:ownclips-straight2")
        ),
    ];

  private static GateReading LeftGrip(ViewGroup group) =>
    ViewGates.Rules(World, group).Single(r => r.Gate == "G17");

  // Fails when a long item is not read for G17, or its grip is taken at the right hand's station.
  [Fact]
  public void Every_long_items_left_grip_lies_on_its_left_hand() {
    ViewGroup[] longs = Longs();
    Assert.True(longs.Length >= 8, $"{longs.Length} long groups");
    foreach (ViewGroup group in longs) {
      GateReading g = LeftGrip(group);
      Assert.Equal("hand.left", g.Name);
      Assert.True(g.Pass, $"{string.Join(",", group.Codes)}: {g.Detail}");
    }
  }

  // Fails when the limit is not read for long items: a hand moved a voxel then passes.
  [Fact]
  public void A_long_item_with_its_hand_moved_a_voxel_fails_G17() {
    foreach (ViewGroup group in Longs()) {
      ModelTransform hand = group.Hand.Clone();
      hand.Translation = new FastVec3f(
        hand.Translation.X + 1f / 16,
        hand.Translation.Y,
        hand.Translation.Z
      );
      GateReading g = LeftGrip(group with { Hand = hand });
      Assert.False(g.Pass, string.Join(",", group.Codes));
      Assert.Equal(1, g.Value, 0.05);
    }
  }

  // Fails when an item that names its own clips is read against the carry's stations: its grip then misses by voxels.
  [Fact]
  public void A_long_item_with_its_own_clips_has_no_G17() {
    ViewGroup own = ViewGates
      .Groups(World, "viewslong")
      .Single(g => g.Codes.Contains("viewslong:ownclips-straight2"));
    Assert.True(own.IsLong);
    Assert.DoesNotContain(ViewGates.Rules(World, own), r => r.Gate == "G17");
  }
}
