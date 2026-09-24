using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>Every clip in exlib's own shipped shapes ends in <c>Repeat</c> or <c>Hold</c>; the rule
/// is <see cref="AnimatorClips"/>'.</summary>
[GuardOf(typeof(AnimatorClips), nameof(AnimatorClips.Check))]
public class AnimatorClipGuards(ITestOutputHelper output) {
  [Fact]
  public void Every_shipped_clip_repeats_or_holds() {
    Premise.NotEmpty(
      LoopingAnimations.ShapeFiles(RepoPaths.Assets("exlib")),
      "shipped shapes"
    );
    AnimatorClips.Result result = AnimatorClips.Check(
      RepoPaths.Assets("exlib")
    );
    output.WriteLine($"{result.Shapes} shapes, {result.Clips} clips");

    FindingLists.Assert(
      result.Findings,
      new Dictionary<string, string>(),
      new Dictionary<string, string>(),
      AnimatorClips.Key
    );
  }
}
