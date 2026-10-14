#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Setups;

internal sealed class SetupFrame : BlockEntity
{
  private ExpandedLib.Blocks.ExRightClickConstructable? _rcc;
}

/// <summary>What a <c>complete</c> draws from a setup's pack: the cells it raises and every stage
/// the construction still needs (setups.md, The pack).</summary>
[Collection("Setups")]
public class SetupPackCompleteTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private const string Frame =
    "{\"place\": {\"id\": \"frame\", \"code\": \"setupbuild:frame\", \"at\": [0, 0, 0], \"complete\": \"all\"}}";
  private const string Stack =
    "{\"place\": {\"id\": \"stack\", \"code\": \"smokestack:smokestack-intake-tier3-n\", \"at\": [1, 1, 0], \"complete\": \"all\"}}";

  private static readonly (string, string)[] Mods =
  [
    ("exlib", "0.8.4"),
    ("setupbuild", "1.0.0"),
    ("smokestack", "1.0.0"),
  ];

  private static string ModPath(string id) =>
    id == "smokestack"
      ? Path.Combine(RepoPaths.Root, "samples", "SmokeStack")
      : Path.Combine(
        RepoPaths.Root,
        "tests",
        "ExpandedLib.Tests",
        "Setups",
        "Fixtures",
        "SetupBuild"
      );

  private static SetupScene Build(string mod, string pack, string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\""
          + mod
          + "\"], \"steps\": ["
          + steps
          + "], \"seconds\": 10, \"record\": {}, \"pack\": "
          + pack
          + "}",
        "t.json"
      ),
      [ModPath(mod)],
      null,
      w =>
        w.RegisterClass(
            "ExRightClickConstructable",
            typeof(ExpandedLib.Blocks.ExRightClickConstructable)
          )
          .RegisterClass("SetupFrame", typeof(SetupFrame))
    );

  private static string Refused(string mod, string pack, string steps) =>
    Assert
      .Throws<InvalidOperationException>(() => Build(mod, pack, steps))
      .Message;

  private static string Gathered(string scope, string gathered) =>
    "{\"scope\": \"" + scope + "\", \"gathered\": " + gathered + "}";

  private const string Paid =
    "{\"setupbuild:frame\": 1, \"game:stick\": 2, \"game:plank-oak\": 3}";

  // Fails when complete takes no stage requirement from the pack, so a construction short of its
  // planks completes.
  [Fact]
  public void A_construction_short_of_a_stage_requirement_fails_at_complete()
  {
    string message = Refused(
      "setupbuild",
      Gathered(
        "all",
        "{\"setupbuild:frame\": 1, \"game:stick\": 2, \"game:plank-oak\": 2}"
      ),
      Frame
    );

    Assert.Equal(
      "t.json: steps[0]: the pack holds 2 of game:plank-*; the step takes 3.",
      message
    );
  }

  // Fails when a stage's requirement is drawn once however many the stage needs, or when a
  // placeholder in a code is drawn as written.
  [Fact]
  public void A_paid_construction_draws_every_stage_requirement_in_stage_order()
  {
    using SetupScene scene = Build("setupbuild", Gathered("all", Paid), Frame);

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal(
      [
        ("steps[0]", "setupbuild:frame", 1),
        ("steps[0]", "game:stick", 2),
        ("steps[0]", "game:plank-*", 3),
      ],
      pack.Draws
    );
    Assert.Empty(pack.End);
  }

  // Fails when complete draws under scope "stacks", where a construction is built free.
  [Fact]
  public void A_construction_under_scope_stacks_draws_nothing()
  {
    using SetupScene scene = Build(
      "setupbuild",
      Gathered("stacks", "{}"),
      Frame
    );

    Assert.Empty(scene.Run(Mods).Pack!.Draws);
  }

  // Fails when complete raises a structure's cells without drawing them.
  [Fact]
  public void A_structure_short_of_the_cells_it_raises_fails_at_complete()
  {
    string message = Refused(
      "smokestack",
      Gathered("all", "{\"smokestack:smokestack-intake-tier3-n\": 1}"),
      Stack
    );

    Assert.StartsWith("t.json: steps[0]: the pack holds 0 of ", message);
    Assert.EndsWith("; the step takes 1.", message);
  }

  // Fails when the raised cells are drawn once for all, or under the step's own block.
  [Fact]
  public void A_completed_structure_draws_each_cell_it_raises_after_its_own_block()
  {
    using SetupScene scene = Build(
      "smokestack",
      Gathered("all", "{\"*\": 1000}"),
      Stack
    );

    SetupPackRecord pack = scene.Run(Mods).Pack!;

    Assert.Equal("smokestack:smokestack-intake-tier3-n", pack.Draws[0].Code);
    Assert.True(pack.Draws.Count > 10);
    Assert.All(
      pack.Draws,
      d => Assert.Equal(("steps[0]", 1), (d.Where, d.Count))
    );
    Assert.Equal(1000 - pack.Draws.Count, pack.End.Values.Single());
  }
}
#endif
