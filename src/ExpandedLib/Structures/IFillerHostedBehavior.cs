using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>A behaviour a mega-block can host on one of its footprint cells.</summary>
public interface IFillerHostedBehavior {
  /// <summary>Called once, before the behaviour's own <c>Initialize</c>, with the principal block's position and the cell's already-rotated connector face.</summary>
  void ConfigureFromFiller(
    BlockPos? principal,
    BlockFacing? connectorFace,
    JsonObject? properties
  );
}

/// <summary>A single behaviour declared on a <c>fillerOffsets</c> cell, with its class code, connector face and properties.</summary>
public readonly record struct FillerBehavior(
  string Code,
  BlockFacing? ConnectorFace,
  JsonObject? Properties
);
