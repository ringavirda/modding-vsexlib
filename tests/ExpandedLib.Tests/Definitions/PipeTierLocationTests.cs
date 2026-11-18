using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Pins the tier axis: two pipe tiers must share no <see cref="ExBlockDef.Location"/> or shape.</summary>
public class PipeTierLocationTests {
  private const string Domain = "iiex";

  /// <summary>Domains owned by a content mod.</summary>
  private static readonly string[] ContentDomains =
  [
    "iiex",
    "iiex",
    "smex",
    "hpex",
    "iiex",
    "siex",
  ];

  private static List<ExBlockDef> Segments(string tier) =>
    [.. BlockPipe.Segments(Domain, tier)];

  private static List<ExBlockDef> Passthroughs(string tier) =>
    [.. BlockPipePassthrough.Passthroughs(Domain, tier)];

  private static List<ExBlockDef> Tier(string tier) =>
    [.. Segments(tier), .. Passthroughs(tier)];

  [Fact]
  public void Two_tiers_in_one_domain_share_no_definition_location() {
    string[] shared =
    [
      .. Tier(BlockPipe.PlatedTier)
        .Select(d => d.Location.ToString())
        .Intersect(Tier(BlockPipe.CastTier).Select(d => d.Location.ToString()))
        .OrderBy(p => p),
    ];

    Assert.True(
      shared.Length == 0,
      $"{shared.Length} definition location(s) are claimed by both the plated and the cast tier. "
        + "Under one domain the second def loaded replaces the first in ExDefinitions, last-writer-wins "
        + "and unlogged, so one whole tier stops existing while every code-level guard stays green:\n  "
        + string.Join("\n  ", shared)
    );
  }

  [Fact]
  public void Every_tier_declares_the_same_number_of_definitions() {
    // Guards the assertion above against a tier that collapsed to nothing.
    int plated = Tier(BlockPipe.PlatedTier).Count;

    Assert.True(plated > 0, "the plated tier declares no definitions at all");
    Assert.Equal(plated, Tier(BlockPipe.CastTier).Count);
    Assert.Equal(plated, Tier(BlockPipe.RolledTier).Count);
  }

  [Fact]
  public void Two_tiers_in_one_domain_share_no_segment_shape() {
    string[] shared =
    [
      .. ShapesOf(Segments(BlockPipe.PlatedTier))
        .Intersect(ShapesOf(Segments(BlockPipe.CastTier)))
        .OrderBy(p => p),
    ];

    Assert.True(
      shared.Length == 0,
      $"{shared.Length} segment shape path(s) are claimed by both tiers; under one domain they resolve "
        + "to one art file and the surviving tier renders the other's mesh:\n  "
        + string.Join("\n  ", shared)
    );
  }

  [Fact]
  public void Two_tiers_in_one_domain_share_no_passthrough_shape() {
    // Fails when a tiered passthrough or bend is drawn with the shared exlib shape again.
    string[] shared =
    [
      .. ShapesOf(Passthroughs(BlockPipe.PlatedTier))
        .Intersect(ShapesOf(Passthroughs(BlockPipe.CastTier)))
        .OrderBy(p => p),
    ];

    Assert.True(
      shared.Length == 0,
      $"{shared.Length} passthrough shape path(s) are claimed by both tiers:\n  "
        + string.Join("\n  ", shared)
    );
    Assert.All(
      ShapesOf(Passthroughs(BlockPipe.CastTier)),
      s => Assert.StartsWith($"{Domain}:pipe/{BlockPipe.CastTier}/", s)
    );
  }

  [Fact]
  public void A_tierless_passthrough_uses_the_shared_exlib_shapes() {
    // Fails when a tierless call stops using exlib:pipe/passthrough and passthroughbend.
    string[] shapes =
    [
      .. ShapesOf(BlockPipePassthrough.Passthroughs(Domain, tier: null))
        .OrderBy(s => s),
    ];

    Assert.Equal(
      ["exlib:pipe/passthrough", "exlib:pipe/passthroughbend"],
      shapes
    );
  }

  [Fact]
  public void No_pipe_shape_is_pinned_to_a_content_mod_domain() {
    // exlib emits these defs for every tier; a content domain here is a cross-assembly literal.
    string[] pinned =
    [
      .. new[]
      {
        BlockPipe.PlatedTier,
        BlockPipe.CastTier,
        BlockPipe.RolledTier,
      }
        .SelectMany(t => ShapesOf(Tier(t)))
        .Where(s =>
          ContentDomains.Contains(s.Split(':')[0]) && s.Split(':')[0] != Domain
        )
        .Distinct()
        .OrderBy(s => s),
    ];

    Assert.True(
      pinned.Length == 0,
      $"{pinned.Length} shape path(s) emitted by exlib name a content mod's domain. Shared library art "
        + "belongs in exlib's own tree; pinned like this it resolves to nothing once that mod is "
        + "renamed or merged, and the blocktype then loads with no shape and no error:\n  "
        + string.Join("\n  ", pinned)
    );
  }

  private static IEnumerable<string> ShapesOf(IEnumerable<ExBlockDef> defs) =>
    defs.SelectMany(d =>
        d.ToJson()["shapebytype"] is { } byType
          ? byType
            .Children<JProperty>()
            .Select(p => p.Value["base"]?.ToString() ?? "")
          : []
      )
      .Where(s => s.Length > 0)
      .Distinct();
}
