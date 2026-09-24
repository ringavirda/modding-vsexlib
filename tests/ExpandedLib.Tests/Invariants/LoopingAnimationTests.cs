using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The looping-animation rule (<see cref="LoopingAnimations"/>) over exlib's own shipped shapes.
/// </summary>
[GuardOf(typeof(LoopingAnimations), nameof(LoopingAnimations.Check))]
public class LoopingAnimationTests {
  [Fact]
  public void Exlibs_own_shapes_carry_no_looping_defect() {
    var offenders = LoopingAnimations.Check(RepoPaths.Assets("exlib"));
    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }

  [Fact]
  public void The_corpus_reaches_the_shipped_shapes() {
    // A path filter that silently matches nothing would make the rule above pass trivially.
    Premise.NotEmpty(
      LoopingAnimations.ShapeFiles(RepoPaths.Assets("exlib")),
      "shipped shapes"
    );
  }

  // One Repeat clip keyed at frames 0 and 29; each argument is that keyframe's elements object.
  private static IReadOnlyList<string> PlantedClip(string first, string last) {
    using var files = new PlantedFiles();
    files.Write(
      "shapes/planted.json",
      "{ \"elements\": [], \"animations\": [ { \"code\": \"spin\", "
        + "\"onAnimationEnd\": \"Repeat\", \"keyframes\": [ "
        + $"{{ \"frame\": 0, \"elements\": {first} }}, "
        + $"{{ \"frame\": 29, \"elements\": {last} }} ] }} ] }}"
    );
    return LoopingAnimations.Check(files.Root);
  }

  [Fact]
  [PlantedDefect(typeof(LoopingAnimations), nameof(LoopingAnimations.Check))]
  public void A_clip_unwinding_across_the_wrap_without_the_shortest_distance_flag_is_reported() {
    string offender = Assert.Single(
      PlantedClip(
        """{ "rotor": { "rotationY": 0 } }""",
        """{ "rotor": { "rotationY": 350 } }"""
      )
    );

    Assert.EndsWith(
      "shapes/planted.json: clip 'spin' element 'rotor' rotationY wraps 350 -> 0 "
        + "(350 deg) with no rotShortestDistanceY",
      offender
    );
  }

  [Fact]
  [PlantedDefect(typeof(LoopingAnimations), nameof(LoopingAnimations.Check))]
  public void An_element_posed_at_only_one_end_of_a_clip_is_reported() {
    string offender = Assert.Single(
      PlantedClip(
        """{ "rotor": { "rotationY": 0 }, "arm": { "rotationX": 10 } }""",
        """{ "rotor": { "rotationY": 90 } }"""
      )
    );

    Assert.EndsWith(
      "shapes/planted.json: clip 'spin' element 'arm' keyframed at 0 but not 29",
      offender
    );
  }

  [Fact]
  public void A_flagged_unwind_with_every_element_posed_at_both_ends_passes() {
    Assert.Empty(
      PlantedClip(
        """{ "rotor": { "rotationY": 0 } }""",
        """{ "rotor": { "rotationY": 350, "rotShortestDistanceY": true } }"""
      )
    );
  }
}
