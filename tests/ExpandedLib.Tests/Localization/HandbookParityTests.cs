using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Parity guard between handbook prose under <c>docs/{domain}/handbook/</c> and its lang-key
/// text. See <see cref="HandbookSync"/> for the transform.</summary>
public class HandbookParityTests {
  // Plain Facts, not [Theory]/[MemberData]: exlib's own case list is legitimately empty.

  [Fact]
  public void Every_shipped_handbook_page_matches_its_authoring_source() {
    var failures = new List<string>();
    foreach (string domain in HandbookSync.Domains())
      foreach (HandbookSync.Page page in HandbookSync.Pages(domain)) {
        var (ok, message) = HandbookSync.Check(page);
        if (!ok)
          failures.Add(message);
      }

    Assert.True(failures.Count == 0, string.Join("\n", failures));
  }

  [Fact]
  public void Every_handbook_page_is_wired_end_to_end() {
    // Checks the wiring, not the prose: a shipped page with no source, a source that ships nowhere,
    // a descriptor pointing at an undefined key.
    var problems = new List<string>();
    foreach (string domain in HandbookSync.Domains())
      problems.AddRange(HandbookSync.Problems(domain));

    Assert.True(problems.Count == 0, string.Join("\n", problems));
  }

  /// <summary>Adopts every edited authoring source into its lang file when
  /// <c>EXLIB_WRITE_HANDBOOK=1</c> is set.</summary>
  [Fact]
  public void Regenerate_handbook_text_when_requested() {
    if (!HandbookSync.WriteRequested)
      return;

    foreach (string domain in HandbookSync.Domains())
      HandbookSync.WriteAll(domain);
  }

  /// <summary>Rewrites the authoring sources from the shipped text when
  /// <c>EXLIB_EXPORT_HANDBOOK=1</c> is set.</summary>
  [Fact]
  public void Export_handbook_text_to_sources_when_requested() {
    if (
      System.Environment.GetEnvironmentVariable("EXLIB_EXPORT_HANDBOOK") != "1"
    )
      return;

    foreach (string domain in HandbookSync.Domains())
      HandbookSync.ExportAll(domain);
  }
}
