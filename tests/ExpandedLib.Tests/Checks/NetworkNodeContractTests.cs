using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="NetworkNodeContract"/>'s type-group, scheme and combined rules over planted
/// network nodes and a planted membership.</summary>
public class NetworkNodeContractTests {
  public NetworkNodeContractTests() => TestModDomain.Register();

  private const string Broken = "plantednodes";
  private const string Clean = "plantednodesclean";
  private static readonly Assembly Here =
    typeof(NetworkNodeContractTests).Assembly;

  /// <summary>A node with no <c>type</c> group under <see cref="Broken"/>, and one with it under
  /// <see cref="Clean"/>.</summary>
  private sealed class TypedNode : BlockNetworkNode, IExBlockDefProvider {
    public override string NetworkType => "test";

    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain switch {
        Broken => [ExBlockDef.Create(domain, "untyped")],
        Clean =>
        [
          ExBlockDef.Create(domain, "typed").VariantGroup("type", "plain"),
        ],
        _ => [],
      };
  }

  /// <summary>A node naming a scheme its orientation states are not, under <see cref="Broken"/>,
  /// and naming its own, under <see cref="Clean"/>.</summary>
  private sealed class SchemedNode : BlockNetworkNode, IExBlockDefProvider {
    public override string NetworkType => "test";

    public static IEnumerable<ExBlockDef> Definitions(string domain) {
      ExBlockDef Axis(string code) =>
        ExBlockDef
          .Create(domain, code)
          .Class(EntityRegistry.KeyFor(domain, typeof(SchemedNode)))
          .VariantGroup("type", "plain")
          .VariantGroup("orientation", "ns", "we", "ud");

      return domain switch {
        Broken =>
        [
          Axis("misnamed")
            .Behavior(
              "ExOrientable",
              new { mode = "network", scheme = "Face" }
            ),
        ],
        Clean => [Axis("named").NetworkOriented()],
        _ => [],
      };
    }
  }

  /// <summary>A block declaring a network membership with no network, under
  /// <see cref="Broken"/> only.</summary>
  private sealed class Member : Block, IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain == Broken
        ?
        [
          ExBlockDef
            .Create(domain, "member")
            .EntityBehavior(TestWorld.NetworkMemberClass),
        ]
        : [];
  }

  [Fact]
  [PlantedDefect(
    typeof(NetworkNodeContract),
    nameof(NetworkNodeContract.TypeGroupViolations)
  )]
  public void A_node_with_no_type_group_is_reported() {
    string finding = Assert.Single(
      NetworkNodeContract.TypeGroupViolations(Broken, Here)
    );

    Assert.StartsWith(
      "plantednodes:untyped (TypedNode) declares NO `type` variant group",
      finding
    );
  }

  [Fact]
  public void A_node_with_a_type_group_passes() {
    Assert.Empty(NetworkNodeContract.TypeGroupViolations(Clean, Here));
  }

  [Fact]
  [PlantedDefect(
    typeof(NetworkNodeContract),
    nameof(NetworkNodeContract.SchemeViolations)
  )]
  public void A_node_naming_another_scheme_is_reported() {
    string finding = Assert.Single(
      NetworkNodeContract.SchemeViolations(Broken, Here, out _)
    );

    Assert.Contains("plantednodes:blocktypes/misnamed.json", finding);
    Assert.Contains(
      "names scheme 'Face' but its `orientation` states are Axis's",
      finding
    );
  }

  [Fact]
  public void A_node_naming_its_own_scheme_passes() {
    Assert.Empty(
      NetworkNodeContract.SchemeViolations(Clean, Here, out int defsChecked)
    );
    Assert.Equal(1, defsChecked);
  }

  // Fails when Violations drops either rule it joins.
  [Fact]
  [PlantedDefect(
    typeof(NetworkNodeContract),
    nameof(NetworkNodeContract.Violations)
  )]
  public void Violations_reports_the_untyped_node_and_the_untyped_membership() {
    IReadOnlyList<string> findings = NetworkNodeContract.Violations(
      Broken,
      Here
    );

    Assert.Contains(
      findings,
      f => f.StartsWith("plantednodes:untyped (TypedNode)")
    );
    Assert.Contains(
      findings,
      f => f.StartsWith("plantednodes:member (Member)")
    );
  }

  // Other providers in this assembly declare untyped memberships under every domain.
  [Fact]
  public void Violations_passes_a_typed_node() {
    Assert.DoesNotContain(
      NetworkNodeContract.Violations(Clean, Here),
      f => f.Contains("(TypedNode)") || f.Contains("(SchemedNode)")
    );
  }
}
