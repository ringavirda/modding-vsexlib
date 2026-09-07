using System;
using System.Collections.Generic;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="ExBlockNames.AddVariantQualifier"/>'s registration order and replace-in-place
/// contract, and <see cref="ExBlockNames.Decorate"/>'s material/rock/brick precedence and
/// parenthetical merging. <c>TestLang</c> echoes every key back rather than formatting it, so a
/// decorated name is asserted either as the untouched base name or, where the exact args a lookup
/// receives are what is under test, through <c>TestLang.Service.Received()</c> - the same idiom
/// <c>ExInfoTests</c> uses. Both statics are process-wide, so <see cref="Dispose"/> removes every
/// qualifier group this class registered and restores the two lang keys the merge test restubs, so
/// neither lingers for other test classes.
/// </summary>
public class ExBlockNamesTests : IDisposable {
  private readonly List<string> _addedQualifiers = [];

  public void Dispose() {
    foreach (string group in _addedQualifiers)
      ExBlockNames.RemoveVariantQualifier(group);
    TestLang
      .Service.Get("exlib:blockname-suffixed", Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    TestLang
      .Service.Get("exlib:blockname-listsep", Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
  }

  private static Block BlockWith(
    params (string key, string value)[] variants
  ) => TestBlocks.Configure(new Block(), "iiex:testblock", 1, variants);

  private void AddVariantQualifier(string variantGroup, string langPrefix) {
    ExBlockNames.AddVariantQualifier(variantGroup, langPrefix);
    _addedQualifiers.Add(variantGroup);
  }

  [Fact]
  public void AddVariantQualifier_appends_new_groups_in_registration_order() {
    AddVariantQualifier("exblocknamestest-order-a", "iiex:order-a-");
    AddVariantQualifier("exblocknamestest-order-b", "iiex:order-b-");

    var qualifiers = ExBlockNames.Qualifiers;
    int indexA = -1,
      indexB = -1;
    for (int i = 0; i < qualifiers.Count; i++) {
      if (qualifiers[i].Group == "exblocknamestest-order-a")
        indexA = i;
      if (qualifiers[i].Group == "exblocknamestest-order-b")
        indexB = i;
    }

    Assert.True(indexA >= 0 && indexB >= 0);
    Assert.True(indexA < indexB);
  }

  [Fact]
  public void AddVariantQualifier_replaces_an_existing_groups_prefix_without_moving_it() {
    AddVariantQualifier("exblocknamestest-replace", "iiex:replace-old-");
    AddVariantQualifier("exblocknamestest-replace-after", "iiex:after-");
    int before = IndexOf("exblocknamestest-replace");

    AddVariantQualifier("exblocknamestest-replace", "iiex:replace-new-");

    Assert.Equal(before, IndexOf("exblocknamestest-replace"));
    Assert.Equal(
      "iiex:replace-new-",
      Find("exblocknamestest-replace").LangPrefix
    );
  }

  private static int IndexOf(string group) {
    var qualifiers = ExBlockNames.Qualifiers;
    for (int i = 0; i < qualifiers.Count; i++)
      if (qualifiers[i].Group == group)
        return i;
    return -1;
  }

  private static (string Group, string LangPrefix) Find(string group) =>
    ExBlockNames.Qualifiers[IndexOf(group)];

  [Fact]
  public void Decorate_with_no_recognised_variant_returns_the_base_name_unchanged() {
    Block block = BlockWith();

    Assert.Equal("Piping", ExBlockNames.Decorate(block, "Piping"));
  }

  [Fact]
  public void Decorate_with_a_material_variant_looks_up_the_material_lang_key() {
    Block block = BlockWith(("material", "copper"));

    ExBlockNames.Decorate(block, "Piping");

    TestLang.Service.Received().Get("material-copper", Arg.Any<object[]>());
    TestLang
      .Service.Received()
      .Get(
        "exlib:blockname-suffixed",
        Arg.Is<object[]>(a =>
          (string)a[0] == "Piping" && (string)a[1] == "material-copper"
        )
      );
  }

  [Fact]
  public void Decorate_prefers_material_over_rock_when_both_are_set() {
    Block block = BlockWith(
      ("material", "copper"),
      ("rock", "basalt-precedence")
    );

    ExBlockNames.Decorate(block, "Piping");

    TestLang
      .Service.DidNotReceive()
      .Get("rock-basalt-precedence", Arg.Any<object[]>());
  }

  [Fact]
  public void Decorate_with_a_rock_variant_and_no_material_looks_up_the_rock_lang_key() {
    Block block = BlockWith(("rock", "granite"));

    ExBlockNames.Decorate(block, "Canal");

    TestLang.Service.Received().Get("rock-granite", Arg.Any<object[]>());
  }

  [Fact]
  public void Decorate_with_a_brick_variant_looks_up_the_blocks_own_domain_brickname_key() {
    Block block = BlockWith(("brick", "fire"));

    ExBlockNames.Decorate(block, "Wall");

    TestLang.Service.Received().Get("iiex:brickname-fire", Arg.Any<object[]>());
  }

  [Fact]
  public void Decorate_applies_a_registered_qualifier_group_after_the_built_in_clause() {
    AddVariantQualifier("exblocknamestest-tier", "iiex:tier-");
    Block block = BlockWith(("exblocknamestest-tier", "advanced"));

    ExBlockNames.Decorate(block, "Reactor");

    TestLang.Service.Received().Get("iiex:tier-advanced", Arg.Any<object[]>());
  }

  [Fact]
  public void Decorate_merges_a_second_qualifier_into_an_existing_parenthetical_group() {
    // Restub the two lookups AppendQualifier drives so the merge branch (name already ends in
    // ")") is reachable: TestLang otherwise echoes the key, never producing a "(...)" suffix.
    TestLang
      .Service.Get("exlib:blockname-suffixed", Arg.Any<object[]>())
      .Returns(ci => {
        var a = ci.Arg<object[]>();
        return $"{a[0]} ({a[1]})";
      });
    TestLang
      .Service.Get("exlib:blockname-listsep", Arg.Any<object[]>())
      .Returns(", ");
    AddVariantQualifier("exblocknamestest-merge", "iiex:merge-");
    Block block = BlockWith(
      ("material", "steel"),
      ("exblocknamestest-merge", "polished")
    );

    string decorated = ExBlockNames.Decorate(block, "Piping");

    Assert.Equal("Piping (material-steel, iiex:merge-polished)", decorated);
  }
}
