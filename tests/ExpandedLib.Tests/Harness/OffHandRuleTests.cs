#if GAME_GE_1_22
using System;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The off-hand check: the game's off-hand slot takes a stack whose storage flags include
/// Offhand, and <see cref="TestPlayer.HoldOffHand"/> refuses the rest.</summary>
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
}
#endif
