using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks;

/// <summary>What a routed click carries to its handler.</summary>
/// <param name="World">The world of the click.</param>
/// <param name="Player">The clicking player.</param>
/// <param name="PrincipalSelection">The selection moved to the principal's cell, box id kept.</param>
/// <param name="ClickedCell">The footprint cell clicked, or null when the principal's own cell was.</param>
/// <param name="Id">The control id the handler was registered for; null for a body handler.</param>
/// <param name="Held">The stack in the player's active hotbar slot, or null for an empty hand.</param>
public sealed record ControlContext(
  IWorldAccessor World,
  IPlayer Player,
  BlockSelection PrincipalSelection,
  BlockPos? ClickedCell,
  string? Id,
  ItemStack? Held
);

/// <summary>
/// Sends a click on a principal to the handler of the control it hit. A control's handler works only on that
/// control's box; the body takes machine-wide verbs only. Built once per principal block and called from its
/// <see cref="Structures.IFillerInteractionTarget.OnFillerInteractStart"/> and
/// <c>OnBlockInteractStart</c>.
/// </summary>
public sealed class ControlRouter {
  private sealed record Entry(
    string Id,
    System.Func<ItemStack?, bool>? Held,
    System.Func<ControlContext, bool> Handler
  );

  private readonly List<Entry> _entries = [];
  private System.Func<ControlContext, bool>? _body;

  /// <summary>Registers <paramref name="handler"/> for the control <paramref name="id"/>, taking any held item.</summary>
  /// <returns>This router.</returns>
  public ControlRouter On(
    string id,
    System.Func<ControlContext, bool> handler
  ) => On(id, null, handler);

  /// <summary>Registers <paramref name="handler"/> for the control <paramref name="id"/> when the held item
  /// satisfies <paramref name="held"/>.</summary>
  /// <param name="id">The control id.</param>
  /// <param name="held">Takes the active slot's stack, null for an empty hand; null takes any held item.</param>
  /// <param name="handler">Returns true when it took the click.</param>
  /// <returns>This router.</returns>
  public ControlRouter On(
    string id,
    System.Func<ItemStack?, bool>? held,
    System.Func<ControlContext, bool> handler
  ) {
    _entries.Add(new Entry(id, held, handler));
    return this;
  }

  /// <summary>Sets the handler of a click that hit no control: machine-wide verbs only. It replaces an earlier
  /// one.</summary>
  /// <returns>This router.</returns>
  public ControlRouter OnBody(System.Func<ControlContext, bool> handler) {
    _body = handler;
    return this;
  }

  /// <summary>
  /// Routes a click. A hit on a control tries the hit id, then its alsoIds, each against its handlers in
  /// registration order whose held predicate accepts the held item; the first runs and its answer is returned.
  /// A hit on no control, on an id whose state is Hidden, or on a control none of whose handlers takes the held
  /// item goes to the body handler for the first two and returns false for the last.
  /// </summary>
  /// <param name="world">The world of the click.</param>
  /// <param name="player">The clicking player.</param>
  /// <param name="principalSel">The selection at the principal's cell, as the filler forwards it.</param>
  /// <param name="clickedCell">The footprint cell clicked, or null for the principal's own cell.</param>
  /// <returns>True when a handler took the click; false lets the machine-wide verbs and construction run.</returns>
  public bool Route(
    IWorldAccessor world,
    IPlayer player,
    BlockSelection principalSel,
    BlockPos? clickedCell
  ) {
    IBlockAccessor accessor = world.BlockAccessor;
    ControlHit hit = ControlHit.Of(
      accessor.GetBlock(principalSel.Position),
      accessor.GetBlockEntity(principalSel.Position),
      principalSel
    );
    ItemStack? held = player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
    if (hit.Ids.Count == 0)
      return _body?.Invoke(
          new ControlContext(
            world,
            player,
            principalSel,
            clickedCell,
            null,
            held
          )
        ) ?? false;
    foreach (string id in hit.Ids)
      foreach (Entry entry in _entries)
        if (entry.Id == id && (entry.Held?.Invoke(held) ?? true))
          return entry.Handler(
            new ControlContext(world, player, principalSel, clickedCell, id, held)
          );
    return false;
  }
}
