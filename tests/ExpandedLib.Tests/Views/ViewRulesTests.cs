using System;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

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

  // Fails when G9's comparison is inverted: the held plate then fails it and the plate below the floor passes. The hay
  // block sunk below the floor fails G9 and G9fp, so a block's first-person floor is judged too.
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
    Assert.Equal(
      -ViewGates.HeldFloor(World, sunk),
      double.Parse(
        ViewGates
          .Rules(World, sunk)
          .First(r => r.Gate == "G9")
          .Detail.Split(' ')[0],
        System.Globalization.CultureInfo.InvariantCulture
      ),
      0.01
    );

    ViewGroup hay = GroupOf("game:hay-normal-ud");
    Assert.True(ViewGates.HeldFloor(World, hay, true) >= 0);

    ModelTransform lowHay = hay.Hand.Clone();
    lowHay.Translation = new FastVec3f(
      lowHay.Translation.X,
      lowHay.Translation.Y,
      lowHay.Translation.Z - 200
    );
    ViewGroup sunkHay = hay with { Hand = lowHay };

    Assert.Equal(["G9", "G9fp"], Failing(sunkHay));
    Assert.True(ViewGates.HeldFloor(World, sunkHay, true) < -1);
  }

#if GAME_GE_1_22
  // Fails when the clip match is made case-sensitive: SpearIdle then fails. A long tool held on any clip but spearidle
  // fails G11 alone; a plate held on crudeOarIdle, a clip that resolves, passes.
  [Theory]
  [InlineData("helditemready", true)]
  [InlineData("spearidlee", true)]
  [InlineData("crudeOarIdle", true)]
  [InlineData("SpearIdle", false)]
  public void Clip_rules(string held, bool fails)
  {
    ViewGroup spear = GroupOf("game:spear-generic-iron");
    Assert.Equal(ItemViewKind.Longtool, spear.Kind);
    Assert.Empty(Failing(spear));

    string[] failing = Failing(spear with { Held = held });

    Assert.Equal(fails ? ["G11"] : [], failing);
  }

  // Fails when a plate held on a clip that resolves is judged as a long tool: crudeOarIdle passes G11 on a plate.
  [Fact]
  public void A_plate_held_on_a_resolving_clip_passes_G11()
  {
    ViewGroup plate = GroupOf("game:metalplate-iron");
    Assert.Empty(Failing(plate with { Held = "crudeOarIdle" }));
  }

  // Fails when a long tool is readied or hit on a clip other than the spear's: the ready clip and the hit clip are each
  // read from the representative and take G11 alone.
  [Fact]
  public void A_long_tool_readied_or_hit_on_another_clip_fails_G11()
  {
    CollectibleObject spear = GroupOf("game:spear-generic-iron").Representative;
    string? ready = spear.HeldRightReadyAnimation;
    string? hit = spear.HeldTpHitAnimation;
    try
    {
      spear.HeldRightReadyAnimation = "crudeOarIdle";
      Assert.Equal(["G11"], Failing(GroupOf("game:spear-generic-iron")));
      spear.HeldRightReadyAnimation = ready;

      spear.HeldTpHitAnimation = "breakhand";
      Assert.Equal(["G11"], Failing(GroupOf("game:spear-generic-iron")));
    }
    finally
    {
      spear.HeldRightReadyAnimation = ready;
      spear.HeldTpHitAnimation = hit;
    }
  }

  // Fails when a left-hand clip is not read: a misspelt left idle or left ready clip then fails G11 alone.
  [Fact]
  public void A_misspelt_left_hand_clip_fails_G11()
  {
    CollectibleObject spear = GroupOf("game:spear-generic-iron").Representative;
    string? idle = spear.HeldLeftTpIdleAnimation;
    string? ready = spear.HeldLeftReadyAnimation;
    try
    {
      spear.HeldLeftTpIdleAnimation = "spearidlee";
      Assert.Equal(["G11"], Failing(GroupOf("game:spear-generic-iron")));
      spear.HeldLeftTpIdleAnimation = idle;

      spear.HeldLeftReadyAnimation = "spearidlee";
      Assert.Equal(["G11"], Failing(GroupOf("game:spear-generic-iron")));
    }
    finally
    {
      spear.HeldLeftTpIdleAnimation = idle;
      spear.HeldLeftReadyAnimation = ready;
    }
  }
#endif

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

  // Fails when G12's comparison is inverted: the rivetrod drawn 1 x 1 x 40 then passes a row of 1 x 1 x 10 and the
  // uprightsheet fails its own row.
  [Fact]
  public void A_child_segment_on_rivetrod_fails() {
    ViewGroup rod = ViewGates
      .Groups(World, "viewsfixture")
      .Single(g => g.Codes.Contains("viewsfixture:rivetrod"));

    GateReading wrong = Catalogue("wrong").Reading(rod)!;

    Assert.False(wrong.Pass);
    Assert.Equal(30, wrong.Value, 0.001);
    Assert.Contains("1 x 1 x 40 against 1 x 1 x 10 at", wrong.Detail);
    Assert.Contains("stock.md:3", wrong.Detail);
    Assert.Equal(0, Catalogue("sized").Reading(Sheet)!.Value, 0.001);
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
  // composite row and a row with no size are skipped, and an unregistered code fails G12, naming its row.
  [Fact]
  public void Rows_skip_in_order() {
    ViewCatalogue sized = Catalogue("sized");

    Assert.Equal(
      ["viewsfixture:uprightsheet", "viewsfixture:phantom"],
      sized.Rows.Select(r => r.Code)
    );
    ViewFinding finding = Assert.Single(sized.Unregistered(World));
    Assert.Contains(
      sized.Rows.Single(r => r.Code == "viewsfixture:phantom").Where,
      finding.Message
    );
  }

#if GAME_GE_1_22
  // Fails when Findings leaves out the unregistered rows: phantom then has no finding. A domain with no collectibles has
  // none of the pages' rows, so it fails when the rows are not cut to the domain.
  [Fact]
  public void Findings_carry_the_unregistered_rows_of_the_domain()
  {
    ViewCatalogue sized = Catalogue("sized");

    ViewFinding phantom = Assert.Single(
      ViewGates.Findings(World, "viewsfixture", sized),
      f => f.Key.EndsWith("G12 unregistered")
    );

    Assert.Equal(
      "viewsfixture:phantom [catalogue] G12 unregistered",
      phantom.Key
    );
    Assert.Contains("stock.md:9", phantom.Message);
    Assert.Empty(ViewGates.Findings(World, "nodomain", sized));
  }

  // Fails when Findings leaves out the rules: the rivetrod then has no G12 finding.
  [Fact]
  public void Findings_carry_the_catalogue_rule_of_each_group()
  {
    Assert.Contains(
      ViewGates.Findings(World, "viewsfixture", Catalogue("wrong")),
      f =>
        f.Key.StartsWith("viewsfixture:rivetrod")
        && f.Key.EndsWith("G12 catalogue")
    );
  }
#endif
}
