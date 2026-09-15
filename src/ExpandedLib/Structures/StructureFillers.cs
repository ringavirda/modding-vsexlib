using System.Collections.Generic;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>
/// A structure-local filler cell from the <c>fillerOffsets</c> JSON array: offset from the principal,
/// attach flag, optional per-cell collision boxes for a partial fill, and an optional passive network
/// port face/type pair, all in north orientation.
/// </summary>
public readonly record struct FillerOffset(
  Vec3i Offset,
  bool AllowAttach,
  Cuboidf[]? CollisionBoxes,
  FillerBehavior[]? Behaviors = null,
  string? PortFace = null,
  string? PortNetworkType = null
);

/// <summary>
/// A resolved world-space filler cell: attach flag, and collision boxes, behaviour connector faces
/// and <see cref="PortFace"/> already rotated into the placed orientation.
/// </summary>
public readonly record struct FillerCell(
  BlockPos Pos,
  bool AllowAttach,
  Cuboidf[]? CollisionBoxes,
  FillerBehavior[]? Behaviors = null,
  string? PortFace = null,
  string? PortNetworkType = null
);

/// <summary>
/// Helpers for the invisible mega-block footprint system. A mega-block occupies one grid cell but
/// renders across many; the surrounding cells are filled with <see cref="BlockStructureFiller"/>
/// placeholders that provide collision and reroute interaction/break/info to the principal.
/// </summary>
public static class StructureFillers {
  /// <summary>Asset code of the invisible filler block. Taken from the generated block table, not a literal.</summary>
  public static AssetLocation FillerCode { get; set; } =
    new(ExlibBlocks.Structurefiller.Code);

  // Guards the "not registered" Error to log once per process.
  private static bool _missingFillerLogged;

  private static Block? ResolveFiller(IWorldAccessor world) {
    Block? filler = world.GetBlock(FillerCode);
    if (filler == null && !_missingFillerLogged) {
      _missingFillerLogged = true;
      world.Logger.Error(
        "[exlib] StructureFillers: filler block '{0}' is not registered; footprint fillers cannot be "
          + "placed or removed.",
        FillerCode
      );
    }
    return filler;
  }

  /// <summary>Parses a resolved <c>fillerOffsets</c> node into north-orientation filler cells.</summary>
  public static List<FillerOffset> ReadOffsets(JsonObject? offsetsNode) {
    var result = new List<FillerOffset>();
    if (offsetsNode == null || !offsetsNode.Exists)
      return result;

    foreach (var entry in offsetsNode.AsArray() ?? []) {
      result.Add(
        new FillerOffset(
          new Vec3i(entry["x"].AsInt(), entry["y"].AsInt(), entry["z"].AsInt()),
          entry["allowAttach"].AsBool(false),
          ReadBoxes(entry),
          ReadBehaviors(entry),
          entry["portFace"].AsString(),
          entry["portNetwork"].AsString()
        )
      );
    }
    return result;
  }

  /// <summary>Reads a cell's optional <c>behaviors</c> array. Returns null when the cell declares none.</summary>
  private static FillerBehavior[]? ReadBehaviors(JsonObject entry) {
    if (!entry["behaviors"].Exists)
      return null;
    var nodes = entry["behaviors"].AsArray();
    if (nodes == null || nodes.Length == 0)
      return null;

    var list = new List<FillerBehavior>(nodes.Length);
    foreach (var node in nodes) {
      string? code = node["code"].AsString();
      if (string.IsNullOrEmpty(code))
        continue;
      list.Add(
        new FillerBehavior(
          code,
          ParseFace(node["face"].AsString()),
          node["properties"].Exists ? node["properties"] : null
        )
      );
    }
    return list.Count > 0 ? [.. list] : null;
  }

  /// <summary>Resolves a face name ("north"/"n"...) to a <see cref="BlockFacing"/>, or null when absent.</summary>
  private static BlockFacing? ParseFace(string? face) =>
    string.IsNullOrEmpty(face)
      ? null
      : BlockFacing.FromCode(face) ?? BlockFacing.FromFirstLetter(face[0]);

  /// <summary>
  /// Reads a cell's optional partial-fill cuboids, north orientation: <c>collisionBoxes</c> (array)
  /// takes precedence, else a single <c>collisionBox</c>, else null.
  /// </summary>
  private static Cuboidf[]? ReadBoxes(JsonObject entry) {
    if (entry["collisionBoxes"].Exists) {
      var nodes = entry["collisionBoxes"].AsArray();
      if (nodes == null || nodes.Length == 0)
        return null;
      var boxes = new List<Cuboidf>(nodes.Length);
      foreach (var node in nodes)
        if (node.AsObject<Cuboidf>() is { } box)
          boxes.Add(box);
      return boxes.Count > 0 ? [.. boxes] : null;
    }
    if (entry["collisionBox"].Exists)
      return entry["collisionBox"].AsObject<Cuboidf>() is { } box
        ? [box]
        : null;
    return null;
  }

  /// <summary>Resolves the world footprint cells for a principal block at <paramref name="principalPos"/>.</summary>
  public static List<FillerCell> FootprintCells(
    IFillerHost principal,
    BlockPos principalPos,
    int angle
  ) {
    var cells = new List<FillerCell>();
    foreach (var off in ReadOffsets(principal.FillerOffsets)) {
      Vec3i r = ExOrientation.RotateOffset(off.Offset, angle);
      // Boxes are declared in north orientation; RotateBoxes pivots on (0.5,0.5,0.5).
      Cuboidf[]? boxes =
        off.CollisionBoxes == null
          ? null
          : ExOrientation.RotateBoxes(off.CollisionBoxes, angle);
      cells.Add(
        new FillerCell(
          principalPos.AddCopy(r.X, r.Y, r.Z),
          off.AllowAttach,
          boxes,
          RotateBehaviorFaces(off.Behaviors, angle),
          off.PortFace == null
            ? null
            : ExOrientation.RotateSideWord(off.PortFace, angle),
          off.PortNetworkType
        )
      );
    }
    return cells;
  }

  /// <summary>
  /// Rotates each declared behaviour's north-orientation connector face into the placed orientation.
  /// Returns the same array reference when there is nothing to rotate.
  /// </summary>
  private static FillerBehavior[]? RotateBehaviorFaces(
    FillerBehavior[]? behaviors,
    int angle
  ) {
    if (behaviors == null || behaviors.Length == 0)
      return behaviors;
    var rotated = new FillerBehavior[behaviors.Length];
    for (int i = 0; i < behaviors.Length; i++) {
      FillerBehavior b = behaviors[i];
      rotated[i] =
        b.ConnectorFace == null
          ? b
          : b with {
            ConnectorFace = ExOrientation.RotateFacing(b.ConnectorFace, angle),
          };
    }
    return rotated;
  }

  /// <summary>True when every cell is free (air or replaceable) so fillers can be placed.</summary>
  public static bool CanPlace(
    IWorldAccessor world,
    IEnumerable<FillerCell> cells
  ) {
    Block? filler = ResolveFiller(world);
    if (filler == null)
      return false;
    foreach (var cell in cells) {
      Block existing = world.BlockAccessor.GetBlock(cell.Pos);
      if (existing.Id != 0 && !existing.IsReplacableBy(filler))
        return false;
    }
    return true;
  }

  /// <summary>Places filler blocks at every cell and links each to the principal. Server-side only.</summary>
  public static void PlaceFillers(
    IWorldAccessor world,
    BlockPos principalPos,
    IEnumerable<FillerCell> cells
  ) {
    if (world.Side != EnumAppSide.Server)
      return;

    Block? filler = ResolveFiller(world);
    if (filler == null)
      return;

    foreach (var cell in cells) {
      world.BlockAccessor.SetBlock(filler.BlockId, cell.Pos);
      if (
        world.BlockAccessor.GetBlockEntity(cell.Pos)
        is BlockEntityStructureFiller be
      ) {
        be.Principal = principalPos.Copy();
        be.AllowAttach = cell.AllowAttach;
        be.CollisionBoxes = cell.CollisionBoxes;
        be.PortFace = cell.PortFace;
        be.PortNetworkType = cell.PortNetworkType;
        // Recreates hosted behaviours now that the principal link is set.
        be.SetHostedBehaviors(cell.Behaviors);
        be.MarkDirty(true);
      }
    }
  }

  /// <summary>Clears the structure's filler cells that are linked to <paramref name="principalPos"/>.</summary>
  public static void RemoveFillers(
    IWorldAccessor world,
    BlockPos principalPos,
    IEnumerable<FillerCell> cells
  ) {
    if (world.Side != EnumAppSide.Server)
      return;

    Block? filler = ResolveFiller(world);
    if (filler == null)
      return;

    foreach (var cell in cells) {
      if (world.BlockAccessor.GetBlock(cell.Pos).Id != filler.BlockId)
        continue;
      if (
        world.BlockAccessor.GetBlockEntity(cell.Pos)
          is BlockEntityStructureFiller be
        && be.Principal != null
        && be.Principal.Equals(principalPos)
      )
        world.BlockAccessor.SetBlock(0, cell.Pos);
    }
  }
}
