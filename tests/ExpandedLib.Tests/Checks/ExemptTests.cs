using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
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

  // Fails when an exemption stops taking its finding, takes one where the code runs on into or
  // follows another word, or applies in another domain's run.
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

  // Fails when Exempt keeps an exemption missing a part, naming no code or an empty one, or keeps
  // a repeated one twice.
  [Fact]
  public void Exempt_names_all_four_and_keeps_a_repeat_once() {
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Exempt("stub", Rule, "", "planted reason")
    );
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Exempt("stub", Rule, "stub:tabbed", null!)
    );
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Exempt("stub", Rule, Array.Empty<string>(), "planted reason")
    );
    Assert.Throws<ArgumentException>(() =>
      ExlibChecks.Exempt("stub", Rule, ["stub:tabbed", ""], "planted reason")
    );
    ExlibChecks.Exempt("stub", Rule, "stub:tabbed", "planted unused");
    ExlibChecks.Exempt("stub", Rule, "stub:tabbed", "planted unused");

    Assert.Single(Of(ExlibChecks.LoadedFor(Game(), "stub"), "Exempt").Errors);
  }

  private const string File = "stub:recipes/grid/planted.json";

  private static string Recipe(
    string name,
    string pattern,
    string ingredients
  ) =>
    $$"""
      {
        "name": "{{name}}", "ingredientPattern": "{{pattern}}", "width": 2, "height": 1,
        "ingredients": {{ingredients}},
        "output": { "type": "item", "code": "stub:{{name}}" }
      }
      """;

  private const string Oak = """{ "type": "item", "code": "game:plank-oak" }""";

  private static string Collision(int at, string a, int otherAt, string b) =>
    $"{File}#{at} ({a}) and {File}#{otherAt} ({b}) match the same input";

  // Fails when an exemption naming one recipe of a colliding pair takes the pair, or one naming
  // both stops taking it, or a collision stops carrying its two recipes as subjects.
  [Fact]
  public void A_collision_is_taken_only_by_an_exemption_naming_both_recipes() {
    ExlibChecks.Exempt(
      "stub",
      "GridRecipeCollision",
      $"{File}#0",
      "planted one side"
    );
    ExlibChecks.Exempt(
      "stub",
      "GridRecipeCollision",
      [$"{File}#1", $"{File}#2"],
      "planted pair"
    );
    IReadOnlyList<CheckResult> results = ExlibChecks.For(
      new RecipeStubSource()
        .Recipe(File, Recipe("one", "PP", $$"""{ "P": {{Oak}} }"""))
        .Recipe(File, Recipe("two", "OO", $$"""{ "O": {{Oak}} }"""))
        .Recipe(File, Recipe("three", "WW", $$"""{ "W": {{Oak}} }""")),
      "stub"
    );

    CheckResult result = Of(results, "GridRecipeCollision");
    Assert.Equal(
      [Collision(0, "one", 1, "two"), Collision(0, "one", 2, "three")],
      result.Errors
    );
    Assert.Equal(
      [Collision(1, "two", 2, "three") + " (exempt: planted pair)"],
      result.Exempted
    );
    Assert.Contains(
      $"GridRecipeCollision exemption of {File}#0 matches no finding (planted one side)",
      Of(results, "Exempt").Errors
    );
  }

  // Fails when a prefix clash stops carrying its two codes as subjects, so an exemption naming
  // the shorter code alone takes it.
  [Fact]
  public void A_prefix_clash_is_taken_only_by_an_exemption_naming_both_codes() {
    ExlibChecks.Exempt(
      "stub",
      "CodePrefixCollision",
      "stub:slag",
      "planted one code"
    );
    RecipeStubSource source = new RecipeStubSource()
      .Definition(ExBlockDef.Create("stub", "slag"))
      .Definition(ExBlockDef.Create("stub", "slag-block"));

    Assert.Single(
      Of(ExlibChecks.For(source, "stub"), "CodePrefixCollision").Errors
    );

    ExlibChecks.Exempt(
      "stub",
      "CodePrefixCollision",
      ["stub:slag", "stub:slag-block"],
      "planted both codes"
    );
    Assert.Empty(
      Of(ExlibChecks.For(source, "stub"), "CodePrefixCollision").Errors
    );
  }

  // Fails when an exemption naming a recipe and the words of one defect takes another defect of
  // that recipe.
  [Fact]
  public void An_exemption_naming_a_defect_leaves_another_defect_of_its_recipe() {
    ExlibChecks.Exempt(
      "stub",
      "GridRecipeShape",
      [$"{File}#0", "key G"],
      "planted key G"
    );

    CheckResult result = Of(
      ExlibChecks.For(
        new RecipeStubSource().Recipe(
          File,
          Recipe("one", "PB", $$"""{ "P": {{Oak}}, "G": {{Oak}} }""")
        ),
        "stub"
      ),
      "GridRecipeShape"
    );

    Assert.Equal([$"{File}#0 (one): letter B has no key"], result.Errors);
    Assert.Equal(
      [
        $"{File}#0 (one): key G is not in the pattern PB (exempt: planted key G)",
      ],
      result.Exempted
    );
  }

  // Fails when a second exemption matching only a finding the first took is reported as unused,
  // or not reported.
  [Fact]
  public void A_second_exemption_of_the_same_finding_is_reported_as_its_duplicate() {
    ExlibChecks.Exempt("stub", Rule, "stub:untabbed1", "planted first");
    ExlibChecks.Exempt(
      "stub",
      Rule,
      ["stub:untabbed1", "CreativeInventoryTabs"],
      "planted second"
    );

    Assert.Equal(
      [
        $"{Rule} exemption of stub:untabbed1 + CreativeInventoryTabs duplicates the "
          + $"{Rule} exemption of stub:untabbed1 (planted second)",
      ],
      Of(ExlibChecks.LoadedFor(Game(), "stub"), "Exempt").Errors
    );
  }

  // Fails when ':' starts a word, stops ending one before white space, ends one before a letter,
  // or '.' stops ending a word before white space or the end.
  [Theory]
  [InlineData("stub:slag-block (block)", "slag-block", false)]
  [InlineData("stub:untabbed1: tabs are null", "stub:untabbed1", true)]
  [InlineData("iiex:slag-block (block)", "iiex", false)]
  [InlineData("names stub:gear.", "stub:gear", true)]
  [InlineData("names stub:gear. Then more", "stub:gear", true)]
  [InlineData("stub:gear.json#1", "stub:gear", false)]
  public void A_code_is_named_only_as_a_whole_word(
    string text,
    string code,
    bool named
  ) => Assert.Equal(named, ExlibChecks.Names(text, code));
}
