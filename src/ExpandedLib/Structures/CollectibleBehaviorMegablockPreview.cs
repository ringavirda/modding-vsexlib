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

  /// <summary>The cells a placement of <paramref name="held"/> aimed at <paramref name="selection"/> would
  /// reserve, split by what the room check decides.</summary>
  /// <param name="world">The world the placement is in; read only.</param>
  /// <param name="held">The block item held; a block that is not a <see cref="BlockFilledMegastructure"/> gives
  /// two empty lists. A block declaring <c>ExOrientable</c> in player mode is turned to the variant the
  /// player's look picks, as its placement does.</param>
  /// <param name="player">The player aiming; its yaw picks the turn.</param>
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
    if (held is not BlockFilledMegastructure)
      return (clear, refuse);

    BlockSelection placing = selection.Clone();
    if (!world.BlockAccessor.GetBlock(placing.Position).IsReplacableBy(held)) {
      placing.Position = placing.Position.AddCopy(placing.Face);
      placing.DidOffset = true;
    }

    var orientable = held.GetBehavior<BlockBehaviorExOrientable>();
    Block placed = held;
    if (orientable is { IsNetworkOriented: false }) {
      placed =
        world.BlockAccessor.GetBlock(
          held.WithVariant(
            orientable.VariantKey,
            orientable.TokenFor(player, placing)
          )
        ) ?? held;
    }
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

  /// <summary>Drives the preview of the held megablock from the held tick; built on the first call. Client side
  /// only.</summary>
  internal void Preview(ICoreClientAPI capi, IPlayer player) {
    _preview ??= new ExCoursePreview(
      capi,
      "exlib:megablock-preview-" + collObj.Code.Path
    );
    _preview.Drive(
      player,
      collObj,
      player.CurrentBlockSelection,
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
    if (
      block.GetCollectibleBehavior<CollectibleBehaviorMegablockPreview>(false)
      != null
    )
      return;
    var behavior = new CollectibleBehaviorMegablockPreview(block);
    behavior.Initialize(new JsonObject(new JObject()));
    block.CollectibleBehaviors = [.. block.CollectibleBehaviors, behavior];
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
