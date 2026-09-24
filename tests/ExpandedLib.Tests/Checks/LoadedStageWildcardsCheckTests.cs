using System.Collections.Generic;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary><see cref="LoadedStageWildcardsCheck"/> over a planted stage table whose wildcards
/// name loaded vanilla codes and the domain's own.</summary>
public class LoadedStageWildcardsCheckTests {
  private readonly ITestOutputHelper output;

  public LoadedStageWildcardsCheckTests(ITestOutputHelper output) {
    this.output = output;
    TestModDomain.Register();
  }

  /// <summary>A block <c>stub:rig</c> whose one stage requires
  /// <paramref name="ingredients"/>, a JSON array.</summary>
  private static RecipeStubSource Rig(string ingredients) =>
    new RecipeStubSource().Definition(
      ExBlockDef
        .Create("stub", "rig")
        .EntityBehavior(
          "ExRightClickConstructable",
          new JObject {
            ["stages"] = new JArray(
              new JObject { ["requireStacks"] = JArray.Parse(ingredients) }
            ),
          }
        )
    );

  // Two logs of different types and woods, and two beams of the domain spanning two groups.
  private static LoadedStubGame Loaded(RecipeStubSource source) =>
    new LoadedStubGame(source)
      .Block(
        "game:log-placed-oak-ud",
        null,
        ("type", "placed"),
        ("wood", "oak"),
        ("rotation", "ud")
      )
      .Block(
        "game:log-grown-pine-ud",
        null,
        ("type", "grown"),
        ("wood", "pine"),
        ("rotation", "ud")
      )
      .Block("game:plank-oak", null, ("wood", "oak"))
      .Block("stub:beam-oak-a", null, ("wood", "oak"), ("size", "a"))
      .Block("stub:beam-pine-b", null, ("wood", "pine"), ("size", "b"));

  // Fails when Run stops deciding a stored vanilla wildcard, or decides a code with no *, an
  // ingredient that stores nothing, or a wildcard of a covered domain.
  [Fact]
  [PlantedDefect(
    typeof(LoadedStageWildcardsCheck),
    nameof(LoadedStageWildcardsCheck.Run)
  )]
  public void A_stored_vanilla_wildcard_spanning_two_groups_is_reported() {
    LoadedStubGame game = Loaded(
      Rig(
        """
        [
          { "type": "block", "code": "game:log-*", "storeWildCard": "wood" },
          { "type": "block", "code": "game:plank-oak", "storeWildCard": "wood" },
          { "type": "block", "code": "game:log-*" },
          { "type": "block", "code": "stub:beam-*", "storeWildCard": "wood" }
        ]
        """
      )
    );

    IReadOnlyList<string> found = LoadedStageWildcardsCheck
      .Run(game, "stub")
      .Errors;
    foreach (string line in found)
      output.WriteLine(line);

    Assert.Equal(
      ["stub:rig stage 0 game:log-*: (b) * spans [type, wood], stores wood"],
      found
    );
  }
}
