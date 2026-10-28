#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>Where a crafted product goes under a pack and how the ledger pays for a laid block with the
/// item a player holds for it (setups.md, The pack).</summary>
[Collection("Setups")]
public class SetupPackStowTests
{
  private const int Products = 12;

  private static string Fixture(string name) =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Setups",
      "Fixtures",
      name
    );

  private static SetupScene Build(string? pack, string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupstow\"], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}"
          + (pack == null ? "" : ", \"pack\": " + pack)
          + "}",
        "t.json"
      ),
      [Fixture("SetupStow")],
      null,
      _ => { }
    );

  private static string Pack(int ore) =>
    "{\"scope\": \"all\", \"gathered\": {\"setupstow:ore\": " + ore + "}}";

  private static string Craft(string output) =>
    "{\"craft\": {\"output\": \"setupstow:" + output + "\"}}";

  private static string Place(string id, string code, int x) =>
    "{\"place\": {\"id\": \""
    + id
    + "\", \"code\": \"setupstow:"
    + code
    + "\", \"at\": ["
    + x
    + ", 0, 0]}}";

  private static string Parts(System.Func<int, string> step) =>
    "[" + string.Join(", ", Enumerable.Range(0, Products).Select(step)) + "]";

  // Fails when a craft under a pack still stows its product in the hotbar: the tenth distinct product
  // finds no empty slot and the build throws, and a hotbar slot holds a product after the crafts.
  [Fact]
  public void A_pack_run_crafts_more_products_than_the_hotbar_holds_and_places_each()
  {
    string crafts = Parts(i => Craft("part-p" + i));
    string places = Parts(i => Place("p" + i, "part-p" + i, i));
    using SetupScene scene = Build(
      Pack(Products),
      crafts.TrimEnd(']') + ", " + places.TrimStart('[')
    );

    for (int i = 0; i < Products; i++)
      Assert.Equal(
        $"setupstow:part-p{i}",
        scene.World.GetBlock(scene.PositionOf("p" + i)).Code.ToString()
      );
    Assert.All(scene.Player.Hotbar, slot => Assert.True(slot.Empty));
  }

  // Fails when the ledger pays for a laid block only with its own code: a crafted rot-s draws nothing
  // for the laid rot-w and the place stops on a pack that holds 0 of it.
  [Fact]
  public void A_crafted_rotated_variant_pays_for_a_differently_rotated_laid_one()
  {
    using SetupScene scene = Build(
      Pack(2),
      "[" + Craft("rot-s") + ", " + Place("r", "rot-w", 0) + "]"
    );

    Assert.Equal(
      "setupstow:rot-w",
      scene.World.GetBlock(scene.PositionOf("r")).Code.ToString()
    );
  }

  // Fails when the ledger pays with any drop of the block rather than one the pack holds, or pays a
  // block whose drops it holds none of: the refusal then names the drop, not the code laid.
  [Fact]
  public void A_laid_block_whose_held_item_is_not_in_the_pack_is_refused_naming_the_code_laid()
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      Build(
        Pack(2),
        "[" + Craft("rot-s") + ", " + Place("f", "fixed-n", 0) + "]"
      )
    );

    Assert.Equal(
      "t.json: steps[1]: the pack holds 0 of setupstow:fixed-n; the step takes 1.",
      e.Message
    );
  }

  // Fails when a craft without a pack stops stowing its product in the hotbar.
  [Fact]
  public void A_run_without_a_pack_stows_a_crafted_product_in_the_hotbar()
  {
    using SetupScene scene = Build(null, "[" + Craft("part-p0") + "]");

    ItemSlot slot = Assert.Single(scene.Player.Hotbar, s => !s.Empty);
    Assert.Equal(
      "setupstow:part-p0",
      slot.Itemstack.Collectible.Code.ToString()
    );
  }
}
#endif
