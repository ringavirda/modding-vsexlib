#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The craft-sequence catalogue: loading, merging by machine and the grid gate that turns a
/// sequenced recipe away from every grid but its machine's.</summary>
public sealed class CraftSequenceTests
{
  private const string ShaftHand = """
    {
      "schema": 1,
      "machine": "workbench",
      "sequences": [
        {
          "recipe": "iiex:shaft hand",
          "steps": [
            { "tool": "game:chisel-*", "offhand": "game:hammer-*", "seconds": 3, "help": "iiex:craftstep-shaft-hand-1" },
            { "tool": "iiex:file-*", "seconds": 2, "help": "iiex:craftstep-shaft-hand-2" },
            { "tool": null, "seconds": 1, "animation": "fitting", "at": "vice" },
            { "tool": "game:chisel-*", "offhand": "game:hammer-*", "seconds": 3 }
          ],
          "byproducts": [ { "type": "item", "code": "iiex:metalbit-castiron", "quantity": 1 } ]
        }
      ]
    }
    """;

  private static List<CraftSequenceSet> Parse(
    out List<string> errors,
    string json
  ) =>
    CraftSequenceLoader.Parse(
      [("iiex:config/craftsequences/workbench.json", json)],
      out errors
    );

  // Fails when the loader keeps one step or drops the off-hand tool.
  [Fact]
  public void A_four_step_sequence_with_an_offhand_tool_loads_whole()
  {
    List<CraftSequenceSet> sets = Parse(out List<string> errors, ShaftHand);

    Assert.Empty(errors);
    CraftSequence sequence = Assert.Single(Assert.Single(sets).Sequences);
    Assert.Equal(
      ["game:chisel-*", "iiex:file-*", null, "game:chisel-*"],
      sequence.Steps.Select(s => s.Tool)
    );
    Assert.Equal(
      ["game:hammer-*", null, null, "game:hammer-*"],
      sequence.Steps.Select(s => s.Offhand)
    );
    Assert.Equal([3f, 2f, 1f, 3f], sequence.Steps.Select(s => s.Seconds));
    Assert.Equal(CraftStep.Vice, sequence.Steps[2].At);
    Assert.Equal("fitting", sequence.Steps[2].Animation);
    Assert.Equal("iiex:craftstep-shaft-hand-1", sequence.Steps[0].Help);
    CraftByproduct byproduct = Assert.Single(sequence.Byproducts);
    Assert.Equal("iiex:metalbit-castiron", byproduct.Code);
  }

  // Fails when the loader drops a step's sound or a file's tool defaults.
  [Fact]
  public void A_step_sound_and_the_tool_defaults_load()
  {
    List<CraftSequenceSet> sets = Parse(
      out List<string> errors,
      """
      {
        "machine": "workbench",
        "tools": [ { "tool": "iiex:file-*", "animation": "filing", "sound": "sounds/effect/file", "stroke": 1.5, "strike": 0.5 } ],
        "sequences": [ { "recipe": "iiex:r", "steps": [ { "tool": "game:hammer-*", "sound": "sounds/effect/rivet" } ] } ]
      }
      """
    );

    Assert.Empty(errors);
    CraftSequenceSet set = Assert.Single(sets);
    Assert.Equal("sounds/effect/rivet", set.Sequences[0].Steps[0].Sound);
    Assert.Equal(
      new CraftToolDefault(
        "iiex:file-*",
        "filing",
        "sounds/effect/file",
        1.5f,
        0.5f
      ),
      Assert.Single(set.Tools!)
    );
  }

  // Fails when the loader accepts a tool default that names nothing to play or strikes outside its stroke.
  [Theory]
  [InlineData("""{ "tool": "x" }""", "no animation or sound")]
  [InlineData("""{ "animation": "a" }""", "no 'tool'")]
  [InlineData(
    """{ "tool": "x", "animation": "a", "stroke": 1, "strike": 1 }""",
    "strikes at"
  )]
  [InlineData("""{ "tool": "x", "animation": "a", "sped": 1 }""", "sped")]
  public void A_malformed_tool_default_is_reported_and_skipped(
    string tool,
    string message
  )
  {
    List<CraftSequenceSet> sets = Parse(
      out List<string> errors,
      $$"""{ "machine": "workbench", "tools": [ {{tool}} ], "sequences": [] }"""
    );

    Assert.Empty(sets);
    Assert.Contains(message, Assert.Single(errors));
  }

  // Fails when a declared default does not replace the hammer's or the latest pattern does not win,
  // or Clear drops the built-in hammer.
  [Fact]
  public void The_registry_starts_with_the_hammer_and_a_declaration_overrides_it()
  {
    var registry = new CraftSequenceRegistry();
    Assert.Equal(
      "hammerhit",
      registry.ToolDefault("game:hammer-steel")?.Animation
    );
    Assert.Null(registry.ToolDefault("game:saw-iron"));
    Assert.Null(registry.ToolDefault(null));

    registry.DefineTool(new CraftToolDefault("game:hammer-*", "slam", null));
    Assert.Equal("slam", registry.ToolDefault("game:hammer-steel")?.Animation);
    Assert.Single(registry.Tools);

    registry.DefineTool(new CraftToolDefault("game:*", "swing", null));
    Assert.Equal("swing", registry.ToolDefault("game:hammer-steel")?.Animation);

    registry.Clear();
    Assert.Equal(
      "hammerhit",
      registry.ToolDefault("game:hammer-steel")?.Animation
    );
  }

  // Fails when a tool default of stroke 0 or less is accepted, which hangs the run at its first hold.
  [Theory]
  [InlineData(0f)]
  [InlineData(-1f)]
  public void A_tool_default_with_no_stroke_is_refused(float stroke)
  {
    var tools = new CraftSequenceRegistry();

    Assert.Throws<ArgumentOutOfRangeException>(() =>
      tools.DefineTool(new CraftToolDefault("x", "a", null, stroke, 0f))
    );
  }

  // Fails when a tool default that strikes before its stroke starts, or at or after it ends, is accepted.
  [Theory]
  [InlineData(-0.1f)]
  [InlineData(1f)]
  public void A_tool_default_that_strikes_outside_its_stroke_is_refused(
    float strike
  )
  {
    var tools = new CraftSequenceRegistry();

    Assert.Throws<ArgumentOutOfRangeException>(() =>
      tools.DefineTool(new CraftToolDefault("x", "a", null, 1f, strike))
    );
  }

  // Fails when the loader accepts a sequence with no steps.
  [Fact]
  public void A_sequence_with_no_steps_is_reported_and_skipped()
  {
    List<CraftSequenceSet> sets = Parse(
      out List<string> errors,
      """{ "machine": "workbench", "sequences": [ { "recipe": "iiex:r", "steps": [] } ] }"""
    );

    Assert.Empty(sets);
    Assert.Contains("no steps", Assert.Single(errors));
  }

  // Fails when the loader stops auditing a step's keys.
  [Fact]
  public void A_step_naming_an_unknown_key_is_reported_and_skipped()
  {
    List<CraftSequenceSet> sets = Parse(
      out List<string> errors,
      """{ "machine": "workbench", "sequences": [ { "recipe": "iiex:r", "steps": [ { "tol": "x" } ] } ] }"""
    );

    Assert.Empty(sets);
    Assert.Contains("tol", Assert.Single(errors));
  }

  // Fails when the loader reads an unknown step position.
  [Fact]
  public void A_step_at_an_unknown_place_is_reported_and_skipped()
  {
    List<CraftSequenceSet> sets = Parse(
      out List<string> errors,
      """{ "machine": "workbench", "sequences": [ { "recipe": "iiex:r", "steps": [ { "at": "floor" } ] } ] }"""
    );

    Assert.Empty(sets);
    Assert.Contains("floor", Assert.Single(errors));
  }

  // Fails when a later declaration replaces the first or goes unreported.
  [Fact]
  public void A_second_declaration_for_one_recipe_loses_and_is_reported()
  {
    var registry = new CraftSequenceRegistry();

    List<string> errors = CraftSequenceLoader.Load(
      [
        ("iiex:config/craftsequences/workbench.json", ShaftHand),
        (
          "othermod:config/craftsequences/workbench.json",
          """{ "machine": "workbench", "sequences": [ { "recipe": "iiex:shaft hand", "steps": [ { "tool": "othermod:saw-*" } ] } ] }"""
        ),
      ],
      registry
    );

    string clash = Assert.Single(errors);
    Assert.StartsWith("othermod:config/craftsequences/workbench.json:", clash);
    CraftSequence held = registry.Sequence("workbench", "iiex:shaft hand")!;
    Assert.Equal(4, held.Steps.Length);
  }

  // Fails when the registry files a sequence under the wrong machine.
  [Fact]
  public void Another_machine_may_declare_the_same_recipe()
  {
    var registry = new CraftSequenceRegistry();

    List<string> errors = CraftSequenceLoader.Load(
      [
        ("a:config/craftsequences/a.json", ShaftHand),
        (
          "b:config/craftsequences/b.json",
          ShaftHand.Replace("workbench", "lathe")
        ),
      ],
      registry
    );

    Assert.Empty(errors);
    Assert.NotNull(registry.Sequence("lathe", "iiex:shaft hand"));
    Assert.Equal(2, registry.Machines.Count);
  }

  private static GridRecipe Recipe(string name) =>
    new()
    {
      Name = new AssetLocation(name),
      Width = 2,
      Height = 2,
    };

  private static bool Matches(TestWorld world, GridRecipe recipe) =>
    world.Api.Event.TriggerMatchesRecipe(null!, recipe, [], 3);

  // Fails when the handler checks width only: the 2 x 2 sequenced recipe would match unmarked.
  [Fact]
  public void A_sequenced_recipe_is_refused_in_the_hand_grid_and_matched_at_its_machine()
  {
    CraftSequenceRegistry.Shared.Clear();
    try
    {
      CraftSequenceLoader.Load([
        ("iiex:config/craftsequences/w.json", ShaftHand),
      ]);
      using var world = new TestWorld();
      CraftSequenceGate.Register(world.Api.Event);
      GridRecipe sequenced = Recipe("iiex:shaft hand");
      GridRecipe plain = Recipe("iiex:plain");

      Assert.False(Matches(world, sequenced));
      Assert.True(Matches(world, plain));
      using (CraftSequenceGate.At("workbench"))
        Assert.True(Matches(world, sequenced));
      using (CraftSequenceGate.At("lathe"))
        Assert.False(Matches(world, sequenced));
      Assert.False(Matches(world, sequenced));
    }
    finally
    {
      CraftSequenceRegistry.Shared.Clear();
    }
  }

  // Fails when the harness asks every handler for a yes, which the game does not: it answers with the last one
  // subscribed, so a later handler's true overrides the gate.
  [Fact]
  public void The_last_subscribed_handler_answers_the_match()
  {
    CraftSequenceRegistry.Shared.Clear();
    try
    {
      CraftSequenceLoader.Load([
        ("iiex:config/craftsequences/w.json", ShaftHand),
      ]);
      using var world = new TestWorld();
      CraftSequenceGate.Register(world.Api.Event);
      GridRecipe sequenced = Recipe("iiex:shaft hand");

      world.Api.Event.MatchesGridRecipe += (_, _, _, _) => true;

      Assert.True(Matches(world, sequenced));
    }
    finally
    {
      CraftSequenceRegistry.Shared.Clear();
    }
  }
}
#endif
