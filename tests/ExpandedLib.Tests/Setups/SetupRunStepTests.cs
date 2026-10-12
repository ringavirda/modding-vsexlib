#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The <c>run</c> step the harness builds (setups.md, <c>run</c>): the path from
/// <c>from</c> through each <c>via</c> to <c>to</c>, each cell the one block of the family with its
/// faces.</summary>
[Collection("Setups")]
public class SetupRunStepTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private const string Plated = "platedpipes:pipe-plated-";

  private const string Family =
    "[\"platedpipes:pipe-plated-straight-*\", \"platedpipes:pipe-plated-bend-*\", \"platedpipes:pipe-plated-tjunction-*\", \"platedpipes:pipe-plated-xjunction-*\"]";

  private static SetupScene Build(string layout, string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"platedpipes\"], "
          + (layout == "" ? "" : "\"layout\": " + layout + ", ")
          + "\"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [Path.Combine(RepoPaths.Root, "samples", "PlatedPipes")]
    );

  private const string EndsWe =
    "\"open\": [{\"at\": [0, 0, 0], \"face\": \"w\"}, {\"at\": [3, 0, 0], \"face\": \"e\"}]";

  private static string Step(string body, string family = Family) =>
    "[{\"run\": {\"code\": \""
    + Plated
    + "straight-we\", \"family\": "
    + family
    + ", "
    + body
    + "}}]";

  private static string[] Codes(SetupScene scene, string id) =>
    [.. scene.CellsOf(id).Select(c => scene.World.GetBlock(c).Code.ToString())];

  // Fails when the step lays a cell short, or a cell twice where two legs meet.
  [Fact]
  public void A_run_step_lays_every_cell_of_its_path_in_one_network()
  {
    using SetupScene scene = Build(
      "",
      Step(
        "\"id\": \"lane\", \"from\": [0, 0, 0], \"to\": [3, 0, 0], " + EndsWe
      )
    );

    Assert.Equal(
      [.. Enumerable.Repeat(Plated + "straight-we", 4)],
      Codes(scene, "lane")
    );
    Assert.Equal([0, 1, 2, 3], scene.CellsOf("lane").Select(c => c.X));
    BlockNetwork? net = scene.World.NetworkAt(scene.PositionOf("lane"));
    Assert.NotNull(net);
    Assert.All(
      scene.CellsOf("lane"),
      c => Assert.Same(net, scene.World.NetworkAt(c))
    );
  }

  // Fails when a via is not a corner of the path, or the corner cell is not the bend whose faces
  // are the two legs'.
  [Fact]
  public void A_via_turns_the_path_and_its_corner_is_the_bend_of_its_two_legs()
  {
    using SetupScene scene = Build(
      "",
      Step(
        "\"id\": \"lane\", \"from\": [0, 0, 0], \"via\": [[2, 0, 0]], \"to\": [2, 0, 2], \"open\": [{\"at\": [0, 0, 0], \"face\": \"w\"}, {\"at\": [2, 0, 2], \"face\": \"s\"}]"
      )
    );

    string[] codes = Codes(scene, "lane");

    Assert.Equal(5, codes.Length);
    Assert.Equal(Plated + "straight-we", codes[0]);
    Assert.Equal(Plated + "bend-ws", codes[2]);
    Assert.Equal(Plated + "straight-ns", codes[4]);
  }

  // Fails when an open face does not join the run to a cell of an earlier run, or that cell does
  // not gain the face back as the tee its three faces name.
  [Fact]
  public void An_open_face_toward_an_earlier_run_joins_it_and_that_cell_gains_the_face_back()
  {
    string layout =
      "{\"legend\": {\"P\": {\"run\": \"base\", \"code\": \""
      + Plated
      + "straight-we\", \"family\": "
      + Family
      + "}, \"o\": {\"open\": true}}, \"layers\": {\"0\": [\"oPPPo\"]}}";

    using SetupScene scene = Build(
      layout,
      Step(
        "\"id\": \"branch\", \"from\": [2, 0, -2], \"to\": [2, 0, -1], \"open\": [{\"at\": [2, 0, -2], \"face\": \"n\"}, {\"at\": [2, 0, -1], \"face\": \"s\"}]"
      )
    );

    Assert.Equal(
      Plated + "tjunction-wne",
      scene.World.GetBlock(new BlockPos(2, 0, 0)).Code.ToString()
    );
    Assert.Equal(
      Plated + "straight-ns",
      scene.World.GetBlock(new BlockPos(2, 0, -2)).Code.ToString()
    );
    Assert.Same(
      scene.World.NetworkAt(new BlockPos(1, 0, 0)),
      scene.World.NetworkAt(new BlockPos(2, 0, -2))
    );
  }

  // Fails when a leg that bends off its axis is laid as a staircase instead of refused.
  [Fact]
  public void A_leg_off_one_axis_is_refused_naming_its_ends()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      Build("", Step("\"from\": [0, 0, 0], \"to\": [1, 0, 1]"))
    );

    Assert.Contains("steps[0]", e.Message);
    Assert.Contains(
      "the leg from (0, 0, 0) to (1, 0, 1) does not run along one axis",
      e.Message
    );
  }

  // Fails when an open face on a position the path does not cross is let through.
  [Fact]
  public void An_open_face_off_the_path_is_refused()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      Build(
        "",
        Step(
          "\"from\": [0, 0, 0], \"to\": [2, 0, 0], \"open\": [{\"at\": [0, 5, 0], \"face\": \"u\"}]"
        )
      )
    );

    Assert.Contains("open at (0, 5, 0) is no cell of the run", e.Message);
  }

  // Fails when an open face letter outside n, e, s, w, u, d is read as some face.
  [Fact]
  public void An_open_face_that_is_not_a_face_letter_is_refused()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      Build(
        "",
        Step(
          "\"from\": [0, 0, 0], \"to\": [2, 0, 0], \"open\": [{\"at\": [0, 0, 0], \"face\": \"x\"}]"
        )
      )
    );

    Assert.Contains(
      "open face \"x\" is not one of n, e, s, w, u, d",
      e.Message
    );
  }

  // Fails when a code that is not a straight pipe takes a family from nowhere.
  [Fact]
  public void A_code_that_is_not_a_straight_pipe_needs_a_family_naming_the_step()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      Build(
        "",
        "[{\"run\": {\"code\": \""
          + Plated
          + "wire\", \"from\": [0, 0, 0], \"to\": [1, 0, 0]}}]"
      )
    );

    Assert.Contains("steps[0]", e.Message);
    Assert.Contains("so the step needs \"family\"", e.Message);
  }

  // Fails when a path whose cell no family block fits is laid with some other block.
  [Fact]
  public void A_cell_no_family_block_fits_fails_naming_its_faces()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      Build(
        "",
        Step(
          "\"from\": [0, 0, 0], \"via\": [[1, 0, 0]], \"to\": [1, 0, 1], \"open\": [{\"at\": [0, 0, 0], \"face\": \"w\"}]",
          "\"" + Plated + "straight-*\""
        )
      )
    );

    Assert.Contains(
      "cell (1, 0, 0) needs a block with the faces sw",
      e.Message
    );
  }
}
#endif
