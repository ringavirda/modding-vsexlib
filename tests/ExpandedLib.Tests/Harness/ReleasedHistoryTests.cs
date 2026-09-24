using System;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ReleasedHistory"/>'s release rows over a mod id no seed uses, and
/// <see cref="ReleasedVersions.Compare"/>.</summary>
public class ReleasedHistoryTests {
  // Fails when a release row's codes stay out of the shipped history, its version out of
  // HighestPublished, or the rows are kept in registration order.
  [Fact]
  public void A_release_row_adds_its_codes_and_its_version() {
    const string mod = "releasedhistorytestmod";
    ReleasedHistory.Register(
      mod,
      "1.2.0",
      [new(mod, "gadget", $"{mod}:gadget", [$"{mod}:gadget"])]
    );
    ReleasedHistory.Register(mod, "1.10.0", []);
    ReleasedHistory.Register(mod, "1.3.0", []);

    Assert.Equal(
      ["1.2.0", "1.3.0", "1.10.0"],
      ReleasedHistory.Releases(mod).Select(r => r.Version)
    );
    Assert.Contains(
      ReleasedHistory.AllShipped,
      s => s.BaseCode == $"{mod}:gadget"
    );
    Assert.Equal("1.10.0", ReleasedVersions.HighestPublished[mod]);
  }

  // Fails when exlib's seed loses a 0.8.x row, or a row gains a code no tag shipped.
  [Fact]
  public void Exlibs_seed_holds_the_three_0_8_releases_adding_no_code() {
    var rows = ReleasedHistory.Releases("exlib");

    Assert.Equal(["0.8.0", "0.8.1", "0.8.2"], rows.Select(r => r.Version));
    Assert.All(rows, r => Assert.Empty(r.Added));
    Assert.Equal(
      ["exlib:structurefiller"],
      ReleasedCodes.Exlib.SelectMany(s => s.Codes)
    );
  }

  // Fails when Compare orders by text, ranks a pre-release above its release, or accepts a
  // version it cannot read.
  [Fact]
  public void Compare_orders_numerically_with_pre_releases_first() {
    Assert.True(ReleasedVersions.Compare("0.10.0", "0.9.9") > 0);
    Assert.True(ReleasedVersions.Compare("0.8.0-preview.3", "0.8.0") < 0);
    Assert.Equal(0, ReleasedVersions.Compare("0.8.2", "0.8.2"));
    Assert.Throws<ArgumentException>(() =>
      ReleasedVersions.Compare("0.8", "0.8.2")
    );
  }
}
