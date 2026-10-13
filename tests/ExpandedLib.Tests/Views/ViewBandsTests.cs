using System;
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
      0.005
    );
    Assert.Equal(
      ViewReferences.MetalPlate.Scale,
      rules["metalplate"]["handScale"],
      0.005
    );
    Assert.Equal(
      ViewReferences.Ingot.Scale,
      rules["ingot"]["handScale"],
      0.005
    );
    Assert.Equal(ViewReferences.Rod.Scale, rules["rod"]["handScale"], 0.005);
    Assert.Equal(
      ViewReferences.Spear.GroundScale,
      rules["spear"]["groundScale"],
      0.05
    );
    Assert.Equal(
      ViewReferences.MetalPlate.GroundScale,
      rules["metalplate"]["groundScale"],
      0.05
    );
    Assert.Equal(
      ViewReferences.Ingot.GroundScale,
      rules["ingot"]["groundScale"],
      0.05
    );
    Assert.Equal(
      ViewReferences.Rod.GroundScale,
      rules["rod"]["groundScale"],
      0.05
    );
  }

  // Fails when a reference rotation moves off the vanilla item it copies: the rod's hand rotation about x 85 raised to 87.
  [Fact]
  public void View_bands_agree_with_the_rotations_and_spear_constants() {
    var rules = Bands.Rules;
    foreach (
      (string name, ViewReferences.HandReference reference) in new[]
      {
        ("spear", ViewReferences.Spear),
        ("metalplate", ViewReferences.MetalPlate),
        ("ingot", ViewReferences.Ingot),
        ("rod", ViewReferences.Rod),
      }
    ) {
      Assert.Equal(reference.Rotation.X, rules[name]["handRotationX"], 0.5);
      Assert.Equal(reference.Rotation.Y, rules[name]["handRotationY"], 0.5);
      Assert.Equal(reference.Rotation.Z, rules[name]["handRotationZ"], 0.5);
      Assert.Equal(reference.GuiRotation.X, rules[name]["guiRotationX"], 0.5);
      Assert.Equal(reference.GuiRotation.Y, rules[name]["guiRotationY"], 0.5);
      Assert.Equal(reference.GuiRotation.Z, rules[name]["guiRotationZ"], 0.5);
    }
    Assert.Equal(
      ViewReferences.SpearSlotScale,
      rules["spear"]["slotScale"],
      0.005
    );
    Assert.Equal(ViewReferences.SpearLength, rules["spear"]["length"], 0.005);
  }

  // Fails when a block median moves off the partial or multicell reference set it copies: the partial slot span 0.710 raised to 0.75.
  [Fact]
  public void View_bands_agree_with_the_block_medians() {
    var rules = Bands.Rules;
    Assert.Equal(
      ViewReferences.PartialSlotSpan,
      rules["partial"]["slotSpan"],
      0.01
    );
    Assert.Equal(
      ViewReferences.PartialGroundLong,
      rules["partial"]["groundLong"],
      0.005
    );
    Assert.Equal(
      ViewReferences.PartialHeldLong,
      rules["partial"]["heldLong"],
      0.005
    );
    Assert.Equal(
      ViewReferences.MulticellSlotSpan,
      rules["multicell"]["slotSpan"],
      0.01
    );
    Assert.Equal(
      ViewReferences.MulticellGroundLong,
      rules["multicell"]["groundLong"],
      0.005
    );
    Assert.Equal(
      ViewReferences.MulticellHeldLong,
      rules["multicell"]["heldLong"],
      0.005
    );
  }

  private static (ViewGroup Group, GroupMetrics Metrics) Reference(string gate) {
    string code = gate switch {
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
    CollectibleObject block = group.Representative;
    ModelTransform authored = block.TpHandTransform;
    try {
      ModelTransform raised = ViewMatrices
        .Transform(block, ViewTarget.Hand)
        .Clone();
      raised.Translation = new FastVec3f(
        raised.Translation.X,
        raised.Translation.Y + 6,
        raised.Translation.Z
      );
      block.TpHandTransform = raised;
      GroupMetrics floating = ViewGates.Measure(World, group);
      Assert.Equal(1.8, floating.Hand.Centre!.Value, 0.05);
      ViewFinding finding = Assert.Single(
        ViewGates.Judge(Bands, group, floating)
      );
      Assert.EndsWith("G13 hand.centre", finding.Key);
    } finally {
      block.TpHandTransform = authored;
    }
    Assert.Empty(ViewGates.Judge(Bands, group, metrics));
  }
}
