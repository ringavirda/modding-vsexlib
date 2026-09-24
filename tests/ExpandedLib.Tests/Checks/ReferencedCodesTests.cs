using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;
using static ExpandedLib.Testing.ReferencedCodes;

namespace ExpandedLib.Tests;

/// <summary><see cref="ReferencedCodes"/>' resolving rules over planted references into a planted
/// domain holding one variant-grouped block.</summary>
public class ReferencedCodesTests {
  public ReferencedCodesTests() => TestModDomain.Register();

  private const string Domain = "plantedrefs";

  private static readonly IReadOnlyDictionary<string, Assembly> Domains =
    new Dictionary<string, Assembly> {
      [Domain] = typeof(ReferencedCodesTests).Assembly,
    };

  /// <summary>Declares its block only under <see cref="Domain"/>, so other scans of this assembly
  /// never see it.</summary>
  private sealed class Gear : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain == Domain
        ?
        [
          ExBlockDef
            .Create(domain, "gear")
            .VariantGroup("size", "small", "large"),
        ]
        : [];
  }

  private static Reference Block(string code) =>
    new(
      "plantedrefs:recipes/grid/gears.json",
      Origin.RecipeIngredient,
      code,
      true
    );

  [Fact]
  [PlantedDefect(typeof(ReferencedCodes), nameof(ReferencedCodes.Unresolvable))]
  public void A_reference_naming_no_block_of_its_domain_is_reported() {
    Assert.Equal(
      [Block("plantedrefs:gear-huge")],
      ReferencedCodes.Unresolvable(
        [Block("plantedrefs:gear-huge"), Block("plantedrefs:gear-small")],
        Domains
      )
    );
  }

  [Fact]
  public void A_defined_variant_a_wildcard_and_a_domain_out_of_reach_pass() {
    Assert.Empty(
      ReferencedCodes.Unresolvable(
        [
          Block("plantedrefs:gear-small"),
          Block("plantedrefs:gear-*"),
          Block("othermod:gear-huge"),
        ],
        Domains
      )
    );
  }

  // Fails when Checkable counts a reference into a domain the run holds no registry for.
  [Fact]
  [PlantedDefect(typeof(ReferencedCodes), nameof(ReferencedCodes.Checkable))]
  public void A_reference_into_a_domain_out_of_reach_is_not_checkable() {
    Assert.Equal(
      0,
      ReferencedCodes.Checkable(
        [Block("othermod:gear-huge"), Block("gear-huge")],
        Domains
      )
    );
  }

  [Fact]
  public void References_into_a_held_domain_are_checkable() {
    Assert.Equal(
      2,
      ReferencedCodes.Checkable(
        [Block("plantedrefs:gear-huge"), Block("plantedrefs:gear-small")],
        Domains
      )
    );
  }

  [Fact]
  [PlantedDefect(typeof(ReferencedCodes), nameof(ReferencedCodes.BareButOurs))]
  public void A_bare_code_naming_our_block_is_reported() {
    Assert.Equal(
      [Block("gear-small")],
      ReferencedCodes.BareButOurs(
        [Block("gear-small"), Block("stone")],
        Domains
      )
    );
  }

  [Fact]
  public void A_qualified_code_a_bare_wildcard_and_a_bare_game_code_pass() {
    Assert.Empty(
      ReferencedCodes.BareButOurs(
        [Block("plantedrefs:gear-small"), Block("gear-*"), Block("stone")],
        Domains
      )
    );
  }
}
