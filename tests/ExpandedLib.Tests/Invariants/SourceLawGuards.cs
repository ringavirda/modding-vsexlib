using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>exlib's sources under <see cref="SourceLaws"/>: exchange, rotor, facing and search;
/// each hit is printed against the allowed and known lists.</summary>
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.StaleOnExchange))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.UndrivenRotor))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.LetterFacing))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.UnguardedSearch))]
public class SourceLawGuards(ITestOutputHelper output) {
  /// <summary>File and <c>FromCode</c> call, and why its argument is always a full word.</summary>
  private static readonly Dictionary<string, string> AllowedFacings = new() {
    ["ExOrientation.cs: BlockFacing.FromCode(side)"] =
      "FacingFromSide maps u, d and the four horizontal letters first, so side is a word here",
    [
      "ExOrientation.cs: BlockFacing.FromCode(SideFromAngle(AngleFromSide(side), asLetter: false))"
    ] = "SideFromAngle with asLetter: false returns a full word",
    ["BlockEntityStructureFiller.cs: BlockFacing.FromCode(faceCode)"] =
      "reads back the code ToTreeAttributes writes from BlockFacing.Code, a full word",
  };

  [Fact]
  public void A_block_entity_that_builds_for_its_facing_rebuilds_on_exchange() =>
    Assert(SourceLaws.StaleOnExchange, new(), new());

  [Fact]
  public void A_part_beside_a_mechanical_network_takes_its_frame_from_the_angle() =>
    Assert(SourceLaws.UndrivenRotor, new(), new());

  [Fact]
  public void A_facing_parsed_from_a_side_state_falls_back_to_its_first_letter() =>
    Assert(SourceLaws.LetterFacing, AllowedFacings, new());

  [Fact]
  public void A_block_search_is_checked_before_it_is_read() =>
    Assert(SourceLaws.UnguardedSearch, new(), new());

  // Prints every hit with the list that holds it, then asserts both lists.
  private void Assert(
    Func<IEnumerable<string>, IReadOnlyList<string>> law,
    Dictionary<string, string> allowed,
    Dictionary<string, string> known
  ) {
    IReadOnlyList<string> files = Premise.NotEmpty(Sources(), "exlib sources");
    IReadOnlyList<string> findings = law(files);
    output.WriteLine($"{files.Count} files, {findings.Count} hits");
    foreach (string finding in findings) {
      string key = SourceLaws.Key(finding);
      output.WriteLine(
        allowed.TryGetValue(key, out string? reason)
          ? $"allowed: {finding} ({reason})"
        : known.TryGetValue(key, out string? defect)
          ? $"known: {finding} ({defect})"
        : $"unlisted: {finding}"
      );
    }
    FindingLists.Assert(findings, allowed, known, SourceLaws.Key);
  }

  private static string[] Sources() =>
    Directory
      .EnumerateFiles(
        Path.Combine(RepoPaths.Root, "src"),
        "*.cs",
        SearchOption.AllDirectories
      )
      .Where(f =>
        !f.Contains("/bin/", StringComparison.Ordinal)
        && !f.Contains("/obj/", StringComparison.Ordinal)
      )
      .ToArray();
}
