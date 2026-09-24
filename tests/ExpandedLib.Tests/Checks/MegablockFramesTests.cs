using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="MegablockFrames.Misfit"/> over a planted shape three blocks long on x and
/// the footprints that do and do not reserve it.</summary>
public class MegablockFramesTests {
  private const string Shape = """
    { "elements": [ { "name": "body", "from": [0, 0, 0], "to": [48, 16, 16] } ] }
    """;

  /// <summary>A principal reserving the cells east of it, <paramref name="length"/> blocks in all.</summary>
  private sealed class Principal(int length) : IFillerHost {
    public JsonObject FillerOffsets {
      get {
        var cells = new JArray();
        for (int x = 1; x < length; x++)
          cells.Add(
            new JObject {
              ["x"] = x,
              ["y"] = 0,
              ["z"] = 0,
            }
          );
        return new JsonObject(cells);
      }
    }
  }

  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_shape_spun_by_a_different_angle_than_its_footprint_is_reported() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    string? misfit = MegablockFrames.Misfit(shape, 90, new Principal(3), 0);

    Assert.NotNull(misfit);
    Assert.StartsWith("draws z over [", misfit);
    Assert.Contains("but reserves [-0.5, 0.5]", misfit);
  }

  [Fact]
  [PlantedDefect(typeof(MegablockFrames), nameof(MegablockFrames.Misfit))]
  public void A_shape_longer_than_its_footprint_is_reported() {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    string? misfit = MegablockFrames.Misfit(shape, 0, new Principal(2), 0);

    Assert.NotNull(misfit);
    Assert.StartsWith(
      "draws x over [-0.5, 2.5] but reserves [-0.5, 1.5]",
      misfit
    );
  }

  [Theory]
  [InlineData(0)]
  [InlineData(90)]
  [InlineData(180)]
  [InlineData(270)]
  public void A_shape_and_footprint_turned_together_fit(int angle) {
    using var files = new PlantedFiles();
    string shape = files.Write("shapes/long.json", Shape);

    Assert.Null(MegablockFrames.Misfit(shape, angle, new Principal(3), angle));
  }
}
