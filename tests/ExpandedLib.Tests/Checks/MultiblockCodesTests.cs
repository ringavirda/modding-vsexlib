using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="MultiblockCodes.Unresolvable(ValueTuple{string, Assembly}[])"/> over a
/// planted furnace whose layout asks for blocks that are and are not defined.</summary>
public class MultiblockCodesTests {
  public MultiblockCodesTests() => TestModDomain.Register();

  private const string Missing = "plantedlayouts";
  private const string Present = "plantedlayoutsclean";
  private const string Strict = "plantedlayoutsstrict";
  private static readonly Assembly Here = typeof(MultiblockCodesTests).Assembly;

  private static ExBlockDef Furnace(string domain, params string[] wanted) {
    var numbers = new JObject();
    for (int i = 0; i < wanted.Length; i++)
      numbers[wanted[i]] = i + 1;
    return ExBlockDef
      .Create(domain, "furnace")
      .Attribute(
        "multiblockStructure",
        new JObject { ["blockNumbers"] = numbers }
      );
  }

  /// <summary>Declares its blocks only under <see cref="Missing"/> and <see cref="Present"/>, so
  /// other scans of this assembly never see them.</summary>
  private sealed class Furnaces : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain switch {
        Missing =>
        [
          Furnace(
            domain,
            "plantedlayouts:brick-*",
            "plantedlayouts:nosuchbrick"
          ),
          ExBlockDef.Create(domain, "brick").VariantGroup("side", "n", "s"),
        ],
        Present =>
        [
          Furnace(domain, "plantedlayoutsclean:brick-n", "game:stone-granite"),
          ExBlockDef.Create(domain, "brick").VariantGroup("side", "n", "s"),
        ],
        Strict =>
        [
          Furnace(
            domain,
            "plantedlayoutsstrict:brick-x",
            "plantedlayoutsstrict:ingot",
            "plantedlayoutsstrict:wall-andesite"
          ),
          ExBlockDef.Create(domain, "brick").VariantGroup("side", "n", "s"),
          ExBlockDef
            .Create(domain, "wall")
            .VariantGroupFromProperties("rock", "block/rock"),
        ],
        _ => [],
      };
  }

  /// <summary>Declares an item under <see cref="Strict"/> only, whose code a layout
  /// names.</summary>
  private sealed class Ingots : IExItemDefProvider {
    public static IEnumerable<ExItemDef> Definitions(string domain) =>
      domain == Strict ? [ExItemDef.Create(domain, "ingot")] : [];
  }

  [Fact]
  [PlantedDefect(typeof(MultiblockCodes), nameof(MultiblockCodes.Unresolvable))]
  public void A_layout_code_no_definition_provides_is_reported() {
    Assert.Equal(
      ["plantedlayouts:furnace wants 'plantedlayouts:nosuchbrick'"],
      MultiblockCodes.Unresolvable((Missing, Here))
    );
  }

  [Fact]
  public void A_defined_variant_and_a_game_code_pass() {
    Assert.Empty(
      MultiblockCodes.Unresolvable(out int codesChecked, (Present, Here))
    );
    Assert.Equal(1, codesChecked);
  }

  // Fails when MultiblockCodesCheck.Run stops reporting a layout code no registered block matches.
  [Fact]
  [PlantedDefect(
    typeof(MultiblockCodesCheck),
    nameof(MultiblockCodesCheck.Run)
  )]
  public void The_harness_and_the_check_report_the_same_code() {
    const string finding =
      "plantedlayouts:furnace wants 'plantedlayouts:nosuchbrick'";

    Assert.Equal([finding], MultiblockCodes.Unresolvable((Missing, Here)));
    Assert.Equal(
      [finding],
      MultiblockCodesCheck
        .Run(new AssemblyCheckSource((Missing, Here)), Missing)
        .Errors
    );
  }

  // Fails when MultiblockCodesCheck.AnyProvides accepts a state the group does not declare, when
  // the check counts item codes as blocks, or when a worldproperty group's `*` stops matching.
  [Fact]
  [PlantedDefect(typeof(MultiblockCodes), nameof(MultiblockCodes.Unresolvable))]
  public void An_undeclared_state_and_an_item_code_are_reported() {
    Assert.Equal(
      [
        "plantedlayoutsstrict:furnace wants 'plantedlayoutsstrict:brick-x'",
        "plantedlayoutsstrict:furnace wants 'plantedlayoutsstrict:ingot'",
      ],
      MultiblockCodes.Unresolvable((Strict, Here))
    );
  }
}
