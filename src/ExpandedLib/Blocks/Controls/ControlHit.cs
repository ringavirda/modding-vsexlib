using System.Collections.Generic;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks;

/// <summary>The control a click or look landed on, read from the selection's box id. Ids exist on 1.22
/// only; on 1.21 and 1.20 every hit is a body hit.</summary>
/// <param name="Id">The id of the box hit, or null for a body hit or an id whose state is Hidden.</param>
/// <param name="Ids">The ids a click may act as, in the order they are tried: <paramref name="Id"/>, then its
/// <see cref="BlockControl.AlsoIds"/> in table order. Empty when <paramref name="Id"/> is null.</param>
public readonly record struct ControlHit(string? Id, IReadOnlyList<string> Ids) {
  /// <summary>A body hit: no id, no ids.</summary>
  public static ControlHit Body => new(null, []);

  /// <summary>Reads the control hit by <paramref name="sel"/> on a principal.</summary>
  /// <param name="principal">The principal's block; its <c>controls</c> table gives the alsoIds.</param>
  /// <param name="entity">The principal's block entity, asked for the control's state; null counts as Shown.</param>
  /// <param name="sel">A selection whose position is the principal's, as a filler forwards it.</param>
  /// <returns>The hit; <see cref="Body"/> when the box has no id, the id's state is
  /// <see cref="ControlState.Hidden"/>, or the game version carries no box ids (before 1.22).</returns>
  public static ControlHit Of(
    Block principal,
    BlockEntity? entity,
    BlockSelection sel
  ) {
#if GAME_GE_1_22
    string? id = sel.SelectionBoxId;
    if (id == null || ControlBoxes.StateOf(entity, id) == ControlState.Hidden)
      return Body;
    var ids = new List<string> { id };
    if (BlockControls.Find(principal, id) is { } row)
      ids.AddRange(row.AlsoIds);
    return new ControlHit(id, ids);
#else
    return Body;
#endif
  }

  /// <summary>Reads the control the player is looking at, for block info and interaction help.</summary>
  /// <param name="principal">The principal's block.</param>
  /// <param name="world">The world the principal stands in.</param>
  /// <param name="principalPos">The principal's position.</param>
  /// <param name="player">The looking player; null or a player looking at nothing gives <see cref="Body"/>.</param>
  /// <returns>The hit when the player's selection is the principal's cell or a filler linked to it;
  /// <see cref="Body"/> for any other cell.</returns>
  public static ControlHit LookedAt(
    Block principal,
    IWorldAccessor world,
    BlockPos principalPos,
    IPlayer? player
  ) {
    BlockSelection? sel = player?.CurrentBlockSelection;
    if (sel?.Position == null)
      return Body;
    bool onStructure =
      sel.Position.Equals(principalPos)
      || (
        world.BlockAccessor.GetBlockEntity(sel.Position)
          is BlockEntityStructureFiller { Principal: { } linked }
        && linked.Equals(principalPos)
      );
    return onStructure
      ? Of(principal, world.BlockAccessor.GetBlockEntity(principalPos), sel)
      : Body;
  }
}
