using Vintagestory.API.Common;

namespace ExpandedLib.Assembling;

/// <summary>What a block declares under <c>attributes.handAssembly</c>: the tool that fixes it and the fasteners each joint spends.</summary>
/// <param name="Tool">Item code of the tool held in the off hand, wildcards allowed.</param>
/// <param name="Fastener">Item code of the fastener a joint spends.</param>
/// <param name="PerJoint">Count of <paramref name="Fastener"/> items one joint spends, at least 1.</param>
public sealed record HandAssemblySpec(
  string Tool,
  string Fastener,
  int PerJoint
) {
  /// <summary>The attribute key the spec is stored under.</summary>
  public const string AttributeKey = "handAssembly";

  /// <summary>Reads the spec a block declares.</summary>
  /// <param name="block">The block to read; its <c>Attributes</c> may be null.</param>
  /// <returns>The spec, or null when the block has no <c>handAssembly</c> attribute or it names no tool or fastener.</returns>
  public static HandAssemblySpec? Of(Block block) {
    var attribute = block.Attributes?[AttributeKey];
    if (attribute == null || !attribute.Exists)
      return null;

    string? tool = attribute["tool"].AsString();
    string? fastener = attribute["fastener"].AsString();
    if (string.IsNullOrEmpty(tool) || string.IsNullOrEmpty(fastener))
      return null;
    return new HandAssemblySpec(tool, fastener, attribute["perJoint"].AsInt(1));
  }
}
