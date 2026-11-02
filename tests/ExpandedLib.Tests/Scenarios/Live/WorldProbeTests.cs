using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The probe's argument parsing, tile walk and report JSON; the generation, the chunk reads
/// and the files only the live run proves.</summary>
public class WorldProbeTests {
  /// <summary>Fails when the code list starts at the wrong word.</summary>
  [Fact]
  public void Arguments_give_the_corner_the_counts_and_every_code() {
    ProbeRequest? r = ProbeRequest.Parse(
      "-4 7 32 16 8 game:ore-*-limonite-* game:rock-*",
      out _
    );
    Assert.NotNull(r);
    Assert.False(r.CenterOnSpawn);
    Assert.Equal(
      (-4, 7, 32, 16, 8),
      (r.MinColumnX, r.MinColumnZ, r.ColumnsX, r.ColumnsZ, r.TileColumns)
    );
    Assert.Equal(["game:ore-*-limonite-*", "game:rock-*"], r.Codes);
  }

  /// <summary>Fails when spawn is not recognised as a corner word.</summary>
  [Fact]
  public void Spawn_stands_for_both_corner_words() {
    Assert.True(
      ProbeRequest.Parse("spawn spawn 4 4 2 game:rock-*", out _)!.CenterOnSpawn
    );
    Assert.Null(
      ProbeRequest.Parse("spawn 3 4 4 2 game:rock-*", out string error)
    );
    Assert.Contains("both", error);
  }

  /// <summary>Fails when a zero tile size or a missing code is accepted.</summary>
  [Theory]
  [InlineData("0 0 4 4 0 game:rock-*")]
  [InlineData("0 0 4 x 2 game:rock-*")]
  [InlineData("0 0 4 4 2")]
  public void Bad_arguments_are_refused(string text) =>
    Assert.Null(ProbeRequest.Parse(text, out string error));

  /// <summary>Fails when the last tile is not clipped or a column is covered twice.</summary>
  [Fact]
  public void Tiles_cover_the_rectangle_once_and_clip_the_last() {
    var r = ProbeRequest.Parse("0 0 10 5 4 game:rock-*", out _)!;
    IReadOnlyList<ProbeTile> tiles = r.Tiles(100, 200);
    Assert.Equal(6, tiles.Count);
    Assert.Equal(50, tiles.Sum(t => t.Columns));
    Assert.Equal(new ProbeTile(100, 200, 103, 203), tiles[0]);
    Assert.Equal(new ProbeTile(108, 204, 109, 204), tiles[^1]);
  }

  /// <summary>Fails when totals are not the sum of the tiles or the rate is not columns over seconds.</summary>
  [Fact]
  public void The_report_sums_tiles_and_gives_the_rate() {
    var tiles = new List<ProbeTileResult>
    {
      new(
        new ProbeTile(0, 0, 3, 3),
        2,
        new Dictionary<string, long> { ["a*"] = 5 }
      ),
      new(
        new ProbeTile(4, 0, 5, 3),
        1,
        new Dictionary<string, long> { ["a*"] = 7 }
      ),
    };
    JObject json = JObject.Parse(
      ProbeReport.ToJson(12345, [10, 120, -4], 4, ["a*", "b"], tiles)
    );
    Assert.Equal(12345, (int)json["seed"]!);
    Assert.Equal(24, (int)json["columns"]!);
    Assert.Equal(6.0, (double)json["columnsPerSecond"]!);
    Assert.Equal(12, (long)json["totals"]!["a*"]!);
    Assert.Equal(0, (long)json["totals"]!["b"]!);
    Assert.Equal(7, (long)json["tiles"]![1]!["counts"]!["a*"]!);
    Assert.Equal([10, 120, -4], json["spawn"]!.Select(t => (int)t).ToArray());
  }
}
