using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>exlib's sources under <see cref="SourceLaws"/>: exchange, rotor, facing, search,
/// tunables, container dialogs and particles; each hit is printed against the allowed and known
/// lists.</summary>
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.StaleOnExchange))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.UndrivenRotor))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.LetterFacing))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.UnguardedSearch))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.CachedTunables))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.DisplayOnlyTunables))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.UnreadTunables))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.ContainerDialogPackets))]
[GuardOf(typeof(SourceLaws), nameof(SourceLaws.InlineParticles))]
public class SourceLawGuards(ITestOutputHelper output) {
  private static readonly Assembly[] Configs = [typeof(ExlibConfig).Assembly];

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

  /// <summary>File and member, and which edit never reaches it.</summary>
  private static readonly Dictionary<string, string> KnownTunables = new() {
    ["BEBehaviorMoltenCell.cs: BEBehaviorMoltenCell._cooldownSpeed"] =
      "no shipped cell declares cooldownSpeed, so every molten cell stamps its metal with the "
      + "MoltenCooldownDefault read when the cell was constructed; an edit reaches only cells "
      + "loaded after it",
  };

  private const string ConsumerValue =
    "a framework value for the mods built on exlib, which exlib's own sources do not read";

  /// <summary>File and value, and who reads it.</summary>
  private static readonly Dictionary<string, string> AllowedUnread = new() {
    ["ExlibConfig.cs: ExlibValues.AmbientTemperature"] =
      ConsumerValue + "; the SmokeStack sample reads it",
    ["ExlibConfig.cs: ExlibValues.MoltenMinFlowAmount"] =
      ConsumerValue
      + "; iiex BlockEntitySandCastingBed floors its transfers at it",
    ["ExlibConfig.cs: ExlibValues.MpGearMeshLoss"] =
      ConsumerValue
      + "; iiex BlockEntityTransmission loses it per second of coupling",
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

  [Fact]
  public void A_live_tunable_is_read_where_it_is_used() =>
    Assert(f => SourceLaws.CachedTunables(f, Configs), new(), KnownTunables);

  [Fact]
  public void A_tunable_is_read_by_more_than_its_text() =>
    Assert(f => SourceLaws.DisplayOnlyTunables(f, Configs), new(), new());

  [Fact]
  public void Every_tunable_is_read() =>
    Assert(f => SourceLaws.UnreadTunables(f, Configs), AllowedUnread, new());

  [Fact]
  public void A_container_that_opens_a_dialog_handles_its_packets() =>
    Assert(SourceLaws.ContainerDialogPackets, new(), new());

  [Fact]
  public void Particles_come_from_ExParticles() =>
    Assert(SourceLaws.InlineParticles, new(), new());

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
