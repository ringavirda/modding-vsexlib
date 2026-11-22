using System.Collections.Generic;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>
/// Client-side aiming preview of a <see cref="BlockFilledMegastructure"/> block item: tints the footprint the
/// placement would reserve green where clear and red where the room check refuses, in the colours of
/// <see cref="ExCoursePreview"/>. The block attaches it to itself on the client.
/// </summary>
[CollectibleBehaviorRegister("ExMegablockPreview", PrefixModId = false)]
public class CollectibleBehaviorMegablockPreview(CollectibleObject collObj)
  : CollectibleBehavior(collObj) {
  private ExCoursePreview? _preview;
  private string? _aimKey;

  /// <summary>The cells a placement of <paramref name="held"/> aimed at <paramref name="selection"/> would
  /// reserve, split by what the room check decides.</summary>
  /// <param name="world">The world the placement is in; read only.</param>
  /// <param name="held">The block item held; a block that is not a <see cref="BlockFilledMegastructure"/> gives
  /// two empty lists. It is turned to the variant its <see cref="BlockFilledMegastructure.PlacedBlock"/>
  /// returns, as its placement turns.</param>
  /// <param name="player">The player aiming; the placed variant is chosen for it.</param>
  /// <param name="selection">The block looked at and the face aimed at; the placement is on the aimed cell
  /// when the block there is replaceable by <paramref name="held"/>, else on the cell across the face.</param>
  /// <returns><c>Clear</c>: the principal cell and the reserved cells that take the footprint.
  /// <c>Refuse</c>: those that do not, or every one of them when the block's room check refuses with no single
  /// cell to blame. World positions; both lists are new.</returns>
  public static (
    IReadOnlyList<BlockPos> Clear,
    IReadOnlyList<BlockPos> Refuse
  ) Footprint(
    IWorldAccessor world,
    Block held,
    IPlayer player,
    BlockSelection selection
  ) {
    List<BlockPos> clear = [];
    List<BlockPos> refuse = [];
    if (held is not BlockFilledMegastructure start)
      return (clear, refuse);

    BlockSelection placing = PlacingSelection(world, held, selection);
    Block? placed = start.PlacedBlock(world, player, placing);
    if (placed is not BlockFilledMegastructure mega)
      return (clear, refuse);

    List<FillerCell> cells = mega.ReservedCellsAt(world, placing.Position);
    List<BlockPos> refusing = StructureFillers.Refusing(world, cells);
    Block principal = world.BlockAccessor.GetBlock(placing.Position);
    bool principalClear = principal.Id == 0 || principal.IsReplacableBy(placed);

    if (principalClear)
      clear.Add(placing.Position.Copy());
    else
      refuse.Add(placing.Position.Copy());
    foreach (FillerCell cell in cells) {
      if (cell.Pos.Equals(placing.Position))
        continue;
      (refusing.Contains(cell.Pos) ? refuse : clear).Add(cell.Pos.Copy());
    }

    if (refuse.Count == 0 && !mega.HasRoomAt(world, placing.Position)) {
      refuse.AddRange(clear);
      clear.Clear();
    }
    return (clear, refuse);
  }

  /// <summary>What the footprint of <paramref name="held"/> aimed at <paramref name="selection"/> depends on
  /// besides the aimed cell: the aimed face and the variant its <see cref="BlockFilledMegastructure.PlacedBlock"/>
  /// returns.</summary>
  /// <param name="world">The world the placement is in; read only.</param>
  /// <param name="held">The block item held.</param>
  /// <param name="player">The player aiming; the placed variant is chosen for it.</param>
  /// <param name="selection">The block looked at; null when none.</param>
  /// <returns>A key that differs whenever the footprint can; empty when nothing is selected.</returns>
  internal static string AimKey(
    IWorldAccessor world,
    Block held,
    IPlayer player,
    BlockSelection? selection
  ) {
    if (selection == null)
      return "";
    Block? placed = held is BlockFilledMegastructure mega
      ? mega.PlacedBlock(
        world,
        player,
        PlacingSelection(world, held, selection)
      )
      : null;
    return selection.Face?.Code + "|" + placed?.Code;
  }

  /// <summary>The selection a placement of <paramref name="held"/> is made with: the aimed cell when its block
  /// is replaceable by <paramref name="held"/>, else the cell across the face, with <c>DidOffset</c> set.</summary>
  private static BlockSelection PlacingSelection(
    IWorldAccessor world,
    Block held,
    BlockSelection selection
  ) {
    BlockSelection placing = selection.Clone();
    if (!world.BlockAccessor.GetBlock(placing.Position).IsReplacableBy(held)) {
      placing.Position = placing.Position.AddCopy(placing.Face);
      placing.DidOffset = true;
    }
    return placing;
  }

  /// <summary>Drives the preview of the held megablock from the held tick; built on the first call. Client side
  /// only.</summary>
  internal void Preview(ICoreClientAPI capi, IPlayer player) {
    _preview ??= new ExCoursePreview(
      capi,
      "exlib:megablock-preview-" + collObj.Code
    );
    BlockSelection? selection = player.CurrentBlockSelection;
    string key = AimKey(capi.World, (Block)collObj, player, selection);
    if (key != _aimKey)
      _preview.Clear();
    _aimKey = key;
    _preview.Drive(
      player,
      collObj,
      selection,
      sel => {
        (IReadOnlyList<BlockPos> clear, IReadOnlyList<BlockPos> refuse) =
          Footprint(capi.World, (Block)collObj, player, sel);
        return (clear, refuse);
      }
    );
  }

  /// <summary>Gives <paramref name="block"/> the behaviour when it has none and registers its held tick. Client
  /// side only; run from the block's <c>OnLoaded</c>.</summary>
  internal static void Attach(Block block, ICoreClientAPI capi) {
    var behavior =
      block.GetCollectibleBehavior<CollectibleBehaviorMegablockPreview>(false);
    if (behavior == null) {
      behavior = new CollectibleBehaviorMegablockPreview(block);
      behavior.Initialize(new JsonObject(new JObject()));
      block.CollectibleBehaviors = [.. block.CollectibleBehaviors, behavior];
    }
    capi.Event.RegisterGameTickListener(
      _ => {
        if (
          capi.World.Player is { } player
          && ReferenceEquals(
            player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible,
            block
          )
        )
          behavior.Preview(capi, player);
      },
      50
    );
  }
}
