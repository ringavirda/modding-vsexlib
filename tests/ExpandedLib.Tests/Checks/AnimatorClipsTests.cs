using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="AnimatorClips"/> over planted shapes, one clip end mode each.</summary>
public class AnimatorClipsTests(ITestOutputHelper output) {
  // One shape holding one clip; end is its onAnimationEnd property, or empty for none.
  private AnimatorClips.Result Planted(string end) {
    using var files = new PlantedFiles();
    files.Write(
      "shapes/planted.json",
      "{ \"elements\": [], \"animations\": [ { \"code\": \"cycle\", "
        + end
        + "\"onActivityStopped\": \"EaseOut\", \"keyframes\": [] } ] }"
    );
    AnimatorClips.Result result = AnimatorClips.Check(files.Root);
    foreach (string line in result.Findings)
      output.WriteLine(line);
    return result;
  }

  [Fact]
  [PlantedDefect(typeof(AnimatorClips), nameof(AnimatorClips.Check))]
  public void A_clip_ending_in_EaseOut_is_reported() {
    AnimatorClips.Result result = Planted("\"onAnimationEnd\": \"EaseOut\", ");

    Assert.Equal(1, result.Shapes);
    Assert.Equal(1, result.Clips);
    Assert.EndsWith(
      "shapes/planted.json: clip 'cycle' ends EaseOut",
      Assert.Single(result.Findings)
    );
  }

  [Fact]
  [PlantedDefect(typeof(AnimatorClips), nameof(AnimatorClips.Check))]
  public void A_clip_ending_in_Stop_is_reported() =>
    Assert.EndsWith(
      "clip 'cycle' ends Stop",
      Assert.Single(Planted("\"onAnimationEnd\": \"Stop\", ").Findings)
    );

  [Theory]
  [InlineData("\"onAnimationEnd\": \"Repeat\", ")]
  [InlineData("\"onAnimationEnd\": \"hold\", ")]
  [InlineData("")]
  public void Repeat_Hold_and_the_default_pass_whatever_onActivityStopped_says(
    string end
  ) => Assert.Empty(Planted(end).Findings);

  [Fact]
  public void Key_is_the_shape_and_clip() =>
    Assert.Equal(
      "iiex/shapes/door.json: clip 'open'",
      AnimatorClips.Key("iiex/shapes/door.json: clip 'open' ends EaseOut")
    );
}
