using System;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Pins orientation-checked multiblock parts: which legends carry a facing, and rotating that
/// facing. A layout with no oriented part emits no facings attribute.
/// </summary>
public class MultiblockFacingsTests {
  private static JObject Def(Action<MultiblockLayoutBuilder> configure) =>
    (JObject)
      ExBlockDef.Create("d", "c").MultiblockLayout(configure).ToJson()[
        "attributes"
      ]!;

  private static JToken? Facings(Action<MultiblockLayoutBuilder> configure) =>
    Def(configure)["multiblockFacings"];

  #region Which legends are oriented

  [Theory]
  // The two forms block codes use: trailing orientation, and orientation mid-code.
  [InlineData("game:cokeovendoor-closed-north", 2)]
  [InlineData("game:brickslabs-fire-south-free", 2)]
  [InlineData("game:brickslabs-fire-east-free", 2)]
  [InlineData("mod:thing-w", 1)]
  public void A_whole_side_segment_is_recognised(string code, int segment) =>
    Assert.Equal(
      [segment],
      MultiblockLayoutBuilder.FindOrientationSegments(code)
    );

  [Theory]
  // Vertical facings do not move under a Y rotation.
  [InlineData("game:brickslabs-fire-up-free")]
  [InlineData("game:brickslabs-fire-down-free")]
  [InlineData("iiex:pipe-outlet-fire-u")]
  // No facing at all, the common case.
  [InlineData("exlib:structurefiller")]
  [InlineData("game:refractorybricks-good-tier*")]
  [InlineData("iiex:furnace-puddlingcore-*")]
  // A side word must be a whole segment; these are not facings.
  [InlineData("mod:westward-thing")]
  [InlineData("mod:pipe-northgate")]
  public void Non_facing_codes_are_not_oriented(string code) =>
    Assert.Empty(MultiblockLayoutBuilder.FindOrientationSegments(code));

  [Fact]
  public void A_layout_with_no_oriented_part_emits_no_facings_attribute() {
    // Absent here to avoid churning goldens for unoriented structures.
    Assert.Null(
      Facings(s =>
        s.Legend('#', "game:refractorybricks-good-tier*")
          .Legend('u', "game:brickslabs-fire-up-free")
          .Layer(0, "# u")
      )
    );
  }

  [Fact]
  public void LegendAnyFacing_opts_a_code_out_of_rotation() {
    Assert.Null(
      Facings(s =>
        s.LegendAnyFacing('d', "game:cokeovendoor-closed-north").Layer(0, "d")
      )
    );
  }

  [Fact]
  public void An_oriented_legend_records_its_facing_segment() {
    JToken? f = Facings(s =>
      s.Legend('#', "game:claybricks-good-fire")
        .Legend('i', "game:brickslabs-fire-south-free")
        .Layer(0, "# i")
    );
    Assert.NotNull(f);
    // Keyed by code, not block number. The value is an array of segment indices.
    Assert.Equal([2], f!["game:brickslabs-fire-south-free"]!.Values<int>());
    Assert.Null(f["game:claybricks-good-fire"]);
  }

  #endregion

  #region Rotation

  [Theory]
  // Authored north is the side whose AngleFromSide equals the structure angle.
  [InlineData("north", 0, "north")]
  [InlineData("north", 90, "west")]
  [InlineData("north", 180, "south")]
  [InlineData("north", 270, "east")]
  // A part authored some other way round carries its offset from that.
  [InlineData("south", 90, "east")]
  [InlineData("south", 270, "west")]
  [InlineData("east", 90, "north")]
  [InlineData("west", 180, "east")]
  public void Side_words_rotate_with_the_structure(
    string side,
    int angle,
    string want
  ) => Assert.Equal(want, ExOrientation.RotateSideWord(side, angle));

  [Fact]
  public void Letter_form_survives_rotation_as_a_letter() {
    // `orientation` variants use letters; `side` variants use words.
    Assert.Equal("w", ExOrientation.RotateSideWord("n", 90));
    Assert.Equal("west", ExOrientation.RotateSideWord("north", 90));
  }

  [Theory]
  [InlineData("up")]
  [InlineData("down")]
  [InlineData("fire")]
  public void Non_horizontal_words_are_returned_unchanged(string word) =>
    Assert.Equal(word, ExOrientation.RotateSideWord(word, 90));

  [Fact]
  public void Rotate_swaps_only_the_facing_segment() {
    MultiblockFacings f = FacingsFor(s =>
      s.Legend('i', "game:brickslabs-fire-south-free").Layer(0, "i")
    );

    var code = new AssetLocation("game", "brickslabs-fire-south-free");
    Assert.Equal(
      "game:brickslabs-fire-east-free",
      f.Rotate(code, 90).ToString()
    );
    Assert.Equal(
      "game:brickslabs-fire-south-free",
      f.Rotate(code, 0).ToString()
    );
    Assert.Equal(
      "game:brickslabs-fire-north-free",
      f.Rotate(code, 180).ToString()
    );
  }

  [Fact]
  public void Rotate_leaves_codes_the_layout_did_not_mark_alone() {
    MultiblockFacings f = FacingsFor(s =>
      s.Legend('i', "game:brickslabs-fire-south-free").Layer(0, "i")
    );

    // A brick has no facing; an up-facing slab has one that cannot turn.
    var brick = new AssetLocation("game", "claybricks-good-fire");
    var upSlab = new AssetLocation("game", "brickslabs-fire-up-free");
    Assert.Equal(brick.ToString(), f.Rotate(brick, 90).ToString());
    Assert.Equal(upSlab.ToString(), f.Rotate(upSlab, 90).ToString());
  }

  [Fact]
  public void An_empty_facing_table_is_the_identity() {
    var code = new AssetLocation("game", "brickslabs-fire-south-free");
    Assert.True(MultiblockFacings.None.IsEmpty);
    Assert.Equal(
      code.ToString(),
      MultiblockFacings.None.Rotate(code, 90).ToString()
    );
  }

  [Fact]
  public void A_full_turn_returns_the_authored_code() {
    MultiblockFacings f = FacingsFor(s =>
      s.Legend('d', "game:cokeovendoor-closed-north").Layer(0, "d")
    );
    var code = new AssetLocation("game", "cokeovendoor-closed-north");
    Assert.Equal(code.ToString(), f.Rotate(code, 360).ToString());
  }

  [Fact]
  public void A_segment_index_past_the_end_falls_back_to_the_authored_code() {
    // Only reachable through a hand-edited attribute.
    Assert.Null(
      MultiblockFacings.RotateSegments("brickslabs-fire-south-free", [9], 90)
    );
    Assert.Null(
      MultiblockFacings.RotateSegments("brickslabs-fire-south-free", [1], 90)
    );
    // One bad index discards the whole rotation.
    Assert.Null(
      MultiblockFacings.RotateSegments("brickslabs-fire-south-free", [2, 9], 90)
    );
  }

  #endregion

  private static MultiblockFacings FacingsFor(
    Action<MultiblockLayoutBuilder> configure
  ) {
    var attrs = new Vintagestory.API.Datastructures.JsonObject(Def(configure));
    return MultiblockFacings.FromAttributes(attrs);
  }

  #region A pinned network node is unrepresentable

  [Theory]
  [InlineData("iiex:pipe-plated-straight-ns")]
  [InlineData("iiex:mpenergy-shaft-we")]
  [InlineData("iiex:pipe-plated-bend-nw")]
  [InlineData("iiex:pipe-plated-tjunction-uns")]
  [InlineData("iiex:molten-canal-brick-xjunction-nswe")]
  public void A_legend_pinning_a_network_token_is_refused(string code) {
    // A multi-letter direction token names only a network node's self-picked orientation.
    var thrown = Assert.Throws<InvalidOperationException>(() =>
      Def(l => l.Legend('p', code).Layer(0, "p"))
    );

    Assert.Contains(code, thrown.Message);
    Assert.Contains("'p'", thrown.Message);
    Assert.Contains("Connector", thrown.Message);
  }

  [Fact]
  public void LegendAnyFacing_is_the_documented_way_out() {
    // The opt-out stays lax: a code meant literally, at every angle, is still expressible.
    JToken? facings = Facings(l =>
      l.LegendAnyFacing('p', "iiex:pipe-plated-straight-ns").Layer(0, "p")
    );

    Assert.Null(facings);
  }

  [Fact]
  public void A_side_word_is_not_a_network_token() {
    // The refusal matches only the declared schemes' tokens, not any run of direction letters.
    JToken? facings = Facings(l =>
      l.Legend('s', "game:brickslabs-fire-south-free").Layer(0, "s")
    );

    Assert.NotNull(facings);
  }

  #endregion
}
