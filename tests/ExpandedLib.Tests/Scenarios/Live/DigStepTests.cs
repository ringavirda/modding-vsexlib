using ExpandedLib.Testing.Live;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary><c>dig</c> and the stated ground on the live runner, over a
/// <see cref="ExpandedLib.Testing.TestWorld"/> with the plot at (100, 50, 100) standing on stone.</summary>
public class DigStepTests {
  private const string Ground = """{ "ground": 2, "plot": [2, 4, 2] }""";

  private static LiveFixture OnStone(string steps, string extra = Ground) {
    var fixture = new LiveFixture();
    for (int x = -3; x <= 3; x++)
      for (int z = -3; z <= 3; z++)
        for (int y = -3; y <= -1; y++)
          fixture.Test.Accessor.SetBlock(
            fixture.Stone.BlockId,
            LiveFixture.Origin.AddCopy(x, y, z)
          );
    fixture.Context.Model = LiveFixture.Case(steps, extra).Model;
    return fixture;
  }

  private static int IdAt(LiveFixture fixture, int x, int y, int z) =>
    fixture.Test.GetBlock(LiveFixture.Origin.AddCopy(x, y, z)).Id;

  /// <summary>Fails when the live dig leaves a cell of its box or its <c>at</c> standing, or digs
  /// past them.</summary>
  [Fact]
  public void A_dug_box_is_air_and_the_ground_around_it_stands() {
    const string steps = """
      [{ "dig": { "from": [-1, -2, -1], "to": [0, -1, 0] } },
       { "dig": { "at": [2, -1, 2] } }]
      """;
    LiveFixture fixture = OnStone(steps);

    fixture.Run(fixture.Context.Model!.Steps);

    for (int x = -1; x <= 0; x++)
      for (int z = -1; z <= 0; z++)
        for (int y = -2; y <= -1; y++)
          Assert.Equal(0, IdAt(fixture, x, y, z));
    Assert.Equal(0, IdAt(fixture, 2, -1, 2));
    Assert.Equal(fixture.Stone.Id, IdAt(fixture, 1, -1, 0));
    Assert.Equal(fixture.Stone.Id, IdAt(fixture, 0, -3, 0));
    Assert.Contains(
      "[scenario]   1 dig [-1,-2,-1] [0,-1,0] ok: 8 cell(s) dug",
      fixture.Lines
    );
  }

  /// <summary>Fails when the live dig sets a cell before its box is checked: the cell under the
  /// refused one stays stone.</summary>
  [Fact]
  public void A_refused_dig_digs_nothing_and_blames_the_scenario() {
    LiveFixture fixture = OnStone(
      """[{ "dig": { "from": [0, -1, 0], "to": [0, 0, 0] } }]"""
    );

    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(fixture.Context.Model!.Steps)
    );

    Assert.Equal(Suspect.Scenario, failure.Suspect);
    Assert.Contains("(0, 0, 0) lies at or above y 0", failure.Message);
    Assert.Equal(fixture.Stone.Id, IdAt(fixture, 0, -1, 0));
  }

  /// <summary>Fails when a live place in the stated ground goes over the terrain instead of
  /// failing as the harness does.</summary>
  [Fact]
  public void A_place_into_ground_that_was_not_dug_fails() {
    LiveFixture fixture = OnStone(
      """[{ "place": { "code": "game:stone", "at": [0, -1, 0] } }]"""
    );

    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(fixture.Context.Model!.Steps)
    );

    Assert.Contains("[0, -1, 0] holds game:stone, not air", failure.Message);
  }

  /// <summary>Fails when a file with no ground stops placing over the terrain under y 0.</summary>
  [Fact]
  public void A_place_under_y_zero_with_no_ground_goes_over_the_terrain() {
    LiveFixture fixture = OnStone(
      """[{ "place": { "code": "game:stone", "at": [0, -1, 0] } }]""",
      "{}"
    );

    fixture.Run(fixture.Context.Model!.Steps);

    Assert.Contains(
      fixture.Lines,
      l => l.StartsWith("[scenario]   1 place") && l.Contains(" ok: ")
    );
  }
}
