using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>
/// Client-side aiming preview: tints the cells a click with the held item would change in one colour
/// and the cells that refuse it in another, through one <see cref="ExHighlightSlots"/> slot.
/// </summary>
/// <remarks>An item drives it from its held tick with <see cref="Drive"/>; the cells are recomputed only
/// when the selected block or the held item changes, and the tint is cleared on deselect, on a switch to
/// another item and on leaving the world. Create one per item on the client (<c>OnLoaded</c> runs once per item code,
/// when <c>api</c> is a <c>ICoreClientAPI</c>), each with a slot key unique to the item, e.g. <c>"yourmod:preview-" + Code</c>; it registers a 100 ms game tick and a leave-world handler that live as long
/// as the client.</remarks>
public sealed class ExCoursePreview {
  /// <summary>The default tint of the cells the click would change: green, translucent (ARGB).</summary>
  public static readonly int DefaultChangeColor = ColorUtil.ColorFromRgba(
    94,
    215,
    94,
    0x60
  );

  /// <summary>The default tint of the cells that refuse the click: red, translucent (ARGB).</summary>
  public static readonly int DefaultRefuseColor = ColorUtil.ColorFromRgba(
    215,
    94,
    94,
    0x60
  );

  private readonly ICoreClientAPI _capi;
  private readonly int _slotId;
  private readonly int _changeColor;
  private readonly int _refuseColor;

  private IPlayer? _player;
  private CollectibleObject? _held;
  private BlockPos? _selected;

  /// <summary>Creates the preview and reserves its highlight slot.</summary>
  /// <param name="capi">The client API; its tick and leave-world events carry the clears.</param>
  /// <param name="slotKey">The <see cref="ExHighlightSlots.Reserve"/> key of this preview's slot,
  /// unique to the item, e.g. <c>"yourmod:preview-" + Code</c>; two previews on one key clear each other's tint.</param>
  /// <param name="changeColor">ARGB tint of the cells the click would change;
  /// <see cref="DefaultChangeColor"/> when null.</param>
  /// <param name="refuseColor">ARGB tint of the cells that refuse the click;
  /// <see cref="DefaultRefuseColor"/> when null.</param>
  public ExCoursePreview(
    ICoreClientAPI capi,
    string slotKey,
    int? changeColor = null,
    int? refuseColor = null
  ) {
    _capi = capi;
    _slotId = ExHighlightSlots.Reserve(slotKey);
    _changeColor = changeColor ?? DefaultChangeColor;
    _refuseColor = refuseColor ?? DefaultRefuseColor;
    capi.Event.RegisterGameTickListener(_ => Watch(), 100);
    capi.Event.LeaveWorld += Clear;
  }

  /// <summary>Shows the preview of <paramref name="held"/> aimed at <paramref name="selection"/>.
  /// Call it every held tick: it does nothing while the selected block and the held item are the ones
  /// of the last call, and clears the tint when nothing is selected. An item whose click changes the world
  /// calls <see cref="Clear"/> after that click, so the next call recomputes against the changed blocks.</summary>
  /// <param name="player">The local player aiming.</param>
  /// <param name="held">The item driving the preview.</param>
  /// <param name="selection">The block looked at; null when none.</param>
  /// <param name="course">Called on a recompute with the selection; returns the cells the click would
  /// change and the cells that refuse it, in world positions. Either list may be empty.</param>
  public void Drive(
    IPlayer player,
    CollectibleObject held,
    BlockSelection? selection,
    System.Func<
      BlockSelection,
      (IReadOnlyList<BlockPos> Change, IReadOnlyList<BlockPos> Refuse)
    > course
  ) {
    _player = player;
    if (selection?.Position is not { } at) {
      Clear();
      return;
    }
    if (ReferenceEquals(_held, held) && at.Equals(_selected))
      return;

    var (change, refuse) = course(selection);
    var positions = new List<BlockPos>(change.Count + refuse.Count);
    var colors = new List<int>(positions.Capacity);
    foreach (BlockPos pos in change) {
      positions.Add(pos.Copy());
      colors.Add(_changeColor);
    }
    foreach (BlockPos pos in refuse) {
      positions.Add(pos.Copy());
      colors.Add(_refuseColor);
    }
    _held = held;
    _selected = at.Copy();
    _capi.World.HighlightBlocks(player, _slotId, positions, colors);
  }

  /// <summary>Removes the tint and forgets the last selection, so the next <see cref="Drive"/>
  /// recomputes. Does nothing when no tint is up.</summary>
  public void Clear() {
    if (_held == null || _player == null)
      return;
    _held = null;
    _selected = null;
    _capi.World.HighlightBlocks(_player, _slotId, [], []);
  }

  /// <summary>Clears the tint when the player no longer holds the item that drew it.</summary>
  internal void Watch() {
    if (_held == null || _player == null)
      return;
    CollectibleObject? now = _player
      .InventoryManager
      ?.ActiveHotbarSlot
      ?.Itemstack
      ?.Collectible;
    if (!ReferenceEquals(now, _held))
      Clear();
  }
}
