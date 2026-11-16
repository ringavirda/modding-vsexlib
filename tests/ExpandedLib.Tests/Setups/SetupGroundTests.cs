#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>A setup file's <c>ground</c> and the <c>dig</c> step in the harness (setups.md, The
/// file; Steps the setups add).</summary>
[Collection("Setups")]
public class SetupGroundTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private const string Slab = "setupfixture:slab";

  private const string Soil = "game:soil-medium-none";

  private static readonly string Fixture = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupFixture"
  );

  private static SetupFile Inline(
    string steps,
    string? ground = "2",
    string? layout = null
  ) =>
    SetupFile.Parse(
      "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupfixture\"], "
        + "\"plot\": [2, 4, 2], "
        + (ground == null ? "" : "\"ground\": " + ground + ", ")
        + (layout == null ? "" : "\"layout\": " + layout + ", ")
        + "\"steps\": "
        + steps
        + ", \"seconds\": 10, \"record\": {}}",
      "t.json"
    );

  private static SetupScene Build(SetupFile file) =>
    SetupScene.Build(file, [Fixture]);

  private static string Refused(SetupFile file) =>
    Assert.Throws<InvalidOperationException>(() => Build(file)).Message;

  private static string CodeAt(SetupScene scene, int x, int y, int z) =>
    scene.World.GetBlock(new BlockPos(x, y, z)).Code.ToString();

  // Fails when the harness does not lay the ground: the place goes into air.
  [Fact]
  public void A_place_into_ground_that_was_not_dug_fails_naming_the_cell()
  {
    string message = Refused(
      Inline($"[{{\"place\": {{\"code\": \"{Slab}\", \"at\": [0, -1, 0]}}}}]")
    );

    Assert.Contains("steps[0]", message);
    Assert.Contains($"(0, -1, 0) holds {Soil}, not air", message);
  }

  // Fails when the ground stops short of its depth or the footprint's edge, or runs past either.
  [Fact]
  public void The_ground_fills_the_footprint_to_its_depth_and_no_further()
  {
    using SetupScene scene = Build(Inline("[]"));

    Assert.Equal(Soil, CodeAt(scene, -2, -1, -2));
    Assert.Equal(Soil, CodeAt(scene, 2, -2, 2));
    Assert.Equal("game:air", CodeAt(scene, 0, -3, 0));
    Assert.Equal("game:air", CodeAt(scene, 3, -1, 0));
    Assert.Equal("game:air", CodeAt(scene, 0, -1, 3));
    Assert.Equal("game:air", CodeAt(scene, 0, 0, 0));
  }

  // Fails when dig leaves a cell of its box standing, digs past the box, or a place into the dug
  // box is refused.
  [Fact]
  public void A_dug_box_is_air_and_takes_a_place()
  {
    using SetupScene scene = Build(
      Inline(
        "[{\"dig\": {\"id\": \"pit\", \"from\": [-1, -2, -1], \"to\": [0, -1, 0]}}, "
          + "{\"dig\": {\"at\": [2, -1, 2]}}, "
          + $"{{\"place\": {{\"code\": \"{Slab}\", \"at\": [0, -2, 0]}}}}]"
      )
    );

    Assert.Equal(8, scene.CellsOf("pit").Count);
    Assert.All(
      scene.CellsOf("pit").Where(c => !(c.X == 0 && c.Y == -2 && c.Z == 0)),
      c => Assert.Equal("game:air", CodeAt(scene, c.X, c.Y, c.Z))
    );
    Assert.Equal(Slab, CodeAt(scene, 0, -2, 0));
    Assert.Equal("game:air", CodeAt(scene, 2, -1, 2));
    Assert.Equal(Soil, CodeAt(scene, 1, -1, 0));
    Assert.Equal(Soil, CodeAt(scene, -2, -2, -1));
    Assert.Equal(Soil, CodeAt(scene, 2, -2, 2));
  }

  // Fails when the layout places into the ground without digging its cell first.
  [Fact]
  public void A_layout_cell_under_y_zero_is_dug_before_it_is_placed()
  {
    using SetupScene scene = Build(
      Inline(
        "[]",
        layout: $"{{\"legend\": {{\"S\": {{\"code\": \"{Slab}\"}}}}, "
          + "\"layers\": {\"-1\": [\"S\"]}}"
      )
    );

    Assert.Equal(Slab, CodeAt(scene, 0, -1, 0));
    Assert.Equal(Soil, CodeAt(scene, 1, -1, 0));
  }

  // Fails when the dig step's y 0 guard is dropped: the box is in depth and footprint.
  [Fact]
  public void A_dig_at_y_zero_is_refused()
  {
    string message = Refused(
      Inline("[{\"dig\": {\"from\": [0, -1, 0], \"to\": [0, 0, 0]}}]")
    );

    Assert.Contains("steps[0] (dig)", message);
    Assert.Contains("(0, 0, 0) lies at or above y 0", message);
  }

  // Fails when the dig step's depth guard is dropped: the cell is under y 0 and in the footprint.
  [Fact]
  public void A_dig_below_the_stated_depth_is_refused()
  {
    string message = Refused(Inline("[{\"dig\": {\"at\": [0, -3, 0]}}]"));

    Assert.Contains("(0, -3, 0) lies below the ground's 2 block(s)", message);
  }

  // Fails when the dig step's footprint guard is dropped: the cell is under y 0 and in depth.
  [Fact]
  public void A_dig_outside_the_footprint_is_refused()
  {
    string message = Refused(Inline("[{\"dig\": {\"at\": [0, -1, -3]}}]"));

    Assert.Contains("(0, -1, -3) lies outside the plot's footprint", message);
  }

  // Fails when the dig step's no-ground guard is dropped: the depth guard then refuses instead.
  [Fact]
  public void A_dig_in_a_file_with_no_ground_is_refused()
  {
    string message = Refused(
      Inline("[{\"dig\": {\"at\": [0, -1, 0]}}]", ground: null)
    );

    Assert.Contains("digs only in a file that states \"ground\"", message);
  }

  // Fails when a file with no ground has ground laid under its plot.
  [Fact]
  public void A_file_with_no_ground_lays_nothing_under_y_zero()
  {
    using SetupScene scene = Build(Inline("[]", ground: null));

    Assert.Equal("game:air", CodeAt(scene, 0, -1, 0));
    Assert.Equal("game:air", CodeAt(scene, 2, -1, 2));
  }

  // Fails when the reader's bounds on ground move: 1 and 16 read, 0, 17 and a fraction refused.
  [Theory]
  [InlineData("1", null)]
  [InlineData("16", null)]
  [InlineData("0", "takes a whole number of blocks from 1 to 16")]
  [InlineData("17", "takes a whole number of blocks from 1 to 16")]
  [InlineData("1.5", "ground")]
  public void The_reader_takes_a_ground_of_1_to_16_blocks(
    string ground,
    string? refusal
  )
  {
    if (refusal == null)
    {
      Assert.Equal(int.Parse(ground), Inline("[]", ground).Model.Ground);
      return;
    }
    var e = Assert.ThrowsAny<Exception>(() => Inline("[]", ground));
    Assert.Contains(refusal, e.Message);
  }

  // Fails when dig takes both forms at once, or neither.
  [Theory]
  [InlineData("{\"at\": [0, -1, 0], \"from\": [0, -1, 0], \"to\": [0, -1, 0]}")]
  [InlineData("{\"from\": [0, -1, 0]}")]
  [InlineData("{}")]
  public void A_dig_takes_at_or_from_and_to(string body)
  {
    var e = Assert.ThrowsAny<Exception>(() => Inline($"[{{\"dig\": {body}}}]"));

    Assert.Contains("takes \"at\", or \"from\" and \"to\"", e.Message);
  }
}
#endif
