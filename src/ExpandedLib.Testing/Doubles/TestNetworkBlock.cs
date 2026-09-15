using System.Collections.Generic;
using ExpandedLib.Blocks;
using ExpandedLib.Networks;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Testing;

/// <summary>
/// Minimal concrete <see cref="BlockNetworkNode"/> for graph tests, with orientation and network
/// type set directly rather than parsed from asset variants.
/// </summary>
public sealed class TestNetworkBlock : BlockNetworkNode {
  private readonly string _networkType;

  public override string NetworkType => _networkType;

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "straight", ["ns", "we", "ud"] } };

  protected override string GetFallbackOrientation(string? type) => "ns";

  private TestNetworkBlock(string networkType, string orientation) {
    _networkType = networkType;
    Type = "straight";
    Orientation = orientation;
  }

  /// <summary>
  /// Builds and registers a node block of <paramref name="networkType"/> and
  /// <paramref name="orientation"/>, with a code and id so it resolves through the store.
  /// </summary>
  public static TestNetworkBlock Create(
    string networkType,
    string orientation,
    int id,
    string? code = null
  ) =>
    TestBlocks.Configure(
      new TestNetworkBlock(networkType, orientation),
      code ?? $"test:{networkType}-{orientation}-{id}",
      id
    );

  /// <summary>
  /// One block per token of <paramref name="orientations"/>, sharing the code stem
  /// <paramref name="stem"/>, each with its own <see cref="BlockBehaviorExOrientable"/> in network
  /// mode. Ids run from <paramref name="firstId"/> upward.
  /// </summary>
  public static TestNetworkBlock[] Family(
    string networkType,
    string stem,
    string scheme,
    string[] orientations,
    int firstId = 1
  ) {
    var family = new TestNetworkBlock[orientations.Length];

    for (int i = 0; i < orientations.Length; i++) {
      string token = orientations[i];
      TestNetworkBlock block = TestBlocks.Configure(
        new TestNetworkBlock(networkType, token),
        $"{stem}-{token}",
        firstId + i,
        ("orientation", token)
      );

      var behaviour = new BlockBehaviorExOrientable(block);
      behaviour.Initialize(
        new JsonObject(
          JToken.Parse($$"""{"mode":"network","scheme":"{{scheme}}"}""")
        )
      );
      // Both arrays: GetBehavior<T> reads CollectibleBehaviors, fan-outs read BlockBehaviors.
      block.BlockBehaviors = [behaviour];
      block.CollectibleBehaviors = [behaviour];
      family[i] = block;
    }

    return family;
  }
}

/// <summary>Test-only hooks for setting a <see cref="BlockNetworkNode"/>'s shape-family and
/// connector state directly, bypassing the asset-load pipeline.</summary>
public static class NetworkNodeTestHooks {
  /// <summary>Sets <see cref="BlockNetworkNode.Type"/> directly.</summary>
  public static void SetNetworkTypeForTest(
    this BlockNetworkNode node,
    string type
  ) => node.SetNetworkTypeForTest(type);

  /// <summary>Sets <see cref="BlockNetworkNode.Orientation"/> directly.</summary>
  public static void ApplyOrientationForTest(
    this BlockNetworkNode node,
    string token
  ) => node.ApplyOrientationForTest(token);
}
