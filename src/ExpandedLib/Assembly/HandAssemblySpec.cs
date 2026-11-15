using Vintagestory.API.Common;

namespace ExpandedLib.Assembling;

/// <summary>What a block declares under <c>attributes.handAssembly</c>: the tool that fixes it, the fasteners each joint spends and the cap that closes an open crossing.</summary>
/// <param name="Tool">Item code of the tool held in the off hand, wildcards allowed.</param>
/// <param name="Fastener">Item code of the fastener a joint spends.</param>
/// <param name="PerJoint">Count of <paramref name="Fastener"/> items one joint spends, at least 1.</param>
/// <param name="Cap">Item code of the cap held in the off hand to close an open crossing of the part, wildcards allowed; null when the part takes no cap.</param>
public sealed record HandAssemblySpec(
  string Tool,
  string Fastener,
  int PerJoint,
  string? Cap = null
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
    string? cap = attribute["cap"].AsString();
    return new HandAssemblySpec(
      tool,
      fastener,
      attribute["perJoint"].AsInt(1),
      string.IsNullOrEmpty(cap) ? null : cap
    );
  }
}
