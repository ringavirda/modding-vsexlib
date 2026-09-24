using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Checks;
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
  private const string Union = "plantednodesunion";
  private static readonly Assembly Here =
    typeof(NetworkNodeContractTests).Assembly;

  /// <summary>A node with no <c>type</c> group under <see cref="Broken"/>, and one with it under
  /// <see cref="Clean"/>.</summary>
  private sealed class TypedNode : BlockNetworkNode, IExBlockDefProvider {
    public override string NetworkType => "test";

    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain switch {
        Broken => [Node(domain, "untyped")],
        Clean => [Node(domain, "typed").VariantGroup("type", "plain")],
        _ => [],
      };

    private static ExBlockDef Node(string domain, string code) =>
      ExBlockDef
        .Create(domain, code)
        .Class(EntityRegistry.KeyFor(domain, typeof(TypedNode)))
        .VariantGroup("orientation", "ns", "we", "ud")
        .NetworkOriented();
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
            .Class(EntityRegistry.KeyFor(domain, typeof(Member)))
            .EntityBehavior(TestWorld.NetworkMemberClass),
        ]
        : [];
  }

  /// <summary>Under <see cref="Union"/> only: a node known by its class alone, a node known by its
  /// JSON alone, and a block declaring a membership subclass with no network.</summary>
  private sealed class UnionNode : BlockNetworkNode, IExBlockDefProvider {
    public override string NetworkType => "test";

    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain == Union
        ?
        [
          ExBlockDef
            .Create(domain, "classonly")
            .Class(EntityRegistry.KeyFor(domain, typeof(UnionNode))),
          ExBlockDef
            .Create(domain, "wrongmode")
            .Class(EntityRegistry.KeyFor(domain, typeof(UnionNode)))
            .VariantGroup("type", "plain")
            .Behavior("ExOrientable", new { mode = "side" }),
          ExBlockDef
            .Create(domain, "jsononly")
            .VariantGroup("orientation", "ns", "we", "ud")
            .NetworkOriented(),
          ExBlockDef
            .Create(domain, "subclassmember")
            .EntityBehavior($"{domain}.{nameof(PlantedMember)}"),
        ]
        : [];
  }

  private sealed class PlantedMember(BlockEntity be)
    : BEBehaviorNetworkMember(be);

  private static IReadOnlyList<string> CheckErrors() =>
    NetworkNodeContractCheck
      .Run(new AssemblyCheckSource((Union, Here)), Union)
      .Errors;

  // Fails when NetworkNodeContractCheck stops selecting a node by its resolved class.
  [Fact]
  [PlantedDefect(
    typeof(NetworkNodeContractCheck),
    nameof(NetworkNodeContractCheck.Run)
  )]
  public void A_node_known_by_its_class_alone_is_reported_on_both_sides() {
    const string typeGroup =
      "plantednodesunion:classonly (UnionNode) declares NO `type` variant group";
    const string scheme =
      "plantednodesunion:blocktypes/classonly.json (plantednodesunion:classonly) declares 0 "
      + "`ExOrientable` behaviour(s)";

    Assert.Contains(
      NetworkNodeContract.TypeGroupViolations(Union, Here),
      f => f.StartsWith(typeGroup)
    );
    Assert.Contains(
      NetworkNodeContract.SchemeViolations(Union, Here, out _),
      f => f.StartsWith(scheme)
    );
    Assert.Contains(CheckErrors(), f => f.StartsWith(typeGroup));
    Assert.Contains(CheckErrors(), f => f.StartsWith(scheme));
  }

  // Fails when NetworkNodeContractCheck stops reading a class-known node's ExOrientable mode.
  [Fact]
  [PlantedDefect(
    typeof(NetworkNodeContract),
    nameof(NetworkNodeContract.SchemeViolations)
  )]
  public void A_class_known_node_orienting_by_another_mode_is_reported_on_both_sides() {
    const string finding =
      "plantednodesunion:blocktypes/wrongmode.json (plantednodesunion:wrongmode) declares "
      + "`ExOrientable` with mode 'side', not 'network'";

    Assert.Contains(
      NetworkNodeContract.SchemeViolations(Union, Here, out _),
      f => f.StartsWith(finding)
    );
    Assert.Contains(CheckErrors(), f => f.StartsWith(finding));
  }

  // Fails when NetworkNodeContractCheck stops selecting a node by its ExOrientable declaration.
  [Fact]
  [PlantedDefect(
    typeof(NetworkNodeContract),
    nameof(NetworkNodeContract.TypeGroupViolations)
  )]
  public void A_node_known_by_its_json_alone_is_reported_on_both_sides() {
    const string finding =
      "plantednodesunion:jsononly declares NO `type` variant group";

    Assert.Contains(
      NetworkNodeContract.TypeGroupViolations(Union, Here),
      f => f.StartsWith(finding)
    );
    Assert.Contains(CheckErrors(), f => f.StartsWith(finding));
  }

  // Fails when NetworkNodeContractCheck stops resolving a behaviour key to a membership subclass.
  [Fact]
  [PlantedDefect(
    typeof(NetworkNodeContract),
    nameof(NetworkNodeContract.MembershipViolations)
  )]
  public void A_membership_subclass_with_no_network_is_reported_on_both_sides() {
    const string finding =
      "plantednodesunion:subclassmember declares a network membership in entityBehaviors";

    Assert.Contains(
      NetworkNodeContract.MembershipViolations(Union, Here),
      f => f.StartsWith(finding)
    );
    Assert.Contains(CheckErrors(), f => f.StartsWith(finding));
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
    Assert.Equal(2, defsChecked);
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
