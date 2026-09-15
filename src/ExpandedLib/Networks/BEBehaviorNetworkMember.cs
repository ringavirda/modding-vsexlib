using System;
using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>
/// One network membership held by a block entity: which network it joins and which faces it couples
/// on. The membership registers its own cell as a graph node and drops it again when the block is
/// removed.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorNetworkMember(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity),
    INetworkMember,
    IFillerHostedBehavior {
  /// <summary>The graph manager this membership registers with, resolved on <see cref="Initialize"/>.</summary>
  public BlockNetworkModSystem? NetworkSystem { get; set; }

  /// <inheritdoc/>
  public virtual string NetworkType { get; protected set; } = "";

  /// <inheritdoc/>
  public virtual string NetworkTypeAt(IBlockAccessor world, BlockPos pos) =>
    NetworkType;

  /// <summary>The faces this membership couples on, or empty to leave the answer to the block.</summary>
  public virtual BlockFacing[] Connectors { get; set; } = [];

  /// <summary>Takes <paramref name="orientation"/> (single-letter side codes, e.g. <c>"ns"</c>,
  /// <c>"we"</c>) as this membership's connector faces; a string naming no side leaves the block to
  /// answer.</summary>
  public void DeclareConnectors(string? orientation) =>
    Connectors = BlockNetworkModSystem.SidesToFaces(orientation);

  /// <inheritdoc/>
  /// <remarks>Answers from <see cref="Connectors"/> when set, otherwise from the block's own
  /// connector set.</remarks>
  public virtual bool HasConnectorAt(
    IBlockAccessor world,
    BlockPos pos,
    BlockFacing face
  ) =>
    Connectors.Length > 0
      ? Connectors.Contains(face)
      : (Blockentity.Block as INetworkConnector)?.HasConnectorAt(
        world,
        pos,
        face
      ) ?? false;

  /// <inheritdoc/>
  public virtual bool IsConnectionBroken(IBlockAccessor world, BlockPos pos) =>
    (Blockentity as INetworkNode)?.IsConnectionBroken() ?? false;

  /// <inheritdoc/>
  public virtual bool IsNetworkEndPoint =>
    (Blockentity.Block as INetworkConnector)?.IsNetworkEndPoint ?? false;

  /// <inheritdoc/>
  public virtual bool AcceptsNeighbour(Block neighbour) =>
    (Blockentity.Block as INetworkConnector)?.AcceptsNeighbour(neighbour)
    ?? true;

  #region Hosted on a footprint cell

  /// <summary>Takes the cell's connector face, already rotated into the placed orientation, as this
  /// membership's own; a cell declaring <c>passThrough</c> couples on the opposite face too.</summary>
  public void ConfigureFromFiller(
    BlockPos? principal,
    BlockFacing? connectorFace,
    JsonObject? properties
  ) {
    if (connectorFace == null)
      return;

    Connectors =
      properties?["passThrough"].AsBool(false) == true
        ? [connectorFace, connectorFace.Opposite]
        : [connectorFace];
  }

  #endregion

  #region Graph registration

  /// <summary>Network state this membership holds ready to push back into its run on load, read once
  /// before the cell registers; <c>null</c> for a membership that persists nothing.</summary>
  protected virtual object? SavedNetworkState => null;

  /// <summary>Takes <paramref name="declared"/> as this membership's network type when it has none of
  /// its own; a disagreement with an existing block-entity answer is logged, not applied.</summary>
  private void ApplyDeclaredNetworkType(ICoreAPI api, string? declared) {
    if (string.IsNullOrEmpty(declared))
      return;

    if (string.IsNullOrEmpty(NetworkType)) {
      NetworkType = declared;
      return;
    }

    if (
      api.Side == EnumAppSide.Server
      && !string.Equals(declared, NetworkType, StringComparison.Ordinal)
    )
      api.Logger.Error(
        "Network membership on {0} at {1} declares network type \"{2}\" but its block entity already "
          + "names \"{3}\", which wins. Drop the declaration.",
        Blockentity.Block?.Code,
        Pos,
        declared,
        NetworkType
      );
  }

  /// <summary>Takes <paramref name="declared"/> as this membership's connector faces when it has none
  /// of its own; a disagreement with an existing set is logged, not applied.</summary>
  private void ApplyDeclaredConnectors(ICoreAPI api, string? declared) {
    if (string.IsNullOrEmpty(declared))
      return;

    BlockFacing[] parsed = BlockNetworkModSystem.SidesToFaces(declared);
    if (Connectors.Length == 0) {
      Connectors = parsed;
      return;
    }

    if (
      api.Side == EnumAppSide.Server
      && !Connectors.ToHashSet().SetEquals(parsed)
    )
      api.Logger.Error(
        "Network membership on {0} at {1} declares connectors \"{2}\" but its host already set "
          + "\"{3}\", which wins. Drop the declaration.",
        Blockentity.Block?.Code,
        Pos,
        declared,
        string.Concat(Connectors.Select(f => f.Code[0]))
      );
  }

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);

    // A behaviour added in code carries no properties; every read here is optional.
    ApplyDeclaredNetworkType(api, properties?["networkType"].AsString());
    ApplyDeclaredConnectors(api, properties?["connectors"].AsString());

    NetworkSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();

    if (api.Side != EnumAppSide.Server)
      return;

    // A blank type throws out of the factory lookup inside a chunk load; logged as an error, not a
    // warning.
    if (string.IsNullOrEmpty(NetworkType)) {
      api.Logger.Error(
        "Network membership on {0} at {1} names no network type and so joins no graph. Declare "
          + "\"networkType\" on the behaviour, or set it from the block entity.",
        Blockentity.Block?.Code,
        Pos
      );
      return;
    }

    // Read first: AddNode's broadcast reaches OnNetworkUpdate, which clears this state.
    object? pendingRestore = SavedNetworkState;

    if (NetworkSystem.GetNetworkAt(Pos) == null)
      NetworkSystem.AddNode(api.World.BlockAccessor, Pos, NetworkType);

    if (
      pendingRestore != null
      && NetworkSystem.GetNetworkAt(Pos) is BlockNetwork network
    ) {
      network.RestoreState(pendingRestore);
      network.BroadcastUpdate(api.World.BlockAccessor);
    }
  }

  // removal-only teardown: a chunk unload is not a removal, and deregistering there would fracture a live network.

  /// <summary>Drops this position out of the graph, which splits or shrinks the network it belonged
  /// to; removal-only, since a chunk unload leaves the node in place and <see cref="Initialize"/>
  /// re-adopts the position when the chunk reloads.</summary>
  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    // Uses the block entity's api, set independently of this behaviour's own initialization state.
    ICoreAPI? api = Blockentity.Api;
    if (api?.Side == EnumAppSide.Server)
      NetworkSystem?.RemoveNode(api.World.BlockAccessor, Pos);
  }

  #endregion
}
