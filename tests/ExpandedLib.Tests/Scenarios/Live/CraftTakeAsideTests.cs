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

  /// <summary>Fails when a product the station handed into a bare working hand is taken back as the
  /// step's tool, so the hold collects nothing.</summary>
  [Fact]
  public void A_product_in_a_bare_hand_is_not_the_wielded_tool()
  {
    var world = new TestWorld();
    var product = new ItemStack(world.RegisterItem("test:counter"));
    var hammer = new ItemStack(world.RegisterItem("test:hammer"));

    Assert.Null(CraftStep.Wielded(new DummySlot(product), null));
    Assert.Null(CraftStep.Wielded(new DummySlot(product), hammer));
  }

  /// <summary>Fails when the step's own tool is left in the hand instead of going back to the pack.</summary>
  [Fact]
  public void The_steps_tool_in_the_hand_is_the_wielded_tool()
  {
    var world = new TestWorld();
    var hammer = new ItemStack(world.RegisterItem("test:hammer"));
    var hand = new DummySlot(hammer.Clone());

    Assert.Same(hand.Itemstack, CraftStep.Wielded(hand, hammer));
  }
}
#endif
