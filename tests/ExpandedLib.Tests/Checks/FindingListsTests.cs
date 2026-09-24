using System;
using System.Collections.Generic;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="FindingLists.Assert"/> over findings keyed by the file before the colon.
/// </summary>
public class FindingListsTests {
  private static readonly Dictionary<string, string> None = new();

  private static string FileOf(string finding) => finding.Split(':')[0];

  // Fails when Assert lets through a finding neither list keys.
  [Fact]
  public void A_finding_on_neither_list_fails_naming_it() {
    var ex = Assert.Throws<InvalidOperationException>(() =>
      FindingLists.Assert(
        ["A.cs:3: bad", "B.cs:9: bad"],
        new Dictionary<string, string> { ["A.cs"] = "ruled" },
        None,
        FileOf
      )
    );

    Assert.Contains("1 finding(s) on neither list", ex.Message);
    Assert.Contains("B.cs:9: bad", ex.Message);
    Assert.DoesNotContain("A.cs:3", ex.Message);
  }

  // Fails when Assert passes a known entry the rule does not report.
  [Fact]
  public void A_known_finding_that_no_longer_fails_fails_naming_it() {
    var ex = Assert.Throws<InvalidOperationException>(() =>
      FindingLists.Assert(
        ["A.cs:3: bad"],
        None,
        new Dictionary<string, string> {
          ["A.cs"] = "awaits a fix",
          ["Gone.cs"] = "was fixed",
        },
        FileOf
      )
    );

    Assert.Contains("1 known finding(s) that no longer fail", ex.Message);
    Assert.Contains("Gone.cs", ex.Message);
  }

  // Fails when Assert fails an allowed entry that keys nothing, or a listed finding.
  [Fact]
  public void Listed_findings_and_an_unused_allowance_pass() {
    FindingLists.Assert(
      ["A.cs:3: bad", "K.cs:1: bad"],
      new Dictionary<string, string> {
        ["A.cs"] = "ruled",
        ["Unused.cs"] = "ruled",
      },
      new Dictionary<string, string> { ["K.cs"] = "awaits a fix" },
      FileOf
    );
  }

  // Fails when Assert accepts a key on both lists or an entry with no reason.
  [Fact]
  public void A_key_on_both_lists_or_without_a_reason_is_rejected() {
    Assert.Throws<ArgumentException>(() =>
      FindingLists.Assert(
        [],
        new Dictionary<string, string> { ["A.cs"] = "ruled" },
        new Dictionary<string, string> { ["A.cs"] = "awaits a fix" }
      )
    );
    Assert.Throws<ArgumentException>(() =>
      FindingLists.Assert(
        [],
        new Dictionary<string, string> { ["A.cs"] = " " },
        None
      )
    );
  }

  // Fails when Assert keys a finding by anything but its whole text when no key is given.
  [Fact]
  public void Without_a_key_a_finding_is_keyed_by_its_whole_text() {
    FindingLists.Assert(
      ["whole line"],
      None,
      new Dictionary<string, string> { ["whole line"] = "awaits a fix" }
    );
  }
}
