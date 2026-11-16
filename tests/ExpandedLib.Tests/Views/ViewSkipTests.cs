using System;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The item-view gates and the sweep given a reason to skip a code, on the views fixture mod.</summary>
[Collection("ItemViews")]
public class ViewSkipTests {
  private const string Domain = "viewsfixture";
  private const string Shaft = "viewsfixture:authoredshaft";
  private const string Twin = "viewsfixture:authoredshafttwin";
  private const string ShaftFinding = Shaft + " [longtool] G3 slot.face";

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

  private static Func<string, string?> Skipping(params string[] codes) =>
    code => codes.Contains(code) ? "not holdable" : null;

  private static string[] Keys(Func<string, string?>? skipOf) =>
    [.. ViewGates.Findings(World, Domain, null, skipOf).Select(f => f.Key)];

  // Fails when Findings ignores skipOf: the skipped shaft then keeps its G3 finding.
  [Fact]
  public void A_skipped_code_with_a_bad_slot_view_has_no_finding() {
    Assert.Contains(ShaftFinding, Keys(null));

    string[] skipped = Keys(Skipping(Shaft, Twin));

    Assert.DoesNotContain(skipped, k => k.StartsWith(Shaft));
    Assert.Contains(skipped, k => k.StartsWith("viewsfixture:longshaft"));
  }

  // Fails when Findings skips every group once skipOf is given: the shaft's group is then skipped though skipOf names
  // no code of it.
  [Fact]
  public void A_code_with_no_reason_is_judged_when_others_are_skipped() {
    string[] keys = Keys(Skipping("viewsfixture:longshaft"));

    Assert.Contains(ShaftFinding, keys);
    Assert.DoesNotContain(keys, k => k.StartsWith("viewsfixture:longshaft"));
  }

  // Fails when a group is skipped for its lead alone: the twin then loses the shaft's findings.
  [Fact]
  public void A_group_with_a_judged_member_keeps_its_findings_under_its_lead() {
    ViewGroup group = ViewGates
      .Groups(World, Domain)
      .Single(g => g.Codes.Contains(Shaft));
    Assert.Equal([Shaft, Twin], group.Codes);

    string[] keys = Keys(Skipping(Shaft));

    Assert.Contains(ShaftFinding, keys);
    Assert.DoesNotContain(Keys(Skipping(Shaft, Twin)), k => k == ShaftFinding);
  }

  // Fails when Findings drops G12 for a skipped group: the rivetrod then loses its catalogue finding.
  [Fact]
  public void G12_stays_for_a_skipped_group() {
    ViewCatalogue wrong = ViewCatalogue.Load(
      Path.Combine(
        RepoPaths.Root,
        "tests",
        "ExpandedLib.Tests",
        "Views",
        "Fixtures",
        "catalogue",
        "wrong"
      ),
      Domain
    )!;

    Assert.Contains(
      ViewGates.Findings(
        World,
        Domain,
        wrong,
        Skipping("viewsfixture:rivetrod")
      ),
      f =>
        f.Key.StartsWith("viewsfixture:rivetrod")
        && f.Key.EndsWith("G12 catalogue")
    );
  }

  // Fails when Rows leaves Skip empty for a skipped group: the twin then has a kind and the shaft's measurement.
  [Fact]
  public void Rows_name_the_skip_and_the_group_that_judges_a_code_anyway() {
    var rows = ViewSweep.Rows(World, Domain, Skipping(Shaft, Twin));
    SweepRow skipped = rows.Single(r => r.Code == Shaft);

    Assert.Equal("not holdable", skipped.Skip);
    Assert.Null(skipped.Kind);
    Assert.Empty(skipped.Failed);
    Assert.Equal("not holdable", rows.Single(r => r.Code == Twin).Skip);

    var mixed = ViewSweep.Rows(World, Domain, Skipping(Shaft));
    SweepRow lead = mixed.Single(r => r.Code == Shaft);
    SweepRow other = mixed.Single(r => r.Code == Twin);

    Assert.Null(lead.Skip);
    Assert.Equal("longtool", lead.Kind);
    Assert.Equal("not holdable; judged with its group", lead.Note);
    Assert.Contains("G3", lead.Failed);
    Assert.Null(other.Skip);
    Assert.Equal("", other.Note);
  }

  // Fails when Write always writes the game domain: the file then has no viewsfixture row.
  [Fact]
  public void Write_with_a_domain_writes_that_domains_rows() {
    const string variable = "EXLIB_TEST_VIEW_SKIP_SWEEP";
    string path = Path.Combine(
      Path.GetTempPath(),
      $"skip-{Guid.NewGuid()}.tsv"
    );
    Environment.SetEnvironmentVariable(variable, path);
    try {
      Assert.Equal(
        path,
        ViewSweep.Write(World, Domain, variable, Skipping(Shaft, Twin))
      );

      string[] lines = File.ReadAllLines(path);

      Assert.Contains(
        lines,
        l => l.StartsWith(Shaft + "\titem\t") && l.Contains("not holdable")
      );
      Assert.DoesNotContain(lines, l => l.StartsWith("game:"));
    } finally {
      Environment.SetEnvironmentVariable(variable, null);
      File.Delete(path);
    }
  }
}
