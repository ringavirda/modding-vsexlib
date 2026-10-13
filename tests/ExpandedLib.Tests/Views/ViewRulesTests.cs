using System;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
#if !GAME_GE_1_21
using FastVec3f = Vintagestory.API.MathTools.Vec3f;
#endif

namespace ExpandedLib.Tests;

/// <summary>The rules G4, G9, G11 and G12, on the whole vanilla game with the views fixture mod.</summary>
[Collection("ItemViews")]
public class ViewRulesTests {
  private static string Fixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Helpers",
      "Views",
      "Fixtures",
      "viewsfixture"
    );

  private static TestWorld World => GameWorlds.Get([Fixture]);

  private static ViewGroup GroupOf(string code) =>
    ViewGates.Groups(World, "game").Single(g => g.Codes.Contains(code));

  private static string[] Failing(ViewGroup group) =>
    [
      .. ViewGates
        .Rules(World, group)
        .Where(r => !r.Pass)
        .Select(r => r.Gate + (r.Target == "fp" ? "fp" : "")),
    ];

  // Fails when G4's comparison is inverted: the flat plate then fails it and the plate on edge passes.
  [Fact]
  public void A_plate_on_edge_fails_G4() {
    ViewGroup plate = GroupOf("game:metalplate-iron");
    Assert.Empty(Failing(plate));

    ModelTransform edge = plate.Ground.Clone();
    edge.Rotation = new FastVec3f(90, edge.Rotation.Y, edge.Rotation.Z);
    ViewGroup standing = plate with { Ground = edge };

    Assert.Equal(["G4"], Failing(standing));
    Assert.Equal(
      standing.Box.Extents()[1] - standing.Box.Extents()[2],
      ViewGates.Rules(World, standing).Single(r => r.Gate == "G4").Value,
      0.01
    );
  }

  // Fails when a block's tilt is compared in the wrong direction or is not read: a hay block turned 30 degrees on the
  // ground then passes.
  [Fact]
  public void A_block_off_its_upright_fails_G4() {
    ViewGroup hay = GroupOf("game:hay-normal-ud");
    Assert.Empty(Failing(hay));

    ModelTransform leaning = hay.Ground.Clone();
    leaning.Rotation = new FastVec3f(
      30,
      leaning.Rotation.Y,
      leaning.Rotation.Z
    );

    Assert.Equal(["G4"], Failing(hay with { Ground = leaning }));
    Assert.Equal(
      30,
      ViewGates
        .Rules(World, hay with { Ground = leaning })
        .Single(r => r.Gate == "G4")
        .Value,
      0.01
    );
  }

  // Fails when a thin block is judged by its tilt: a hay block drawn as a 2-voxel slab with its thin side horizontal then
  // passes.
  [Fact]
  public void A_thin_block_is_judged_by_its_flat_extent() {
    ViewGroup hay = GroupOf("game:hay-normal-ud");
    ViewGroup slab = hay with {
      Box = hay.Box with {
        From = new Vec3d(0, 0, 0),
        To = new Vec3d(16, 16, 2),
      },
    };
    Assert.Equal(["G4"], Failing(slab));

    ModelTransform flat = hay.Ground.Clone();
    flat.Rotation = new FastVec3f(90, flat.Rotation.Y, flat.Rotation.Z);
    Assert.Empty(Failing(slab with { Ground = flat }));
  }

  // Fails when G9's comparison is inverted: the held plate then fails it and the plate below the floor passes.
  [Fact]
  public void An_item_held_below_the_floor_fails_G9() {
    ViewGroup plate = GroupOf("game:metalplate-iron");
    Assert.True(ViewGates.HeldFloor(World, plate) >= 0);

    ModelTransform low = plate.Hand.Clone();
    low.Translation = new FastVec3f(
      low.Translation.X,
      low.Translation.Y - 12,
      low.Translation.Z
    );
    ViewGroup sunk = plate with { Hand = low };

    Assert.Equal(["G9", "G9fp"], Failing(sunk));
    Assert.True(ViewGates.HeldFloor(World, sunk) < -1);
  }

  // Fails when the clip match is made case-sensitive: SpearIdle then fails. A long tool on helditemready fails G11
  // alone, and a block held on a clip other than the two it takes fails it.
  [Theory]
  [InlineData("helditemready", true)]
  [InlineData("spearidlee", true)]
  [InlineData("crudeOarIdle", false)]
  [InlineData("SpearIdle", false)]
  public void Clip_rules(string held, bool fails) {
    ViewGroup spear = GroupOf("game:spear-generic-iron");
    Assert.Equal(ItemViewKind.Longtool, spear.Kind);
    Assert.Empty(Failing(spear));

    string[] failing = Failing(spear with { Held = held });

    Assert.Equal(fails ? ["G11"] : [], failing);
  }

  // Fails when a bulk item or a block is allowed any resolving clip: a bulk plate or a hay block held on spearidle then
  // passes.
  [Fact]
  public void A_bulk_item_and_a_block_take_their_own_clips() {
    ViewGroup plate = GroupOf("game:metalplate-iron") with {
      Kind = ItemViewKind.Bulk,
    };
    Assert.Empty(Failing(plate with { Held = "holdbothhandslarge" }));
    Assert.Equal(["G11"], Failing(plate with { Held = "spearidle" }));

    ViewGroup hay = GroupOf("game:hay-normal-ud");
    CollectibleObject block = hay.Representative;
    string? authored = block.HeldRightTpIdleAnimation;
    try {
      block.HeldRightTpIdleAnimation = "holdunderarm";
      Assert.Empty(Failing(hay));
      block.HeldRightTpIdleAnimation = "spearidle";
      Assert.Equal(["G11"], Failing(hay));
    } finally {
      block.HeldRightTpIdleAnimation = authored;
    }
  }

  private static ViewGroup Sheet =>
    ViewGates
      .Groups(World, "viewsfixture")
      .Single(g => g.Codes.Contains("viewsfixture:uprightsheet"));

  private static ViewCatalogue Catalogue(string name) =>
    ViewCatalogue.Load(
      Path.Combine(
        RepoPaths.Root,
        "tests",
        "ExpandedLib.Tests",
        "Views",
        "Fixtures",
        "catalogue",
        name
      ),
      "viewsfixture"
    )!;

  // Fails when G12's comparison is inverted: the sheet drawn 9 x 1 x 9 then fails its own row and passes a row of
  // 1 x 1 x 10. The fixture's uprightsheet stands in for rivetrod.
  [Fact]
  public void A_child_segment_on_rivetrod_fails() {
    ViewGroup sheet = Sheet;

    Assert.Equal(0, Catalogue("sized").Reading(sheet)!.Value, 0.001);
    GateReading wrong = Catalogue("wrong").Reading(sheet)!;

    Assert.False(wrong.Pass);
    Assert.Equal(8, wrong.Value, 0.001);
    Assert.Contains("stock.md:3", wrong.Detail);
    Assert.Null(Catalogue("sized").Reading(GroupOf("game:metalplate-iron")));
  }

  // Fails when only the first page's row is read: the sheet matches a.md and then passes.
  [Fact]
  public void Two_pages_that_disagree_fail() {
    ViewCatalogue pages = Catalogue("disagree");
    GateReading reading = pages.Reading(Sheet)!;

    Assert.False(reading.Pass);
    Assert.Contains("b.md:3", reading.Detail);
    Assert.Equal(["viewsfixture:uprightsheet"], pages.Disagreements());
    Assert.Empty(Catalogue("sized").Disagreements());
  }

  // Fails when the root check is removed: the missing folder then loads as an empty catalogue.
  [Fact]
  public void A_missing_configured_root_throws() {
    string missing = Path.Combine(RepoPaths.Root, "no-such-docs");

    Assert.Throws<DirectoryNotFoundException>(() =>
      ViewCatalogue.Load(missing, "viewsfixture")
    );
    Assert.Null(ViewCatalogue.Load(null, "viewsfixture"));
  }

  // Fails when pig.md's header is not excluded: plainbar then enters the catalogue. A game: row, a Proposed row, a
  // composite row and a row with no size are skipped, and an unregistered code is named.
  [Fact]
  public void Rows_skip_in_order() {
    ViewCatalogue sized = Catalogue("sized");

    Assert.Equal(
      ["viewsfixture:uprightsheet", "viewsfixture:phantom"],
      sized.Rows.Select(r => r.Code)
    );
    Assert.Equal(
      ["viewsfixture:phantom"],
      sized.Unregistered(World).Select(r => r.Code)
    );
  }
}
