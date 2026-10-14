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
}
#endif
