#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The off-hand check: the game's off-hand slot takes a stack whose storage flags include
/// Offhand, <see cref="TestPlayer.HoldOffHand"/> refuses the rest unless a mod's allow list names the
/// exact code, and <see cref="OffHandRule.Stale"/> names the entries that no longer offend.</summary>
public class OffHandRuleTests
{
  private const EnumItemStorageFlags Both =
    EnumItemStorageFlags.General | EnumItemStorageFlags.Offhand;

  // Fails when HoldOffHand writes the slot without asking the rule.
  [Fact]
  public void HoldOffHand_RefusesAGeneralOnlyStack()
  {
    var world = new TestWorld();
    TestPlayer player = world.Player();
    var stack = new ItemStack(world.RegisterItem("test:plain-lump"));

    var e = Assert.Throws<ArgumentException>(() => player.HoldOffHand(stack));

    Assert.Contains("test:plain-lump", e.Message);
    Assert.Contains("Offhand", e.Message);
    Assert.Contains(OffHandRule.FileName, e.Message);
    Assert.True(player.OffHandSlot.Empty);
  }

  // Fails when Takes tests the flags for equality with Offhand.
  [Fact]
  public void HoldOffHand_TakesAStackFlagged257()
  {
    var world = new TestWorld();
    TestPlayer player = world.Player();
    Item tool = world.RegisterItem("test:flagged-tool");
    tool.StorageFlags = Both;
    var stack = new ItemStack(tool);

    player.HoldOffHand(stack);

    Assert.Same(stack, player.OffHandSlot.Itemstack);
    Assert.True(OffHandRule.Takes(stack));
  }

  // Fails when Refusal ignores the allow list.
  [Fact]
  public void Refusal_PassesAListedCode()
  {
    var world = new TestWorld();
    var stack = new ItemStack(world.RegisterItem("test:coke"));

    Assert.Null(SetupOffHand.Refusal(stack, Listed("test:coke")));
    Assert.NotNull(SetupOffHand.Refusal(stack, Listed("test:other")));
  }

  // Fails when the allow list matches a code by StartsWith in either direction.
  [Theory]
  [InlineData("test:coke-big")]
  [InlineData("test:cok")]
  public void Refusal_RefusesNeighboursOfAListedCode(string code)
  {
    var world = new TestWorld();
    var stack = new ItemStack(world.RegisterItem(code));

    string? refusal = SetupOffHand.Refusal(stack, Listed("test:coke"));

    Assert.NotNull(refusal);
    Assert.Contains(code, refusal);
  }

  // Fails when Stale skips the check of the default stack against the slot.
  [Fact]
  public void Stale_NamesAnEntryThatNowTakesOffhand()
  {
    using var mod = new ModFolder("test:tool x", "\"test:tool\"");
    var world = new TestWorld();
    world.RegisterItem("test:tool").StorageFlags = Both;

    IReadOnlyList<string> stale = OffHandRule.Stale(world.World, mod.Path);

    string line = Assert.Single(stale);
    Assert.StartsWith("test:tool: ", line);
    Assert.Contains("takes the off-hand slot", line);
  }

  // Fails when Stale skips the use check.
  [Fact]
  public void Stale_NamesAnEntryNothingUses()
  {
    using var mod = new ModFolder("test:lump x", "\"test:other\"");
    var world = new TestWorld();
    world.RegisterItem("test:lump");

    IReadOnlyList<string> stale = OffHandRule.Stale(world.World, mod.Path);

    string line = Assert.Single(stale);
    Assert.StartsWith("test:lump: ", line);
    Assert.Contains("nothing puts it", line);
  }

  // Fails when a code no item or block has is counted as offending.
  [Fact]
  public void Stale_NamesAnEntryNoItemHas()
  {
    using var mod = new ModFolder("test:ghost x", "\"test:ghost\"");
    var world = new TestWorld();

    IReadOnlyList<string> stale = OffHandRule.Stale(world.World, mod.Path);

    string line = Assert.Single(stale);
    Assert.StartsWith("test:ghost: ", line);
    Assert.Contains("no item or block", line);
  }

  // Fails when Stale returns every entry.
  [Fact]
  public void Stale_IsEmptyForAnEntryStillOffendingAndUsed()
  {
    using var mod = new ModFolder(
      "test:viajson x\ntest:vias y",
      "\"test:vias\""
    );
    mod.Write(
      Path.Combine("tests", "live", "setup.json"),
      "{ /* held */ steps: [ { Offhand: { code: \"test:viajson\" } } ] }"
    );
    var world = new TestWorld();
    world.RegisterItem("test:viajson");
    world.RegisterItem("test:vias");

    Assert.Empty(OffHandRule.Stale(world.World, mod.Path));
  }

  // Fails when the wildcard guard of Parse is deleted.
  [Fact]
  public void Parse_RefusesWildcard() =>
    Refused("game:sand-* sand", "a.txt, line 1", "game:sand-*");

  // Fails when the colon guard of Parse is deleted.
  [Fact]
  public void Parse_MissingColon() =>
    Refused("coke ItemChargingBox fill", "a.txt, line 1", "coke");

  // Fails when the mechanic guard of Parse is deleted.
  [Fact]
  public void Parse_EmptyMechanic() =>
    Refused(
      "game:coke fill\ngame:charcoal   ",
      "a.txt, line 2",
      "game:charcoal"
    );

  // Fails when the duplicate guard of Parse is deleted.
  [Fact]
  public void Parse_Duplicate() =>
    Refused("game:coke a\n\ngame:coke b", "a.txt, line 3", "game:coke");

  // Fails when Parse reads a comment or a blank line as an entry.
  [Fact]
  public void Parse_SkipsCommentsAndBlanks()
  {
    IReadOnlyDictionary<string, string> parsed = SetupOffHand.Parse(
      "# header\n\n  \ngame:coke fill; cover\r\n\tgame:charcoal\tcover\n",
      "a.txt"
    );

    Assert.Equal(2, parsed.Count);
    Assert.Equal("fill; cover", parsed["game:coke"]);
    Assert.Equal("cover", parsed["game:charcoal"]);
  }

  private static void Refused(string text, string where, string code)
  {
    var e = Assert.Throws<SetupFileException>(() =>
      SetupOffHand.Parse(text, "a.txt")
    );
    Assert.Contains(where, e.Message);
    Assert.Contains(code, e.Message);
  }

  private static IReadOnlyDictionary<string, string> Listed(string code) =>
    new Dictionary<string, string> { [code] = "a mechanic" };

  private sealed class ModFolder : IDisposable
  {
    public string Path { get; } =
      System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "offhand-" + Guid.NewGuid().ToString("N")
      );

    public ModFolder(string list, string csSource)
    {
      Write(
        System.IO.Path.Combine("tests", "live", OffHandRule.FileName),
        list
      );
      Write(
        System.IO.Path.Combine("tests", "Uses.cs"),
        "class U { string s = " + csSource + "; }"
      );
    }

    public void Write(string relative, string text)
    {
      string file = System.IO.Path.Combine(Path, relative);
      Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
      File.WriteAllText(file, text);
    }

    public void Dispose() => Directory.Delete(Path, true);
  }
}
#endif
