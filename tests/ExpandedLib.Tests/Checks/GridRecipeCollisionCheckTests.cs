using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="GridRecipeCollisionCheck"/> over planted pairs of grid recipes that the
/// game's matcher does and does not match on one input.</summary>
public class GridRecipeCollisionCheckTests {
  private readonly ITestOutputHelper output;

  public GridRecipeCollisionCheckTests(ITestOutputHelper output) {
    this.output = output;
    TestModDomain.Register();
  }

  private const string File = "stub:recipes/grid/planted.json";

  /// <summary>A grid recipe named <paramref name="name"/> of the given pattern and size, with
  /// <paramref name="ingredients"/> as its JSON ingredients object and <paramref name="extra"/>
  /// as further top-level properties.</summary>
  private static string Recipe(
    string name,
    string pattern,
    int width,
    int height,
    string ingredients,
    string extra = ""
  ) =>
    $$"""
      {
        "name": "{{name}}", "ingredientPattern": "{{pattern}}",
        "width": {{width}}, "height": {{height}}, {{extra}}
        "ingredients": {{ingredients}},
        "output": { "type": "item", "code": "stub:{{name}}" }
      }
      """;

  private const string Oak = """{ "type": "item", "code": "game:plank-oak" }""";
  private const string Pine =
    """{ "type": "item", "code": "game:plank-pine" }""";

  private IReadOnlyList<string> Findings(RecipeStubSource source) {
    IReadOnlyList<string> found = GridRecipeCollisionCheck
      .Run(source, "stub")
      .Errors;
    foreach (string line in found)
      output.WriteLine(line);
    return found;
  }

  private IReadOnlyList<string> Findings(params string[] recipes) =>
    Findings(
      recipes.Aggregate(new RecipeStubSource(), (s, r) => s.Recipe(File, r))
    );

  private static string Collision(int at, string a, int otherAt, string b) =>
    $"{File}#{at} ({a}) and {File}#{otherAt} ({b}) match the same input";

  #region Shaped

  // Fails when Run stops comparing two recipes of one domain, or reports a pair twice.
  [Fact]
  [PlantedDefect(
    typeof(GridRecipeCollisionCheck),
    nameof(GridRecipeCollisionCheck.Run)
  )]
  public void Two_recipes_of_the_same_input_are_reported() =>
    Assert.Equal(
      [Collision(0, "one", 1, "two")],
      Findings(
        Recipe("one", "PP", 2, 1, $$"""{ "P": {{Oak}} }"""),
        Recipe("two", "OO", 2, 1, $$"""{ "O": {{Oak}} }""")
      )
    );

  // Fails when a wildcard stops overlapping a code it matches.
  [Fact]
  [PlantedDefect(
    typeof(GridRecipeCollisionCheck),
    nameof(GridRecipeCollisionCheck.Run)
  )]
  public void A_wildcard_over_a_code_is_reported() =>
    Assert.Equal(
      [Collision(0, "any", 1, "oak")],
      Findings(
        Recipe(
          "any",
          "P,P",
          1,
          2,
          """{ "P": { "type": "item", "code": "game:plank-*" } }"""
        ),
        Recipe("oak", "O,O", 1, 2, $$"""{ "O": {{Oak}} }""")
      )
    );

  // Fails when allowedVariants stop narrowing an unnamed wildcard, ingredients of another item
  // class overlap, or an ingredient with no type stops being a block.
  [Fact]
  public void Allowed_variants_and_the_item_class_tell_recipes_apart() =>
    Assert.Equal(
      [Collision(2, "block", 3, "untyped")],
      Findings(
        Recipe(
          "narrowed",
          "PP",
          2,
          1,
          """
          { "P": { "type": "item", "code": "game:plank-*", "allowedVariants": ["pine", "birch"] } }
          """
        ),
        Recipe("oak", "OO", 2, 1, $$"""{ "O": {{Oak}} }"""),
        Recipe(
          "block",
          "BB",
          2,
          1,
          """{ "B": { "type": "Block", "code": "game:plank-oak" } }"""
        ),
        Recipe(
          "untyped",
          "BB",
          2,
          1,
          """{ "B": { "code": "game:plank-oak" } }"""
        )
      )
    );

  // Fails when an exact code reads its allowedVariants.
  [Fact]
  public void An_exact_code_ignores_its_allowed_variants() =>
    Assert.Equal(
      [Collision(0, "exact", 1, "oak")],
      Findings(
        Recipe(
          "exact",
          "P",
          1,
          1,
          """{ "P": { "type": "item", "code": "game:plank-oak", "allowedVariants": ["pine"] } }"""
        ),
        Recipe("oak", "O", 1, 1, $$"""{ "O": {{Oak}} }""")
      )
    );

  // Fails when a named wildcard stops taking one state in every slot it fills, or a name with no
  // allowedVariants is bound.
  [Fact]
  public void A_named_wildcard_takes_one_state_in_every_slot() =>
    Assert.Equal(
      [
        Collision(0, "named", 2, "oaks"),
        Collision(0, "named", 3, "open"),
        Collision(1, "mixed", 3, "open"),
        Collision(2, "oaks", 3, "open"),
      ],
      Findings(
        Recipe(
          "named",
          "PP",
          2,
          1,
          """
          {
            "P": {
              "type": "item", "code": "game:plank-*", "name": "wood",
              "allowedVariants": ["oak", "pine"]
            }
          }
          """
        ),
        Recipe("mixed", "OQ", 2, 1, $$"""{ "O": {{Oak}}, "Q": {{Pine}} }"""),
        Recipe("oaks", "OO", 2, 1, $$"""{ "O": {{Oak}} }"""),
        Recipe(
          "open",
          "PP",
          2,
          1,
          """{ "P": { "type": "item", "code": "game:plank-*", "name": "wood" } }"""
        )
      )
    );

  // Fails when a name two slots share takes the first slot's states, stays bound when the last
  // slot has no allowedVariants, or, unbound, is narrowed by a slot's own allowedVariants.
  [Fact]
  public void A_shared_name_takes_the_last_slot_states() =>
    Assert.Equal(
      [
        Collision(0, "last", 1, "open"),
        Collision(0, "last", 2, "pine"),
        Collision(1, "open", 2, "pine"),
      ],
      Findings(
        Recipe(
          "last",
          "PL",
          2,
          1,
          """
          {
            "P": {
              "type": "item", "code": "game:plank-*", "name": "wood", "allowedVariants": ["oak"]
            },
            "L": {
              "type": "item", "code": "game:log-*", "name": "wood", "allowedVariants": ["pine"]
            }
          }
          """
        ),
        Recipe(
          "open",
          "PL",
          2,
          1,
          """
          {
            "P": {
              "type": "item", "code": "game:plank-*", "name": "wood", "allowedVariants": ["oak"]
            },
            "L": { "type": "item", "code": "game:log-*", "name": "wood" }
          }
          """
        ),
        Recipe(
          "pine",
          "PL",
          2,
          1,
          """
          {
            "P": { "type": "item", "code": "game:plank-pine" },
            "L": { "type": "item", "code": "game:log-pine" }
          }
          """
        )
      )
    );

  // Fails when the names of a recipe with more than 256 combinations stay bound.
  [Fact]
  public void Names_past_the_combination_limit_are_left_unbound() {
    string states = string.Join(
      ", ",
      Enumerable
        .Range(0, 255)
        .Select(i => $"\"w{i}\"")
        .Append("\"oak\"")
        .Append("\"pine\"")
    );
    Assert.Equal(
      [Collision(0, "named", 1, "mixed")],
      Findings(
        Recipe(
          "named",
          "PP",
          2,
          1,
          $$"""
          {
            "P": {
              "type": "item", "code": "game:plank-*", "name": "wood",
              "allowedVariants": [{{states}}]
            }
          }
          """
        ),
        Recipe("mixed", "OQ", 2, 1, $$"""{ "O": {{Oak}}, "Q": {{Pine}} }""")
      )
    );
  }

  // Fails when Run compares untrimmed patterns, lets a box sit where its recipe's grid cannot
  // lay it, compares boxes of another shape, or lets an empty slot meet a filled one.
  [Fact]
  public void Trimmed_patterns_collide_where_they_can_sit_at_one_offset() =>
    Assert.Equal(
      [Collision(0, "padded", 1, "column"), Collision(7, "gap", 8, "spaced")],
      Findings(
        Recipe("padded", "_P_,_P_,___", 3, 3, $$"""{ "P": {{Oak}} }"""),
        Recipe("column", "P,P", 1, 2, $$"""{ "P": {{Oak}} }"""),
        Recipe("row", "PP", 2, 1, $$"""{ "P": {{Oak}} }"""),
        Recipe("left", "P__", 3, 1, $$"""{ "P": {{Pine}} }"""),
        Recipe("right", "__P", 3, 1, $$"""{ "P": {{Pine}} }"""),
        Recipe("top", "P,_,_", 1, 3, $$"""{ "P": {{Oak}} }"""),
        Recipe("bottom", "_,_,P", 1, 3, $$"""{ "P": {{Oak}} }"""),
        Recipe("gap", "Q_Q", 3, 1, $$"""{ "Q": {{Pine}} }"""),
        Recipe("spaced", "Q Q", 3, 1, $$"""{ "Q": {{Pine}} }"""),
        Recipe("full", "QQQ", 3, 1, $$"""{ "Q": {{Pine}} }""")
      )
    );

  #endregion

  #region Shapeless

  // Fails when a shapeless recipe is matched by position or with too few slots, keeps a slot's
  // first pairing when another slot needs it, or lets one item fill two of its stacks.
  [Fact]
  [PlantedDefect(
    typeof(GridRecipeCollisionCheck),
    nameof(GridRecipeCollisionCheck.Run)
  )]
  public void A_shapeless_recipe_collides_with_its_ingredients_in_any_slots() =>
    Assert.Equal(
      [
        Collision(1, "loose", 2, "column"),
        Collision(1, "loose", 4, "wild"),
        Collision(1, "loose", 5, "flipped"),
        Collision(2, "column", 4, "wild"),
        Collision(4, "wild", 5, "flipped"),
      ],
      Findings(
        Recipe("single", "O", 1, 1, $$"""{ "O": {{Oak}} }"""),
        Recipe(
          "loose",
          "OQ",
          2,
          1,
          $$"""{ "O": {{Oak}}, "Q": {{Pine}} }""",
          "\"shapeless\": true,"
        ),
        Recipe("column", "Q,O", 1, 2, $$"""{ "O": {{Oak}}, "Q": {{Pine}} }"""),
        Recipe("row", "OO", 2, 1, $$"""{ "O": {{Oak}} }"""),
        Recipe(
          "wild",
          "WO",
          2,
          1,
          $$"""{ "W": { "type": "item", "code": "game:plank-*" }, "O": {{Oak}} }""",
          "\"shapeless\": true,"
        ),
        Recipe("flipped", "O,Q", 1, 2, $$"""{ "O": {{Oak}}, "Q": {{Pine}} }""")
      )
    );

  // Fails when a shapeless recipe stops merging its exact ingredients or the input into one stack
  // per item, or reads each slot of a shaped recipe as an item of its own.
  [Fact]
  public void A_shapeless_recipe_takes_its_input_merged_into_stacks() =>
    Assert.Equal(
      [Collision(0, "stacked", 1, "loose"), Collision(2, "single", 3, "pair")],
      Findings(
        Recipe("stacked", "OOQ", 3, 1, $$"""{ "O": {{Oak}}, "Q": {{Pine}} }"""),
        Recipe(
          "loose",
          "OQ",
          2,
          1,
          $$"""{ "O": {{Oak}}, "Q": {{Pine}} }""",
          "\"shapeless\": true,"
        ),
        Recipe("single", "O", 1, 1, $$"""{ "O": {{Oak}} }"""),
        Recipe(
          "pair",
          "OP",
          2,
          1,
          $$"""{ "O": {{Oak}}, "P": {{Oak}} }""",
          "\"shapeless\": true,"
        )
      )
    );

  // Fails when a shapeless pair whose search passes the placement limit stops being reported.
  [Fact]
  public void A_shapeless_search_past_its_limit_is_reported() {
    string planks = string.Join(
      ", ",
      "abcdefg".Select(c =>
        $$"""
          "{{c}}": { "type": "item", "code": "game:plank-{{c}}" }
          """
      )
    );
    Assert.Equal(
      [Collision(0, "seven", 1, "eight")],
      Findings(
        Recipe(
          "seven",
          "abc,def,g__",
          3,
          3,
          $"{{ {planks} }}",
          "\"shapeless\": true,"
        ),
        Recipe(
          "eight",
          "PPP,PPP,PL_",
          3,
          3,
          """
          {
            "P": { "type": "item", "code": "game:plank-*" },
            "L": { "type": "item", "code": "game:log-oak" }
          }
          """
        )
      )
    );
  }

  #endregion

  #region Ingredients and skipped recipes

  // Fails when a tag-only, regex, advanced or any-domain ingredient stops overlapping what it
  // could match.
  [Fact]
  public void A_tag_only_a_regex_an_advanced_and_an_any_domain_ingredient_overlap() =>
    Assert.Equal(
      [
        Collision(0, "tagged", 1, "regex"),
        Collision(0, "tagged", 2, "anydomain"),
        Collision(0, "tagged", 3, "advanced"),
        Collision(0, "tagged", 4, "oak"),
        Collision(1, "regex", 2, "anydomain"),
        Collision(1, "regex", 3, "advanced"),
        Collision(1, "regex", 4, "oak"),
        Collision(2, "anydomain", 3, "advanced"),
        Collision(2, "anydomain", 4, "oak"),
        Collision(3, "advanced", 4, "oak"),
      ],
      Findings(
        Recipe(
          "tagged",
          "T",
          1,
          1,
          """{ "T": { "type": "item", "tags": ["fettlestock"] } }"""
        ),
        Recipe(
          "regex",
          "R",
          1,
          1,
          """{ "R": { "type": "item", "code": "@game:plank-(birch|pine)" } }"""
        ),
        Recipe(
          "anydomain",
          "A",
          1,
          1,
          """{ "A": { "type": "item", "code": "*:plank-oak" } }"""
        ),
        Recipe(
          "advanced",
          "V",
          1,
          1,
          """{ "V": { "type": "item", "code": "game:plank-{wood}" } }"""
        ),
        Recipe("oak", "O", 1, 1, $$"""{ "O": {{Oak}} }""")
      )
    );

  // Fails when Run reads a disabled recipe, a recipe with a keyless letter, one larger than 3x3,
  // or one whose pattern does not fill its grid.
  [Fact]
  public void A_recipe_the_game_refuses_or_never_matches_is_skipped() =>
    Assert.Empty(
      Findings(
        Recipe("kept", "P", 1, 1, $$"""{ "P": {{Oak}} }"""),
        Recipe(
          "disabled",
          "P",
          1,
          1,
          $$"""{ "P": {{Oak}} }""",
          "\"enabled\": false,"
        ),
        Recipe("keyless", "PQ", 2, 1, $$"""{ "P": {{Oak}} }"""),
        Recipe(
          "wide",
          "P___",
          4,
          1,
          $$"""{ "P": {{Oak}} }""",
          "\"shapeless\": true,"
        ),
        Recipe("unfilled", "P", 2, 1, $$"""{ "P": {{Oak}} }""")
      )
    );

  // Fails when Run stops holding a domain's recipes against the other domains of the source.
  [Fact]
  public void A_recipe_of_another_family_domain_is_held_against_the_domain() =>
    Assert.Equal(
      [
        $"{File}#0 (oak) and other:recipes/grid/planted.json#0 (again) match the same input",
      ],
      Findings(
        new RecipeStubSource()
          .Recipe(File, Recipe("oak", "P", 1, 1, $$"""{ "P": {{Oak}} }"""))
          .Recipe(
            "other:recipes/grid/planted.json",
            Recipe("again", "P", 1, 1, $$"""{ "P": {{Oak}} }""")
          )
      )
    );

  // Fails when an ingredient code without a domain is read as game's, not its file's.
  [Fact]
  public void A_code_without_a_domain_is_in_its_file_domain() =>
    Assert.Equal(
      [
        $"{File}#1 (cog) and other:recipes/grid/planted.json#1 (named) match the same input",
      ],
      Findings(
        new RecipeStubSource()
          .Recipe(
            File,
            Recipe(
              "gear",
              "G",
              1,
              1,
              """{ "G": { "type": "item", "code": "gear" } }"""
            )
          )
          .Recipe(
            File,
            Recipe(
              "cog",
              "C",
              1,
              1,
              """{ "C": { "type": "item", "code": "cog" } }"""
            )
          )
          .Recipe(
            File,
            Recipe(
              "vanilla",
              "S",
              1,
              1,
              """{ "S": { "type": "item", "code": "game:gear" } }"""
            )
          )
          .Recipe(
            "other:recipes/grid/planted.json",
            Recipe(
              "bare",
              "G",
              1,
              1,
              """{ "G": { "type": "item", "code": "gear" } }"""
            )
          )
          .Recipe(
            "other:recipes/grid/planted.json",
            Recipe(
              "named",
              "C",
              1,
              1,
              """{ "C": { "type": "item", "code": "stub:cog" } }"""
            )
          )
      )
    );

  // Fails when PatternsMeet treats a star as a literal or matches two distinct literals.
  [Theory]
  [InlineData("metal-*-iron", "metal-plate-*", true)]
  [InlineData("plank-*", "plank-oak", true)]
  [InlineData("*", "", true)]
  [InlineData("a*b", "a*c", false)]
  [InlineData("plank-*", "log-*", false)]
  public void Two_patterns_meet_when_some_code_matches_both(
    string a,
    string b,
    bool meet
  ) {
    Assert.Equal(meet, GridRecipeCollisionCheck.PatternsMeet(a, b));
    Assert.Equal(meet, GridRecipeCollisionCheck.PatternsMeet(b, a));
  }

  #endregion
}
