#if GAME_GE_1_22
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>How the <c>craft</c> step takes a station's tools and outputs while the hand holds the
/// product the station gave (scenarios.md, <c>craft</c>).</summary>
public class CraftTakeAsideTests
{
  /// <summary>Fails when the take runs with the product still in the hand, or the hand is cleared
  /// after the take so the product is lost.</summary>
  [Fact]
  public void A_take_with_the_product_in_the_hand_returns_the_taken_stack_and_keeps_the_product()
  {
    var world = new TestWorld();
    var product = new ItemStack(world.RegisterItem("test:flask"));
    var diagram = new ItemStack(world.RegisterItem("test:diagram"));
    var hand = new DummySlot(product);
    bool handWasEmpty = false;

    ItemStack? got = CraftStep.TakeAside(
      hand,
      () =>
      {
        handWasEmpty = hand.Empty;
        hand.Itemstack = diagram;
      }
    );

    Assert.True(handWasEmpty);
    Assert.Same(diagram, got);
    Assert.Same(product, hand.Itemstack);
  }

  /// <summary>Fails when an empty hand is left holding what the take put there.</summary>
  [Fact]
  public void A_take_with_an_empty_hand_leaves_the_hand_empty()
  {
    var world = new TestWorld();
    var diagram = new ItemStack(world.RegisterItem("test:diagram"));
    var hand = new DummySlot(null);

    ItemStack? got = CraftStep.TakeAside(hand, () => hand.Itemstack = diagram);

    Assert.Same(diagram, got);
    Assert.Null(hand.Itemstack);
  }
}
#endif
