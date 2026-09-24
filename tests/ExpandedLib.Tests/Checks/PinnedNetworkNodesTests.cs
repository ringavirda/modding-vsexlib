using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="PinnedNetworkNodes.Violations"/> over a planted layout pinning a network
/// node and one pinning a plain block.</summary>
public class PinnedNetworkNodesTests {
  public PinnedNetworkNodesTests() => TestModDomain.Register();

  private const string Pinned = "plantedpins";
  private const string Clean = "plantedpinsclean";
  private static readonly Assembly Here =
    typeof(PinnedNetworkNodesTests).Assembly;

  private static IEnumerable<ExBlockDef> Blocks(string domain, string pinned) =>
    [
      ExBlockDef
        .Create(domain, "valve")
        .VariantGroup("orientation", "ns", "we", "ud")
        .NetworkOriented(),
      ExBlockDef.Create(domain, "brick").VariantGroup("side", "n", "s"),
      ExBlockDef
        .Create(domain, "boiler")
        .Attribute(
          "multiblockFacings",
          new Dictionary<string, string> { [$"{domain}:{pinned}"] = "north" }
        ),
    ];

  /// <summary>Declares its blocks only under <see cref="Pinned"/> and <see cref="Clean"/>, so other
  /// scans of this assembly never see them.</summary>
  private sealed class Layouts : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain switch {
        Pinned => Blocks(domain, "valve-ns"),
        Clean => Blocks(domain, "brick-n"),
        _ => [],
      };
  }

  [Fact]
  [PlantedDefect(
    typeof(PinnedNetworkNodes),
    nameof(PinnedNetworkNodes.Violations)
  )]
  public void A_layout_pinning_a_network_node_is_reported() {
    string finding = Assert.Single(
      PinnedNetworkNodes.Violations(out _, (Pinned, Here))
    );

    Assert.StartsWith(
      "plantedpins:boiler pins 'plantedpins:valve-ns', which is a network node",
      finding
    );
  }

  [Fact]
  public void A_layout_pinning_a_plain_block_passes() {
    Assert.Empty(
      PinnedNetworkNodes.Violations(out int codesChecked, (Clean, Here))
    );
    Assert.Equal(1, codesChecked);
  }

  // Fails when PinnedNetworkNodesCheck.Run stops reporting a pinned code that matches a node.
  [Fact]
  [PlantedDefect(
    typeof(PinnedNetworkNodesCheck),
    nameof(PinnedNetworkNodesCheck.Run)
  )]
  public void The_harness_and_the_check_report_the_same_pin() {
    string harness = Assert.Single(
      PinnedNetworkNodes.Violations(out _, (Pinned, Here))
    );
    string check = Assert.Single(
      PinnedNetworkNodesCheck
        .Run(new AssemblyCheckSource((Pinned, Here)), Pinned)
        .Errors
    );

    Assert.StartsWith("plantedpins:boiler pins 'plantedpins:valve-ns'", check);
    Assert.Equal(check, harness);
  }
}
