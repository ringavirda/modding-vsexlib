using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;
#if GAME_GE_1_22
using Vintagestory.GameContent;
#endif

namespace ExpandedLib.Tests;

#if GAME_GE_1_22
/// <summary>The page Shift+H opens for a placed block and the page a stack links to, when the stack
/// the block picks has no page of its own.</summary>
public class HandbookPagesTests
{
  private static readonly BlockPos At = new(10, 10, 10);

  private readonly TestWorld _world = new();

  private static string PageOf(Block block) =>
    GuiHandbookItemStackPage.PageCodeForStack(new ItemStack(block));

  private string Nearest(Block block) =>
    HandbookPages.Nearest(_world.ClientApi, new ItemStack(block));

  // A plain block; the handbook lists it when page is true.
  private Block Plain(
    string code,
    int id,
    bool page,
    string[]? groupBy = null,
    params (string, string)[] variants
  )
  {
    Block block = TestBlocks.Configure(new Block(), code, id, variants);
    if (page)
      block.CreativeInventoryTabs = ["general"];
    if (groupBy != null)
      block.Attributes = new JsonObject(
        new JObject
        {
          ["handbook"] = new JObject { ["groupBy"] = new JArray(groupBy) },
        }
      );
    _world.Register(block);
    return block;
  }

  // A block of the family with ExOrientable on it, as every machine variant loads.
  private Block Oriented(Block block, string mode, string? scheme = null)
  {
    var behaviour = new BlockBehaviorExOrientable(block);
    behaviour.Initialize(
      new JsonObject(
        JToken.Parse(
          scheme == null
            ? $$"""{"mode":"{{mode}}"}"""
            : $$"""{"mode":"{{mode}}","scheme":"{{scheme}}"}"""
        )
      )
    );
    block.BlockBehaviors = [behaviour];
    block.CollectibleBehaviors = [behaviour];
    return block;
  }

  // Bricks a and b on four sides; only a facing north has a handbook page.
  private Block[] Megastructure()
  {
    var all = new System.Collections.Generic.List<Block>();
    int id = 1;
    foreach (string brick in new[] { "a", "b" })
    foreach (string side in new[] { "n", "e", "s", "w" })
    {
      Block block = Oriented(
        TestBlocks.Configure(
          new BlockFilledMegastructure(),
          $"stub:mega-{brick}-{side}",
          id++,
          ("brick", brick),
          ("side", side)
        ),
        "horizontal"
      );
      if (brick == "a" && side == "n")
        block.CreativeInventoryTabs = ["general"];
      _world.Register(block);
      all.Add(block);
    }
    return [.. all];
  }

  // Fails when candidates are limited to the pick's own brick: the pick is brick b and the page
  // is brick a.
  [Fact]
  public void A_megastructure_pick_of_another_brick_opens_the_page_it_has()
  {
    Block[] mega = Megastructure();
    Block placed = mega.Single(b => b.Code.Path == "mega-b-e");
    _world.Place(At, placed);

    Assert.Equal(
      PageOf(mega.Single(b => b.Code.Path == "mega-a-n")),
      HandbookText.PickPage(_world.ClientApi, At)
    );
  }

  // Fails when a link's page code is the stack's own instead of its provider's: the stack is
  // brick b and the provider answers the page of brick a.
  [Fact]
  public void A_link_to_a_megastructure_stack_goes_to_the_page_its_provider_gives()
  {
    Block[] mega = Megastructure();

    Assert.Equal(
      PageOf(mega.Single(b => b.Code.Path == "mega-a-n")),
      HandbookMadeAtPatch.PageCode(
        _world.ClientApi,
        new ItemStack(mega.Single(b => b.Code.Path == "mega-b-e"))
      )
    );
  }

  // Fails under the same brick limit: the filler's pick forwards to the principal.
  [Fact]
  public void A_filler_of_that_megastructure_opens_the_same_page()
  {
    Block[] mega = Megastructure();
    _world.Place(At, mega.Single(b => b.Code.Path == "mega-b-e"));
    BlockPos filler = At.UpCopy(1);
    var be = new BlockEntityStructureFiller { Principal = At.Copy() };
    _world.Place(filler, _world.Filler, be);
    ClientTwin.Stand(_world, be);

    Assert.Equal(
      PageOf(mega.Single(b => b.Code.Path == "mega-a-n")),
      HandbookText.PickPage(_world.ClientApi, filler)
    );
  }

  // Fails when candidates are limited to the block's own orientation state: the node picks its
  // fallback ns and the page stands on we.
  [Fact]
  public void A_node_pick_with_a_fallback_orientation_opens_the_page_it_has()
  {
    TestNetworkBlock[] node = TestNetworkBlock.Family(
      "test",
      "stub:node",
      "Axis",
      ["ns", "we", "ud"]
    );
    foreach (TestNetworkBlock block in node)
      _world.Register(block);
    node[1].CreativeInventoryTabs = ["general"];
    _world.Place(At, node[2]);

    Assert.Equal(PageOf(node[1]), HandbookText.PickPage(_world.ClientApi, At));
  }

  // Fails when the groupBy selection is ignored: the variant rule picks alpha-a, which differs
  // from alpha-b in one state, over beta-b, which differs in none.
  [Fact]
  public void A_groupBy_selection_wins_over_the_variant_group_rule()
  {
    Block picked = Plain("stub:alpha-b", 1, false, ["beta-*"], ("v", "b"));
    Plain("stub:alpha-a", 2, true, null, ("v", "a"));
    Block grouped = Plain("stub:beta-b", 3, true, null, ("v", "b"));

    Assert.Equal(PageOf(grouped), Nearest(picked));
  }

  // Fails when the tie goes to the higher id: both candidates differ from the pick in one state.
  [Fact]
  public void A_tie_goes_to_the_lower_block_id()
  {
    Block picked = Plain("stub:alpha-b", 1, false, null, ("v", "b"));
    Plain("stub:alpha-c", 9, true, null, ("v", "c"));
    Block lower = Plain("stub:alpha-a", 5, true, null, ("v", "a"));

    Assert.Equal(PageOf(lower), Nearest(picked));
  }

  // Fails when the own-page check is skipped: the grouped block of the lower id is chosen instead.
  [Fact]
  public void A_stack_with_its_own_page_keeps_it()
  {
    Block own = Plain("stub:beta-b", 9, true, ["beta-*"]);
    Plain("stub:beta-a", 2, true);

    Assert.Equal(PageOf(own), Nearest(own));
  }

  // Fails when the provider answers for a world that is not the client's.
  [Fact]
  public void A_server_world_gets_the_stack_own_page_code()
  {
    Block[] mega = Megastructure();
    Block picked = mega.Single(b => b.Code.Path == "mega-b-n");
    var provider = (IHandBookPageCodeProvider)
      picked.GetBehavior<BlockBehaviorExOrientable>()!;

    Assert.Equal(
      PageOf(picked),
      provider.HandbookPageCodeForStack(_world.World, new ItemStack(picked))
    );
  }
}
#endif
