using System.Collections.Generic;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="GameReferencesCheck"/> over a planted domain's recipe and construction
/// stages naming <c>game:</c> codes the stub game does and does not load.</summary>
public class GameReferencesCheckTests {
  private readonly ITestOutputHelper output;

  public GameReferencesCheckTests(ITestOutputHelper output) {
    this.output = output;
    TestModDomain.Register();
  }

  private const string File = "stub:recipes/grid/planted.json";

  private static RecipeStubSource Source() =>
    new RecipeStubSource()
      .Recipe(
        File,
        """
        {
          "ingredientPattern": "ABCD", "width": 4, "height": 1,
          "ingredients": {
            "A": { "type": "item", "code": "game:stick-missing" },
            "B": { "type": "item", "code": "game:plank-*" },
            "C": { "type": "item", "code": "game:log-oak" },
            "D": { "type": "item", "code": "stub:own" }
          },
          "output": { "type": "block", "code": "stub:rack" }
        }
        """
      )
      .Definition(
        ExBlockDef
          .Create("stub", "rig")
          .EntityBehavior(
            "ExRightClickConstructable",
            new JObject {
              ["stages"] = JArray.Parse(
                """
                [ { "requireStacks": [
                  { "type": "item", "code": "nails" },
                  { "type": "item", "code": "bogus" }
                ] } ]
                """
              ),
            }
          )
      );

  // Fails when Run stops reporting an unloaded game code, stops resolving a wildcard or an exact
  // code, stops telling blocks from items, reads a bare stage code outside game, or checks the
  // domain's own codes.
  [Fact]
  [PlantedDefect(typeof(GameReferencesCheck), nameof(GameReferencesCheck.Run))]
  public void A_game_code_nothing_loaded_names_is_reported() {
    LoadedStubGame game = new LoadedStubGame(Source())
      .Item("game:plank-oak")
      .Block("game:log-oak")
      .Item("game:nails");

    IReadOnlyList<string> found = GameReferencesCheck.Run(game, "stub").Errors;
    foreach (string line in found)
      output.WriteLine(line);

    Assert.Equal(
      [
        $"{File}: game:stick-missing (item, RecipeIngredient): names nothing the game loaded",
        $"{File}: game:log-oak (item, RecipeIngredient): names nothing the game loaded",
        "stub:blocktypes/rig.json: game:bogus (item, ConstructionRequire): names nothing the game "
          + "loaded",
      ],
      found
    );
  }
}
