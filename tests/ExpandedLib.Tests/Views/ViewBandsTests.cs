using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The bands the view gates judge against, and the judgement itself, on the whole vanilla game.</summary>
[Collection("ItemViews")]
public class ViewBandsTests {
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

  private static ViewBandData Bands => ViewBands.Embedded;

  // Fails when one band's minimum is raised 10% past the set's: the difference names the vanilla code at that end.
  // EXLIB_WRITE_VIEW_BANDS=1 rewrites view-bands.json instead.
  [Fact]
  public void Bands_equal_what_the_reference_sets_compute() {
    ViewBandData computed = ViewBands.Compute(World);
    if (ViewBands.WriteRequested) {
      ViewBands.Write(computed);
      return;
    }
    var diffs = ViewBands.Differences(computed, Bands);
    Assert.True(diffs.Count == 0, string.Join("\n", diffs));
  }

  // Fails when the check for an unmatched entry is dropped: the set then loses a member without a word.
  [Fact]
  public void A_reference_entry_that_matches_nothing_throws() {
    var set = new ViewBands.ReferenceSet(
      ItemViewKind.Lump,
      ["ingot-", "no-such-thing"],
      false,
      false,
      ["G1"]
    );
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      ViewBands.Resolve(World, ViewGates.Groups(World, "game"), set)
    );
    Assert.Contains("no-such-thing", e.Message);
  }

  // Fails when a rule number moves off the vanilla reference it copies: the rod's hand scale 0.68 raised to 0.70.
  [Fact]
  public void View_bands_agree_with_the_rules_constants() {
    var rules = Bands.Rules;
    Assert.Equal(
      ViewReferences.Spear.Scale,
      rules["spear"]["handScale"],
      Precision
    );
    Assert.Equal(
      ViewReferences.MetalPlate.Scale,
      rules["metalplate"]["handScale"],
      Precision
    );
    Assert.Equal(
      ViewReferences.Ingot.Scale,
      rules["ingot"]["handScale"],
      Precision
    );
    Assert.Equal(
      ViewReferences.Rod.Scale,
      rules["rod"]["handScale"],
      Precision
    );
    Assert.Equal(
      ViewReferences.Spear.GroundScale,
      rules["spear"]["groundScale"],
      Precision
    );
    Assert.Equal(
      ViewReferences.MetalPlate.GroundScale,
      rules["metalplate"]["groundScale"],
      Precision
    );
    Assert.Equal(
      ViewReferences.Ingot.GroundScale,
      rules["ingot"]["groundScale"],
      Precision
    );
    Assert.Equal(
      ViewReferences.Rod.GroundScale,
      rules["rod"]["groundScale"],
      Precision
    );
  }

  // Fails when a reference rotation moves off the vanilla item it copies: the rod's hand rotation about x 85 raised to 87.
  [Fact]
  public void View_bands_agree_with_the_rotations_and_spear_constants() {
    var rules = Bands.Rules;
    foreach (
      (string name, ViewReferences.HandReference reference) in References()
    ) {
      AssertSameRotation(reference.Rotation, rules[name], "handRotation");
      AssertSameRotation(reference.GuiRotation, rules[name], "guiRotation");
    }
    Assert.Equal(
      ViewReferences.SpearSlotScale,
      rules["spear"]["slotScale"],
      Precision
    );
    Assert.Equal(
      ViewReferences.SpearLength,
      rules["spear"]["length"],
      Precision
    );
    Assert.Equal(
      ViewReferences.SpearSlotOrigin,
      rules["spear"]["slotOrigin"],
      Precision
    );
  }

  // Fails when a grip fraction, the gap or the thickness moves off the vanilla item it copies: the spear's grip x 0.3842 raised to 0.3852.
  [Fact]
  public void View_bands_agree_with_the_grip_constants() {
    var rules = Bands.Rules;
    foreach (
      (string name, ViewReferences.HandReference reference) in References()
    ) {
      for (int k = 0; k < 3; k++)
        Assert.Equal(
          reference.Grip[k],
          rules[name]["grip" + "XYZ"[k]],
          Precision
        );
      Assert.Equal(reference.OffDrawn, rules[name]["offDrawn"], Precision);
      Assert.Equal(reference.Thickness, rules[name]["thickness"], Precision);
    }
  }

  // Fails when a block median moves off the partial or multicell reference set it copies: the partial slot span 0.7101 raised to 0.7111.
  [Fact]
  public void View_bands_agree_with_the_block_medians() {
    var rules = Bands.Rules;
    Assert.Equal(
      ViewReferences.PartialSlotSpan,
      rules["partial"]["slotSpan"],
      Precision
    );
    Assert.Equal(
      ViewReferences.PartialGroundLong,
      rules["partial"]["groundLong"],
      Precision
    );
    Assert.Equal(
      ViewReferences.PartialHeldLong,
      rules["partial"]["heldLong"],
      Precision
    );
    Assert.Equal(
      ViewReferences.MulticellSlotSpan,
      rules["multicell"]["slotSpan"],
      Precision
    );
    Assert.Equal(
      ViewReferences.MulticellGroundLong,
      rules["multicell"]["groundLong"],
      Precision
    );
    Assert.Equal(
      ViewReferences.MulticellHeldLong,
      rules["multicell"]["heldLong"],
      Precision
    );
  }

  // Fails when a reference member leaves the band of a gate its set calibrates: the boar spear when its tip entry is dropped,
  // the erel spear when the working end is taken from the far end from the grip.
  [Fact]
  public void Every_reference_member_passes_the_gates_it_calibrates() {
    IReadOnlyList<ViewGroup> groups = ViewGates.Groups(World, "game");
    foreach (ViewBands.ReferenceSet set in ViewBands.Sets) {
      string kind = set.Kind.ToString().ToLowerInvariant();
      foreach (
        (ViewGroup group, string code) in ViewBands.Resolve(World, groups, set)
      ) {
        GroupMetrics metrics = ViewGates.Measure(World, group);
        foreach (string gate in set.Gates) {
          double v = ViewGates.ValueOf(metrics, gate);
          if (double.IsNaN(v))
            continue;
          Band band = Bands.Bands[kind][gate];
          Assert.True(
            v >= band.Low && v <= band.High,
            $"{code} [{kind}] {gate} {v} outside {band.Low}..{band.High}"
          );
        }
      }
    }
  }

  private const double Precision = 1e-4;

  private static (
    string Name,
    ViewReferences.HandReference Reference
  )[] References() =>
    [
      ("spear", ViewReferences.Spear),
      ("metalplate", ViewReferences.MetalPlate),
      ("ingot", ViewReferences.Ingot),
      ("rod", ViewReferences.Rod),
    ];

  // Each entry of the two rotation matrices agrees, since several Euler triples give one orientation.
  private static void AssertSameRotation(
    Vec3d expected,
    IReadOnlyDictionary<string, double> rule,
    string key
  ) {
    Mat4 want = Mat4.Identity.RotateXYZ(expected.X, expected.Y, expected.Z);
    Mat4 got = Mat4.Identity.RotateXYZ(
      rule[key + "X"],
      rule[key + "Y"],
      rule[key + "Z"]
    );
    for (int row = 0; row < 4; row++)
      for (int col = 0; col < 4; col++)
        Assert.Equal(want[row, col], got[row, col], Precision);
  }

  private static (ViewGroup Group, GroupMetrics Metrics) Reference(
    string gate,
    string? code = null
  ) {
    code ??= gate switch {
      "G10" or "G10fp" => "game:spear-generic-iron",
      "G13" => "game:hay-normal-ud",
      _ => "game:ingot-iron",
    };
    ViewGroup group = GroupOf(code);
    return (group, ViewGates.Measure(World, group));
  }

  private static GroupMetrics With(GroupMetrics m, string gate, double v) =>
    gate switch {
      "G1" => m with { Slot = m.Slot with { Span = v } },
      "G2" => m with { Slot = m.Slot with { Centre = v } },
      "G3" => m with { Slot = m.Slot with { Face = v } },
      "G5" => m with { Ground = m.Ground with { Rest = v } },
      "G6" => m with { Ground = m.Ground with { Size = v } },
      "G7" => m with { Hand = m.Hand with { Size = v } },
      "G8" => m with { Hand = m.Hand with { Grip = v } },
      "G8fp" => m with { HandFp = m.HandFp! with { Grip = v } },
      "G10" => m with { Axis = v },
      "G10fp" => m with { AxisFp = v },
      "G13" => m with { Hand = m.Hand with { Centre = v } },
      _ => throw new ArgumentException(gate),
    };

  // Fails when any gate's comparison is inverted: the in-band reference then fails, or the out-of-band value passes.
  [Theory]
  [InlineData("G1", "G1", "slot.span", 1)]
  [InlineData("G1", "G1", "slot.span", -1)]
  [InlineData("G2", "G2", "slot.centre", 1)]
  [InlineData("G3", "G3", "slot.face", 1)]
  [InlineData("G3", "G3", "slot.face", -1)]
  [InlineData("G5", "G5", "ground.rest", 1)]
  [InlineData("G5", "G5", "ground.rest", -1)]
  [InlineData("G6", "G6", "ground.size", 1)]
  [InlineData("G6", "G6", "ground.size", -1)]
  [InlineData("G7", "G7", "hand.size", 1)]
  [InlineData("G7", "G7", "hand.size", -1)]
  [InlineData("G8", "G8", "hand.grip", 1)]
  [InlineData("G8fp", "G8", "hand.grip.fp", 1)]
  [InlineData("G10", "G10", "hand.axis", 1)]
  [InlineData("G10fp", "G10", "hand.axis.fp", 1)]
  [InlineData("G13", "G13", "hand.centre", 1)]
  public void Each_banded_gate_has_a_fixture_only_it_rejects(
    string id,
    string gate,
    string name,
    int side
  ) {
    (ViewGroup group, GroupMetrics metrics) = Reference(id);
    Assert.Empty(ViewGates.Judge(Bands, group, metrics));

    string kind = group.Kind.ToString().ToLowerInvariant();
    double v = id switch {
      "G8" or "G8fp" => Bands.Limits["G8"] + 1,
      _ when side > 0 => Bands.Bands[kind][id].High + 1,
      _ => Bands.Bands[kind][id].Low - 1,
    };
    ViewFinding finding = Assert.Single(
      ViewGates.Judge(Bands, group, With(metrics, id, v))
    );
    Assert.Equal(
      $"{group.Representative.Code} [{kind}] {gate} {name}",
      finding.Key
    );
  }

  // Fails when the bulk limit is dropped: a grip of 1.5 voxel then fails a bulk group, or the lump limit is widened and passes.
  [Fact]
  public void The_grip_limit_is_wider_for_bulk() {
    (ViewGroup group, GroupMetrics metrics) = Reference("G8");
    GroupMetrics loose = With(metrics, "G8", 1.5);
    Assert.Contains(
      ViewGates.Judge(Bands, group, loose),
      f => f.Key.EndsWith("G8 hand.grip")
    );
    Assert.DoesNotContain(
      ViewGates.Judge(Bands, group with { Kind = ItemViewKind.Bulk }, loose),
      f => f.Key.EndsWith("G8 hand.grip")
    );
    Assert.Contains(
      ViewGates.Judge(
        Bands,
        group with {
          Kind = ItemViewKind.Bulk,
        },
        With(metrics, "G8", 2.5)
      ),
      f => f.Key.EndsWith("G8 hand.grip")
    );
  }

  // Fails when a workpiece is banded: its views are vanilla's, and the rules judge them.
  [Fact]
  public void A_workpiece_has_no_band() {
    (ViewGroup group, GroupMetrics metrics) = Reference("G8");
    Assert.Empty(
      ViewGates.Judge(
        Bands,
        group with {
          Kind = ItemViewKind.Workpiece,
        },
        With(metrics, "G7", 99)
      )
    );
  }

  // Fails when the message drops the set's ends or the group's other codes.
  [Fact]
  public void A_finding_names_the_view_kind_gate_band_and_every_code() {
    (ViewGroup group, GroupMetrics metrics) = Reference("G7");
    Band band = Bands.Bands["lump"]["G7"];
    ViewFinding finding = Assert.Single(
      ViewGates.Judge(Bands, group, With(metrics, "G7", 5))
    );
    Assert.Equal(
      $"{group.Representative.Code} [lump] G7 hand.size",
      finding.Key
    );
    Assert.StartsWith(finding.Key + " 5.00 outside ", finding.Message);
    Assert.Contains($"({band.MinCode} ", finding.Message);
    Assert.Contains($", {band.MaxCode} ", finding.Message);
    Assert.Contains("game:ingot-copper", finding.Message);
    Assert.Contains($" for {group.Codes.Count} codes: ", finding.Message);
  }

  // Fails when G13 judges the lower end: a cube carried 1.2 block above the hands then passes.
  [Fact]
  public void A_floating_block_fails_G13() {
    (ViewGroup group, GroupMetrics metrics) = Reference("G13");
    ModelTransform raised = group.Hand.Clone();
    raised.Translation = new FastVec3f(
      raised.Translation.X,
      raised.Translation.Y + 6,
      raised.Translation.Z
    );
    ViewGroup lifted = group with { Hand = raised };
    GroupMetrics floating = ViewGates.Measure(World, lifted);
    Assert.Equal(1.8, floating.Hand.Centre!.Value, 0.05);
    ViewFinding finding = Assert.Single(
      ViewGates.Judge(Bands, lifted, floating)
    );
    Assert.EndsWith("G13 hand.centre", finding.Key);
    Assert.Empty(ViewGates.Judge(Bands, group, metrics));
  }

  // Fails when G3's comparison is inverted, and when the raster counts #null faces.
  [Fact]
  public void An_edge_on_plate_fails_G3() {
    (ViewGroup plate, GroupMetrics metrics) = Reference(
      "G3",
      "game:metalplate-iron"
    );
    Assert.Empty(ViewGates.Judge(Bands, plate, metrics));

    ModelTransform upright = plate.Gui.Clone();
    upright.Rotation = new FastVec3f(0, -44, -180);
    ViewGroup edgeOn = plate with { Gui = upright };
    ViewFinding finding = Assert.Single(
      ViewGates.Judge(Bands, edgeOn, ViewGates.Measure(World, edgeOn))
    );
    Assert.EndsWith("G3 slot.face", finding.Key);
  }

  // Fails when the raster counts #null faces: a plate whose broad faces are #null then covers as much as the metalplate.
  [Fact]
  public void The_face_raster_skips_null_faces() {
    ViewGroup plate = GroupOf("game:metalplate-iron");
    var hollowItem = World.World.GetItem(
      new AssetLocation("viewsfixture:hollowplate")
    );
    ViewGroup hollow = ViewGates.GroupOf(World, hollowItem)! with {
      Gui = plate.Gui,
    };
    Assert.Equal(plate.Box.Extents(), hollow.Box.Extents());
    double full = ViewGates.Slot(plate).Face;
    double edges = ViewGates.Slot(hollow).Face;
    Assert.True(full > 0.5, $"{full}");
    Assert.True(edges < full / 2, $"{edges} against {full}");
  }

  private static (
    ViewGroup Group,
    GroupMetrics Metrics,
    ViewGates.ViewTwin Twin
  ) Greensand(Action<ItemViewSet>? change = null) {
    Item greensand = World.World.GetItem(
      new AssetLocation("viewsfixture:greensand")
    );
    ItemViewSet views = ItemViews.Compute(World.Api, greensand)!;
    change?.Invoke(views);
    ViewGroup group = ViewGates.Measured(
      ViewGates.GroupOf(World, greensand)!,
      views
    );
    return (
      group,
      ViewGates.Measure(World, group),
      ViewGates.TwinOf(World, group)!
    );
  }

  // Fails when the twin branch is dropped: greensand is then judged by its kind's band and limit.
  [Fact]
  public void A_vanilla_shape_twin_is_judged_against_its_twin() {
    (ViewGroup group, GroupMetrics metrics, ViewGates.ViewTwin twin) =
      Greensand();
    Assert.StartsWith("flour-", twin.Code);
    Assert.Empty(ViewGates.Judge(Bands, group, metrics, twin));
    GateReading grip = ViewGates
      .Readings(Bands, group, metrics, twin)
      .Single(r => r.Name == "hand.grip");
    Assert.True(grip.Value > 1, $"{grip.Value}");
    Assert.True(grip.Pass);
    Assert.NotEmpty(ViewGates.Judge(Bands, group, metrics));
  }

  // Fails when the message takes the kind's end codes: the finding then names the kind's reference, not the twin.
  [Fact]
  public void A_twin_off_its_twins_values_names_the_twin() {
    (_, _, ViewGates.ViewTwin twin) = Greensand();
    (ViewGroup group, GroupMetrics metrics, _) = Greensand(set =>
      set.TpHand.ScaleXYZ = new FastVec3f(
        set.TpHand.ScaleXYZ.X * 2,
        set.TpHand.ScaleXYZ.Y * 2,
        set.TpHand.ScaleXYZ.Z * 2
      )
    );
    ViewFinding finding = Assert.Single(
      ViewGates.Judge(Bands, group, metrics, twin),
      f => f.Key.EndsWith("G7 hand.size")
    );
    Assert.Contains($"({twin.Code} ", finding.Message);
  }
}
