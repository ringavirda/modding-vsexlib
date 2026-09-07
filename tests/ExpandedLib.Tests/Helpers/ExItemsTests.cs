using System.Collections.Generic;
using ExpandedLib.Helpers;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="ExItems.WrenchStacks"/>: one stack per registered wrench item, built once per world
/// and cached in the API's <c>ObjectCache</c> - the cache is keyed by session, not process, so a
/// second world with a different item set must rebuild rather than reuse the first's stacks.
/// </summary>
public class ExItemsTests {
  private static IWorldAccessor WorldWith(params Item[] items) {
    var api = Substitute.For<ICoreAPI>();
    api.ObjectCache.Returns(new Dictionary<string, object>());
    var world = Substitute.For<IWorldAccessor>();
    world.Api.Returns(api);
    world.Items.Returns(items);
    return world;
  }

  private static Item ItemNamed(string code) =>
    new() { Code = new AssetLocation(code) };

  [Fact]
  public void WrenchStacks_returns_one_stack_per_item_whose_path_contains_wrench() {
    IWorldAccessor world = WorldWith(
      ItemNamed("iiex:wrench-basic"),
      ItemNamed("iiex:hammer"),
      ItemNamed("iiex:wrench-fancy")
    );

    ItemStack[] stacks = ExItems.WrenchStacks(world);

    Assert.Equal(2, stacks.Length);
    Assert.Contains(stacks, s => s.Collectible.Code.Path == "wrench-basic");
    Assert.Contains(stacks, s => s.Collectible.Code.Path == "wrench-fancy");
  }

  [Fact]
  public void WrenchStacks_is_empty_when_no_item_matches() {
    IWorldAccessor world = WorldWith(ItemNamed("iiex:hammer"));

    Assert.Empty(ExItems.WrenchStacks(world));
  }

  [Fact]
  public void WrenchStacks_is_cached_and_not_rebuilt_on_a_second_call() {
    IWorldAccessor world = WorldWith(ItemNamed("iiex:wrench-basic"));

    ItemStack[] first = ExItems.WrenchStacks(world);
    ItemStack[] second = ExItems.WrenchStacks(world);

    Assert.Same(first, second);
  }

  [Fact]
  public void WrenchStacks_rebuilds_for_a_different_worlds_cache() {
    IWorldAccessor first = WorldWith(ItemNamed("iiex:wrench-basic"));
    IWorldAccessor second = WorldWith(ItemNamed("iiex:wrench-fancy"));

    ItemStack[] firstStacks = ExItems.WrenchStacks(first);
    ItemStack[] secondStacks = ExItems.WrenchStacks(second);

    Assert.NotSame(firstStacks, secondStacks);
    Assert.Equal("wrench-fancy", secondStacks[0].Collectible.Code.Path);
  }
}
