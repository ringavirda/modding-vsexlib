using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The declared-scheme rotation rule: rotate each direction letter preserving order; if
/// the result is not a declared token, use the unique declared token with the same face
/// set.</summary>
public class ExOrientationsTests {
  #region The worked examples from the design doc

  // Transcribed from docs/design/mechanics/orientation-schemes.md's worked table (90 deg, north to west).
  [Theory]
  [InlineData("Face", "n", "w")]
  [InlineData("Axis", "we", "ns")] // ordered gives `sn`, undeclared -> set {s,n} -> `ns`
  [InlineData("DirectedAxis", "we", "sn")] // ordered gives `sn`, declared -> direction survives
  [InlineData("PipeBend", "nw", "ws")] // ordered gives `ws`, declared
  [InlineData("PipeTee", "uwe", "uns")] // ordered gives `usn`, undeclared -> set -> `uns`
  [InlineData("PipeCross", "weud", "nsud")] // ordered gives `snud`, undeclared -> set -> `nsud`
  public void The_documented_rotation_of_every_scheme_at_ninety_degrees(
    string scheme,
    string token,
    string expected
  ) => Assert.Equal(expected, Scheme(scheme).Rotate(token, 90));

  [Fact]
  public void The_ordered_step_is_what_keeps_a_directed_axis_directed() {
    // DirectedAxis declares `sn`; Axis does not preserve direction.
    Assert.Equal("ns", ExOrientations.Axis.Rotate("we", 90));
    Assert.Equal("sn", ExOrientations.DirectedAxis.Rotate("we", 90));
  }

  [Fact]
  public void The_set_step_is_what_repairs_a_non_canonical_spelling() {
    // PipeTee's tokens are spelled non-canonically: `uwe` rotates to `usn`, not the declared `uns`.
    Assert.DoesNotContain("usn", ExOrientations.PipeTee.Tokens);
    Assert.Contains("uns", ExOrientations.PipeTee.Tokens);
    Assert.Equal("uns", ExOrientations.PipeTee.Rotate("uwe", 90));
  }

  #endregion

  #region Properties that must hold for every scheme

  [Fact]
  public void Four_quarter_turns_return_every_token_to_itself() {
    // Four quarter turns must be the identity for every token of every scheme.
    foreach (ExOrientationScheme scheme in ExOrientations.All)
      foreach (string token in scheme.Tokens) {
        string round = token;
        for (int i = 0; i < 4; i++)
          round = scheme.Rotate(round, 90);
        Assert.Equal($"{scheme.Name}: {token}", $"{scheme.Name}: {round}");
      }
  }

  [Fact]
  public void Every_rotation_of_every_token_is_a_token_the_block_declares() {
    // A rotation must never produce a code the block does not declare.
    foreach (ExOrientationScheme scheme in ExOrientations.All)
      foreach (string token in scheme.Tokens)
        foreach (int angle in new[] { 0, 90, 180, 270 })
          Assert.True(
            scheme.Contains(scheme.Rotate(token, angle)),
            $"{scheme.Name}.Rotate({token}, {angle}) = "
              + $"'{scheme.Rotate(token, angle)}', which {scheme.Name} does not declare"
          );
  }

  [Fact]
  public void A_rotation_never_changes_how_many_faces_a_token_names() {
    // A rotation never changes how many faces a token names.
    foreach (ExOrientationScheme scheme in ExOrientations.All)
      foreach (string token in scheme.Tokens)
        Assert.Equal(token.Length, scheme.Rotate(token, 90).Length);
  }

  [Fact]
  public void The_set_fallback_is_unambiguous_wherever_it_is_reachable() {
    // At most one declared token per face set; the directed schemes are exempt.
    foreach (ExOrientationScheme scheme in ExOrientations.All) {
      if (scheme.Name.StartsWith("Directed"))
        continue;

      List<string> dupes =
      [
        .. scheme
          .Tokens.GroupBy(t => string.Concat(t.Distinct().OrderBy(c => c)))
          .Where(g => g.Count() > 1)
          .Select(g => string.Join("/", g)),
      ];
      Assert.Equal(
        $"{scheme.Name}: ",
        $"{scheme.Name}: {string.Join(", ", dupes)}"
      );
    }
  }

  [Fact]
  public void A_directed_scheme_reaches_its_ordered_step_for_every_token() {
    // A directed scheme's ordered step must always match a declared token.
    foreach (
      var scheme in new[]
      {
        ExOrientations.DirectedAxis,
        ExOrientations.DirectedAxisFlat,
      }
    )
      foreach (string token in scheme.Tokens)
        foreach (int angle in new[] { 90, 180, 270 }) {
          string ordered = string.Concat(
            token.Select(c =>
              c is 'u' or 'd'
                ? c
                : ExOrientation.SideFromAngle(
                  ExOrientation.AngleFromSide(c.ToString()) + angle,
                  asLetter: true
                )[0]
            )
          );
          Assert.True(
            scheme.Contains(ordered),
            $"{scheme.Name}: {token}@{angle} -> {ordered} is not declared, "
              + "so the ambiguous set fallback WOULD be reached"
          );
        }
  }

  #endregion

  #region Not-ours tokens, and the vertical no-ops

  [Fact]
  public void A_token_the_scheme_does_not_declare_comes_back_untouched() {
    // A material or type segment, not a direction letter, must be rotated to itself.
    foreach (
      string notAToken in new[]
      {
        "sun",
        "used",
        "wend",
        "tier1",
        "refractory",
        "",
      }
    )
      Assert.Equal(notAToken, ExOrientations.Axis.Rotate(notAToken, 90));
  }

  [Fact]
  public void A_vertical_token_is_its_own_image_under_every_y_rotation() {
    // Vertical faces never rotate under a Y turn.
    Assert.Equal("ud", ExOrientations.Axis.Rotate("ud", 90));
    Assert.False(ExOrientations.Axis.RotatesUnderY("ud"));
    Assert.True(ExOrientations.Axis.RotatesUnderY("ns"));

    Assert.Equal("u", ExOrientations.FaceAll.Rotate("u", 270));
    Assert.False(ExOrientations.FaceAll.RotatesUnderY("u"));
    Assert.True(ExOrientations.FaceAll.RotatesUnderY("n"));
  }

  #endregion

  #region Resolving a scheme from a block's declared states

  [Fact]
  public void A_state_list_resolves_to_the_scheme_that_declares_exactly_it() {
    Assert.Same(
      ExOrientations.Face,
      ExOrientations.Resolve(["n", "e", "s", "w"])
    );
    Assert.Same(
      ExOrientations.Axis,
      ExOrientations.Resolve(["ns", "we", "ud"])
    );
    Assert.Same(ExOrientations.AxisFlat, ExOrientations.Resolve(["ns", "we"]));

    // A block may declare its states in any order.
    Assert.Same(
      ExOrientations.Face,
      ExOrientations.Resolve(["w", "n", "s", "e"])
    );
  }

  [Fact]
  public void A_near_miss_resolves_to_nothing_rather_than_to_the_closest_scheme() {
    // Resolve requires exact set equality; a subset never matches.
    Assert.Null(ExOrientations.Resolve(["n", "e", "s"]));
    Assert.Null(ExOrientations.Resolve(["ns", "we", "ud", "extra"]));
    Assert.Null(ExOrientations.Resolve([]));
    Assert.Null(ExOrientations.Resolve(null));
  }

  [Fact]
  public void The_two_axis_schemes_are_told_apart_by_their_declared_states_alone() {
    // `ns` is legal in both grammars; only the declared state list distinguishes them.
    Assert.Same(
      ExOrientations.Axis,
      ExOrientations.Resolve(["ns", "we", "ud"])
    );
    Assert.Same(
      ExOrientations.DirectedAxis,
      ExOrientations.Resolve(["ns", "we", "ud", "sn", "ew", "du"])
    );
    Assert.Contains("ns", ExOrientations.Axis.Tokens);
    Assert.Contains("ns", ExOrientations.DirectedAxis.Tokens);
  }

  #endregion

  private static ExOrientationScheme Scheme(string name) =>
    ExOrientations.All.Single(s => s.Name == name);
}
