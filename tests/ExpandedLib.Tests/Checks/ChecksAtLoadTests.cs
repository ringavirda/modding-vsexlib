using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The load-time run of the content checks: the server's at <c>GameReady</c>, after every
/// mod's <c>StartServerSide</c>, and none on a client, which receives no recipes, a server asset
/// category.</summary>
[Collection("WorldState")]
public sealed class ChecksAtLoadTests : System.IDisposable {
  public ChecksAtLoadTests() => ExlibChecks.ClearDeclarations();

  public void Dispose() => ExlibChecks.ClearDeclarations();

  // Fails when AssetsFinalize runs the content checks on a client.
  [Fact]
  public void A_client_load_runs_no_check_and_logs_no_unused_exemption() {
    using var world = new TestWorld();
    ((ICoreAPI)world.ClientApi).ModLoader.Returns(world.Mods);
    world.ClientApi.IsSinglePlayer.Returns(true);
    var assets = Substitute.For<IAssetManager>();
    assets
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns(_ => new List<IAsset>());
    ((ICoreAPI)world.ClientApi).Assets.Returns(assets);
    ExlibChecks.Exempt(
      "exlib",
      "GridRecipeCollision",
      "exlib:recipes/grid/planted.json#0",
      "planted recipe the client never receives"
    );
    Assert.Contains(
      ExlibChecks.All(world.ClientApi),
      r =>
        r.Check == "Exempt" && r.Errors.Any(e => e.Contains("planted.json#0"))
    );

    new ExpandedLibModSystem().AssetsFinalize(world.ClientApi);

    Assert.DoesNotContain(
      world.Log.Entries,
      e => e.Message.Contains("[exlib]")
    );
  }

  // Fails when the checks run before GameReady, the held recipes are noted at GameReady or never,
  // or a recipe the game refused at load is left out as a removed one is.
  [Fact]
  public void The_server_reads_a_refused_recipe_and_skips_one_a_later_mod_removed() {
    using var world = new TestWorld();
    world.Mods.Add("laterplanted", "1.0.0", dependencies: "exlib");
    AssetLocation file = new("laterplanted", "recipes/grid/crates.json");
    const string Crates = """
      [
        { "ingredientPattern": "P", "width": 1, "height": 1,
          "ingredients": { "P": { "type": "item", "code": "game:plank" } },
          "output": { "type": "block", "code": "laterplanted:crate" } },
        { "ingredientPattern": "P", "width": 1, "height": 1,
          "ingredients": { "P": { "type": "item", "code": "game:plank" } },
          "output": { "type": "block", "code": "laterplanted:chest" } },
        { "ingredientPattern": "P", "width": 1, "height": 1,
          "ingredients": { "P": { "type": "item", "code": "game:plank" } },
          "output": { "type": "block", "code": "laterplanted:barrel" } },
        { "ingredientPattern": "S", "width": 1, "height": 1,
          "ingredients": { "S": { "type": "item", "code": "game:stick" } },
          "output": { "type": "block", "code": "laterplanted:nosuchblock" } }
      ]
      """;
    world
      .Api.Assets.GetMany("recipes/", "laterplanted")
      .Returns([
        ExSyntheticAsset.Create(
          file,
          Encoding.UTF8.GetBytes(Crates),
          new ExDefinitionOrigin()
        ),
      ]);
    List<GridRecipe> held =
    [
      .. new[] { "crate", "chest", "barrel" }.Select(path => new GridRecipe
      {
        Name = file,
        Output = new CraftingRecipeIngredient
        {
          Type = EnumItemClass.Block,
          Code = new AssetLocation("laterplanted", path),
        },
      }),
    ];
    world.World.GridRecipes.Returns(held);
    int id = 900;
    foreach (GridRecipe recipe in held)
      world.Register(new Block { Code = recipe.Output.Code, BlockId = id++ });
    var phases = new List<(EnumServerRunPhase Phase, System.Action Run)>();
    world
      .Api.Event.When(e =>
        e.ServerRunPhase(
          Arg.Any<EnumServerRunPhase>(),
          Arg.Any<System.Action>()
        )
      )
      .Do(ci =>
        phases.Add((ci.Arg<EnumServerRunPhase>(), ci.Arg<System.Action>()))
      );
    world.Api.ChatCommands.Parsers.Returns(
      new CommandArgumentParsers(world.Api)
    );
    var exlib = new ExpandedLibModSystem();
    ReflectionHelpers.SetProperty(
      exlib,
      nameof(ModSystem.Mod),
      world.Mods.GetMod("exlib")!
    );
    // Apart from the world's log: parallel tests leave process-wide definitions in exlib's domain.
    var logger = new ErrorLines();
    world.Api.Logger.Returns(logger);
    try {
      exlib.AssetsFinalize(world.Api);
      exlib.StartServerSide(world.Api);
      held.RemoveAt(1);
      foreach ((EnumServerRunPhase phase, System.Action run) in phases)
        if (phase == EnumServerRunPhase.GameReady)
          run();
    } finally {
      exlib.Dispose();
    }

    Assert.Equal(
      [
        "laterplanted:recipes/grid/crates.json#0 (laterplanted:crate) and "
          + "laterplanted:recipes/grid/crates.json#2 (laterplanted:barrel) match the same input",
      ],
      logger
        .Lines.Where(e => e.Contains("match the same input"))
        .Select(e => e.Replace("[exlib]", "").Trim())
    );
    Assert.Contains(
      logger.Lines,
      e =>
        e.Contains(
          "laterplanted:recipes/grid/crates.json: laterplanted:nosuchblock"
        )
    );
  }

  // Keeps each Error line and reports nothing to FailOnWarnings.
  private sealed class ErrorLines : LoggerBase {
    internal List<string> Lines { get; } = [];

    protected override void LogImpl(
      EnumLogType logType,
      string format,
      params object[] args
    ) {
      if (logType == EnumLogType.Error)
        Lines.Add(
          args is { Length: > 0 } ? string.Format(format, args) : format
        );
    }
  }
}
