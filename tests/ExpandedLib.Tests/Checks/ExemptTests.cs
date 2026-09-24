using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ExlibChecks.Exempt"/> over the loaded runs of a planted domain whose
/// collectibles carry null creative tab lists.</summary>
public sealed class ExemptTests : IDisposable {
  public ExemptTests() {
    TestModDomain.Register();
    ExlibChecks.ClearDeclarations();
  }

  public void Dispose() => ExlibChecks.ClearDeclarations();

  private const string Rule = "CollectibleCollections";

  private static string Untabbed(string code) =>
    $"{code}: CreativeInventoryTabs is null, which looking at a mount dereferences";

  private static LoadedStubGame Game() =>
    new LoadedStubGame(new RecipeStubSource().Covering("stub"))
      .Item("stub:untabbed1", item => item.CreativeInventoryTabs = null!)
      .Item("stub:untabbed10", item => item.CreativeInventoryTabs = null!);

  private static CheckResult Of(
    IReadOnlyList<CheckResult> results,
    string check
  ) => results.Single(r => r.Check == check);

  // Fails when an exemption stops taking the finding naming its code, takes one where the code
  // runs on into another word, takes one where another word runs into it, or applies in another
  // domain's run.
  [Fact]
  public void An_exemption_takes_only_the_finding_naming_its_code_as_a_word() {
    ExlibChecks.Exempt("stub", Rule, "stub:untabbed1", "planted reason");
    ExlibChecks.Exempt("stub", Rule, "tub:untabbed10", "planted inner word");
    ExlibChecks.Exempt(
      "other",
      Rule,
      "stub:untabbed10",
      "planted other domain"
    );

    CheckResult result = Of(ExlibChecks.LoadedFor(Game(), "stub"), Rule);

    Assert.Equal([Untabbed("stub:untabbed10")], result.Errors);
    Assert.Equal(
      [Untabbed("stub:untabbed1") + " (exempt: planted reason)"],
      result.Exempted
    );
  }

  // Fails when an exemption that took no finding goes unreported, one of a rule that did not run
  // is reported by a run that is not Verify, or Verify stops reporting it.
  [Fact]
  public void An_exemption_that_matches_no_finding_is_reported() {
    ExlibChecks.Exempt("stub", Rule, "stub:untabbed1", "planted reason");
    ExlibChecks.Exempt("stub", Rule, "stub:tabbed", "planted unused");
    ExlibChecks.Exempt(
      "stub",
      "NoSuchRule",
      "stub:any",
      "planted unknown rule"
    );

    Assert.Equal(
      [$"{Rule} exemption of stub:tabbed matches no finding (planted unused)"],
      Of(ExlibChecks.LoadedFor(Game(), "stub"), "Exempt").Errors
    );
    Assert.Equal(
      [
        $"{Rule} exemption of stub:tabbed matches no finding (planted unused)",
        "NoSuchRule exemption of stub:any matches no finding (planted unknown rule)",
      ],
      Of(ExlibChecks.Verify(Game(), "stub"), "Exempt").Errors
    );
  }

  // Fails when Exempt keeps an exemption missing a part, or keeps a repeated one twice.
  [Fact]
  public void Exempt_names_all_four_and_keeps_a_repeat_once() {
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Exempt("stub", Rule, "", "planted reason")
    );
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Exempt("stub", Rule, "stub:tabbed", null!)
    );
    ExlibChecks.Exempt("stub", Rule, "stub:tabbed", "planted unused");
    ExlibChecks.Exempt("stub", Rule, "stub:tabbed", "planted unused");

    Assert.Single(Of(ExlibChecks.LoadedFor(Game(), "stub"), "Exempt").Errors);
  }
}
