#if GAME_GE_1_22
using System;
using System.IO;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The pack a setup file starts from, its draws and gives, the standing blocks it carries and
/// its end check (setups.md, The pack).</summary>
public class SetupPackTests
{
  private static string Fixture(string name) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Setups",
      "Files",
      "pack",
      name + ".json"
    );

  private static SetupFileModel Load(string name)
  {
    string path = Fixture(name);
    return SetupFileModel.Parse(File.ReadAllText(path), path);
  }

  private static SetupFileModel? Resolve(string id)
  {
    string path = Fixture(id.Substring(id.LastIndexOf('/') + 1));
    return File.Exists(path)
      ? SetupFileModel.Parse(File.ReadAllText(path), path)
      : null;
  }

  private static SetupPack Pack(string name) =>
    SetupPack.Start(Load(name), Resolve);

  private static SetupFileModel Inline(string pack) =>
    SetupFileModel.Parse(
      "{ \"setup\": \"pack-inline\", \"description\": \"x\", \"mods\": [], \"steps\": [], \"seconds\": 9, \"record\": {}, \"pack\": "
        + pack
        + " }",
      "pack-inline.json"
    );

  // Fails when a pack's keys or counts are read without their checks; one row per guard.
  [Theory]
  [InlineData("{ \"after\": {} }", "takes a \"scope\".")]
  [InlineData(
    "{ \"scope\": \"some\" }",
    "\"some\" is not \"all\" or \"stacks\"."
  )]
  [InlineData(
    "{ \"scope\": \"all\", \"after\": { \"play-stations\": 1 } }",
    "is not a mod and a setup id"
  )]
  [InlineData(
    "{ \"scope\": \"all\", \"after\": { \"iiex/setups/play-stations\": 0 } }",
    "is at least 1."
  )]
  [InlineData(
    "{ \"scope\": \"all\", \"gathered\": { \"game:log-*\": 0 } }",
    "is at least 1."
  )]
  [InlineData(
    "{ \"scope\": \"all\", \"grants\": [ { \"code\": \"iiex:castplate-heavy\", \"count\": 1 } ] }",
    "takes an \"until\""
  )]
  [InlineData(
    "{ \"scope\": \"all\", \"ends\": { \"stacks\": {}, \"hands\": [] } }",
    "unknown key \"hands\"."
  )]
  [InlineData("{ \"scope\": \"all\", \"bogus\": 1 }", "unknown key \"bogus\".")]
  public void PackRefusesMalformedKeys(string pack, string message)
  {
    SetupFileException error = Assert.Throws<SetupFileException>(() =>
      Inline(pack)
    );
    Assert.Contains(message, error.Message);
  }

  // Fails when after is ignored: the earlier stage's ends never reach the pack.
  [Fact]
  public void AfterEndsStartThePackWithMetal()
  {
    SetupPack pack = Pack("pack-later");
    Assert.Equal(2, pack.Held("iiex:diagram-molten-straight"));
    Assert.Equal(1600, pack.Held("metal:iron"));
  }

  // Fails when gathered stacks or grants are not added to the start.
  [Fact]
  public void GatheredAndGrantsStartThePack()
  {
    SetupPack pack = Pack("pack-earlier");
    Assert.Equal(16, pack.Held("game:log-oak"));
    Assert.Equal(1, pack.Held("iiex:castplate-heavy"));
  }

  // Fails when a wildcard key is matched only as written, so a drawn log finds no stock.
  [Fact]
  public void DrawTakesFromAWildcardKey()
  {
    SetupPack pack = Pack("pack-earlier");
    pack.Draw("game:log-oak", 3);
    Assert.Equal(13, pack.Held("game:log-oak"));
    Assert.Equal(13, pack.Held("game:log-birch"));
  }

  // Fails when a draw past what the pack holds is not refused.
  [Fact]
  public void ShortDrawNamesTheCodeAndBothCounts()
  {
    SetupPack pack = Pack("pack-later");
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () =>
        pack.Draw("metal:iron", 1601)
    );
    Assert.Equal(
      "the pack holds 1600 of metal:iron; the step takes 1601.",
      error.Message
    );
    Assert.Equal(1600, pack.Held("metal:iron"));
  }

  // Fails when a complete end is refused.
  [Fact]
  public void EndsPassWhenEveryDeclaredStackAndMetalIsGiven()
  {
    SetupPack pack = Pack("pack-earlier");
    pack.Give("iiex:diagram-molten-straight", 1);
    pack.Give("metal:iron", 800);
    pack.CheckEnds();
  }

  // Fails when CheckEnds passes an end that hands on no declared stack, or when Give records nothing.
  [Fact]
  public void EndMissingAStackFails()
  {
    SetupPack pack = Pack("pack-earlier");
    pack.Give("metal:iron", 800);
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      pack.CheckEnds
    );
    Assert.Equal(
      "the stage ends with 0 of iiex:diagram-molten-straight; it declares 1.",
      error.Message
    );
  }

  // Fails when CheckEnds passes an end that hands on less metal than it declares.
  [Fact]
  public void EndShortOfMetalFails()
  {
    SetupPack pack = Pack("pack-earlier");
    pack.Give("iiex:diagram-molten-straight", 1);
    pack.Give("metal:iron", 799);
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      pack.CheckEnds
    );
    Assert.Equal(
      "the stage ends with 799 of metal:iron; it declares 800.",
      error.Message
    );
  }

  // Fails when a taken standing block stays on the list, so a second place is free too.
  [Fact]
  public void StandingBlockPlacesFreeOnce()
  {
    SetupPack pack = Pack("pack-later");
    Assert.True(pack.TakeStanding("iiex:workbench-bench"));
    Assert.False(pack.TakeStanding("iiex:workbench-bench"));
    Assert.False(pack.TakeStanding("iiex:anvil"));
  }

  // Fails when a stage reached through two afters adds its standing block once per path, so a second
  // place is free.
  [Fact]
  public void A_stage_reached_through_two_afters_leaves_its_standing_block_free_once()
  {
    SetupPack pack = Pack("pack-diamond");
    Assert.True(pack.TakeStanding("iiex:workbench-bench"));
    Assert.False(pack.TakeStanding("iiex:workbench-bench"));
  }

  // Fails when a concrete draw takes from a wildcard key before its own key, so a later draw of
  // another variant finds nothing.
  [Fact]
  public void A_concrete_draw_takes_its_own_key_before_a_wildcard_key()
  {
    SetupPack pack = SetupPack.Start(
      Inline(
        "{ \"scope\": \"stacks\", \"gathered\": { \"game:log-*\": 5, \"game:log-oak\": 5 } }"
      ),
      Resolve
    );

    pack.Draw("game:log-oak", 5);
    pack.Draw("game:log-birch", 1);

    Assert.Equal(4, pack.Held("game:log-*"));
  }

  // Fails when a wildcard draw spans keys, so a stage needing three planks pays with two oak and one
  // pine.
  [Fact]
  public void A_wildcard_draw_takes_all_of_it_from_one_key()
  {
    SetupPack pack = SetupPack.Start(
      Inline(
        "{ \"scope\": \"stacks\", \"gathered\": { \"game:plank-oak\": 2, \"game:plank-pine\": 1 } }"
      ),
      Resolve
    );

    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () => pack.Draw("game:plank-*", 3, "steps[0]", "3,0,0")
    );
    Assert.Equal(
      "the pack holds 3 of game:plank-* under no one key; the step takes 3.",
      error.Message
    );
  }

  // Fails when a later wildcard draw at the same position takes from another key than the first
  // draw did, so the construction pays two woods.
  [Fact]
  public void A_later_wildcard_draw_at_a_position_takes_the_key_the_first_took()
  {
    SetupPack pack = SetupPack.Start(
      Inline(
        "{ \"scope\": \"stacks\", \"gathered\": { \"game:plank-oak\": 1, \"game:plank-pine\": 3 } }"
      ),
      Resolve
    );

    pack.Draw("game:plank-*", 1, "steps[0]", "3,0,0");
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () => pack.Draw("game:plank-*", 2, "steps[1]", "3,0,0")
    );
    Assert.Equal(
      "the pack holds 0 of game:plank-*; the step takes 2.",
      error.Message
    );
  }

  // Fails when standing blocks are read from the direct after only, not through the chain.
  [Fact]
  public void StandingBlockCarriesThroughAChainOfAfters()
  {
    SetupPack pack = Pack("pack-last");
    Assert.True(pack.TakeStanding("iiex:workbench-bench"));
    Assert.False(pack.TakeStanding("iiex:workbench-bench"));
  }

  // Fails when an after that names no file is not refused by name.
  [Fact]
  public void AfterNamingNoFileFails()
  {
    SetupFileModel root = Inline(
      "{ \"scope\": \"all\", \"after\": { \"exlib/setups/pack-missing\": 1 } }"
    );
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () =>
        SetupPack.Start(root, Resolve)
    );
    Assert.Equal(
      "after names \"exlib/setups/pack-missing\", which no file has.",
      error.Message
    );
  }

  // Fails when an after onto a file with no pack is read as no ends rather than refused.
  [Fact]
  public void AfterNamingAFileWithoutAPackFails()
  {
    SetupFileModel root = Inline(
      "{ \"scope\": \"all\", \"after\": { \"exlib/setups/pack-nopack\": 1 } }"
    );
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () =>
        SetupPack.Start(root, Resolve)
    );
    Assert.Equal(
      "after names \"exlib/setups/pack-nopack\", whose file has no pack.",
      error.Message
    );
  }

  // Fails when a file with no pack starts a pack without being refused.
  [Fact]
  public void StartNeedsAPack()
  {
    Assert.Throws<ArgumentException>(() =>
      SetupPack.Start(Load("pack-nopack"), Resolve)
    );
  }

  // Fails when an after cycle is not refused; the chain would never end.
  [Fact]
  public void AfterCycleIsRefused()
  {
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(
      () =>
        SetupPack.Start(Load("pack-loop-a"), Resolve)
    );
    Assert.Equal(
      "after cycles through \"exlib/setups/pack-loop-b\".",
      error.Message
    );
  }

  private static SetupFileModel WithSteps(string? pack, string steps) =>
    SetupFileModel.Parse(
      "{ \"setup\": \"pack-steps\", \"description\": \"x\", \"mods\": [], \"steps\": "
        + steps
        + ", \"seconds\": 9, \"record\": {}"
        + (pack == null ? "" : ", \"pack\": " + pack)
        + " }",
      "pack-steps.json"
    );

  // Fails when power or source loads under scope "all", or when the refusal also catches scope
  // "stacks".
  [Theory]
  [InlineData(
    "power",
    "{ \"at\": [0, 0, 0], \"face\": \"n\", \"speed\": 1, \"torque\": 1 }"
  )]
  [InlineData(
    "source",
    "{ \"at\": [0, 0, 0], \"face\": \"n\", \"medium\": \"steam\" }"
  )]
  public void A_live_only_verb_is_refused_under_scope_all_and_read_under_stacks(
    string kind,
    string body
  )
  {
    string steps = "[{ \"" + kind + "\": " + body + " }]";

    SetupFileException error = Assert.Throws<SetupFileException>(() =>
      WithSteps("{ \"scope\": \"all\" }", steps)
    );
    SetupFileModel stacks = WithSteps("{ \"scope\": \"stacks\" }", steps);

    Assert.Equal(
      "pack-steps.json: steps[0]: \""
        + kind
        + "\" is a live-only verb, refused under pack scope \"all\": the stage builds what it drives.",
      error.Message
    );
    Assert.Equal(kind, stacks.Steps[0].Kind);
  }

  // Fails when the refusal reads only the main steps, so a during step slips through.
  [Fact]
  public void A_live_only_verb_in_a_during_action_is_refused_under_scope_all()
  {
    SetupFileException error = Assert.Throws<SetupFileException>(() =>
      SetupFileModel.Parse(
        "{ \"setup\": \"pack-steps\", \"description\": \"x\", \"mods\": [], \"steps\": [], \"seconds\": 9, \"record\": {}, \"pack\": { \"scope\": \"all\" }, \"during\": [{ \"at\": 2, \"steps\": [{ \"power\": { \"at\": [0, 0, 0], \"face\": \"n\", \"speed\": 1, \"torque\": 1 } }] }] }",
        "pack-steps.json"
      )
    );

    Assert.Contains("\"power\" is a live-only verb", error.Message);
  }

  // Fails when draws and gives are dropped from the step, or read as other counts.
  [Fact]
  public void A_step_carries_its_draws_and_gives_in_file_order()
  {
    SetupFileModel model = WithSteps(
      "{ \"scope\": \"all\" }",
      "[{ \"wait\": 1, \"draws\": { \"game:flint\": 2, \"game:stick\": 1 }, \"gives\": { \"game:charcoal\": 3 } }]"
    );

    SetupStep step = model.Steps[0];

    Assert.Equal([("game:flint", 2), ("game:stick", 1)], step.Draws);
    Assert.Equal([("game:charcoal", 3)], step.Gives);
  }

  // Fails when draws or gives load in a file with no pack, where nothing could account them.
  [Theory]
  [InlineData("draws")]
  [InlineData("gives")]
  public void Draws_and_gives_need_a_pack(string key)
  {
    SetupFileException error = Assert.Throws<SetupFileException>(() =>
      WithSteps(
        null,
        "[{ \"wait\": 1, \"" + key + "\": { \"game:flint\": 1 } }]"
      )
    );

    Assert.Equal(
      "pack-steps.json: steps[0]: \"" + key + "\" needs a file-level \"pack\".",
      error.Message
    );
  }

  // Fails when a draw's count is not checked as at least 1.
  [Fact]
  public void A_draw_of_none_is_refused()
  {
    SetupFileException error = Assert.Throws<SetupFileException>(() =>
      WithSteps(
        "{ \"scope\": \"all\" }",
        "[{ \"wait\": 1, \"draws\": { \"game:flint\": 0 } }]"
      )
    );

    Assert.Contains("is at least 1.", error.Message);
  }

  // Fails when a wildcard code asked of the pack does not reach the concrete keys it covers.
  [Fact]
  public void A_wildcard_draw_takes_from_the_concrete_stacks_it_covers()
  {
    SetupPack pack = SetupPack.Start(
      Inline(
        "{ \"scope\": \"all\", \"gathered\": { \"game:plank-oak\": 2, \"game:plank-pine\": 2 } }"
      ),
      _ => null
    );

    pack.Draw("game:plank-*", 2);

    Assert.Equal(2, pack.Held("game:plank-*"));
  }
}
#endif
