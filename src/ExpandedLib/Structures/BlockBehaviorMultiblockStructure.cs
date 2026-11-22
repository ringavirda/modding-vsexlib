using System.Collections.Generic;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>
/// Handles the multiblock build-outline projection: Ctrl+Shift+right-click toggles the hologram of
/// missing or incorrect blocks and contributes the help line.
/// </summary>
[BlockBehaviorRegister("MultiblockStructure", PrefixModId = false)]
public class BlockBehaviorMultiblockStructure : BlockBehavior {
  public BlockBehaviorMultiblockStructure(Block block)
    : base(block) { }

  /// <summary>Whether the player is making the build-outline gesture (Ctrl+Shift held).</summary>
  public static bool IsProjectionGesture(IPlayer? byPlayer) {
    var controls = byPlayer?.Entity?.Controls;
    return controls != null && controls.CtrlKey && controls.ShiftKey;
  }

  /// <summary>
  /// Resolves the incomplete multiblock anchor for the block at <paramref name="pos"/>, or null. A filler
  /// cell resolves through its <see cref="BlockEntityStructureFiller.Principal"/>, followed once.
  /// </summary>
  public static BlockEntityMultiblockStructure? ResolveIncompleteAnchor(
    IWorldAccessor world,
    BlockPos pos
  ) {
    BlockEntity? entity = world.BlockAccessor.GetBlockEntity(pos);
    if (entity is BlockEntityStructureFiller { Principal: { } principal })
      entity = world.BlockAccessor.GetBlockEntity(principal);
    BlockEntityMultiblockStructure? anchor = entity switch {
      BlockEntityMultiblockStructure structure => structure,
      IMultiblockComponent component => component.ResolveOwningAnchor(),
      _ => null,
    };
    return anchor is { StructureComplete: false } ? anchor : null;
  }

  /// <summary>The unfinished part whose outline this client shows and the player it is drawn for, null when
  /// none is. Client only and process-wide: one outline shows at a time in the shared highlight slot, so any
  /// other draw or clear of that slot resets it.</summary>
  internal static (BlockPos Principal, IPlayer Player)? ShownPart;

  /// <summary>
  /// The principal position and block of an unfinished staged part at <paramref name="pos"/>: the construction
  /// of the block there, or of the principal a filler cell there points to, with stages left to build.
  /// </summary>
  /// <returns>Null when the cell holds no such part.</returns>
  internal static (BlockPos Principal, Block Block)? UnfinishedPartAt(
    IWorldAccessor world,
    BlockPos pos
  ) {
    BlockPos principal = pos;
    BlockEntity? entity = world.BlockAccessor.GetBlockEntity(pos);
    if (entity is BlockEntityStructureFiller { Principal: { } linked }) {
      principal = linked;
      entity = world.BlockAccessor.GetBlockEntity(linked);
    }
    return
      entity?.GetBehavior<ExRightClickConstructable>() is { IsComplete: false }
      ? (principal.Copy(), world.BlockAccessor.GetBlock(principal))
      : null;
  }

  /// <summary>Draws, or clears when it is already drawn, the outline of the unfinished part at
  /// <paramref name="principal"/>: its whole footprint when the block is a megablock with filler offsets, else
  /// the principal cell alone, all in the open tint.</summary>
  private static void ToggleOutline(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockPos principal,
    Block block
  ) {
    if (ShownPart is { } shown && shown.Principal.Equals(principal)) {
      BlockEntityMultiblockStructure.HighlightOutline(world, byPlayer, []);
      return;
    }

    List<(BlockPos At, int Color, bool Principal)> outline =
      block is BlockFilledMegastructure host
      && StructureFillers.ReadOffsets(host.FillerOffsets).Count > 0
        ? StructureFillers.PartOutline(
          host,
          principal,
          host.StructureAngle,
          ProjectionColors.Open
        )
        : [(principal.Copy(), ProjectionColors.Open, true)];
    BlockEntityMultiblockStructure.HighlightOutline(world, byPlayer, outline);
    ShownPart = (principal.Copy(), byPlayer);
  }

  /// <summary>Clears the part outline when it shows the part at <paramref name="principal"/>, for a part that
  /// is finished or removed. Does nothing when another part or none is shown.</summary>
  internal static void DropPartOutline(IWorldAccessor world, BlockPos principal) {
    if (ShownPart is { } shown && shown.Principal.Equals(principal))
      BlockEntityMultiblockStructure.HighlightOutline(world, shown.Player, []);
  }

  /// <summary>
  /// Toggles the build-outline projection when the player gestures over an incomplete anchor at
  /// <paramref name="pos"/>, else over an unfinished staged part there that no incomplete anchor owns, which
  /// outlines the part's footprint (a second gesture on the same part clears it); returns whether the click was
  /// consumed.
  /// </summary>
  public static bool TryToggleProjection(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockPos pos
  ) {
    if (!IsProjectionGesture(byPlayer))
      return false;

    if (ResolveIncompleteAnchor(world, pos) is { } anchor)
      anchor.Interact(byPlayer);
    else if (UnfinishedPartAt(world, pos) is var (principal, block)) {
      if (world.Side == EnumAppSide.Client)
        ToggleOutline(world, byPlayer, principal, block);
    } else
      return false;

    (byPlayer as IClientPlayer)?.TriggerFpAnimation(
      EnumHandInteract.HeldItemInteract
    );
    return true;
  }

  /// <summary>
  /// Returns the Ctrl+Shift+right-click "show multiblock structure" help line, translated against
  /// <paramref name="forBlock"/>'s domain.
  /// </summary>
  public static WorldInteraction[] ProjectionHelp(Block forBlock) {
    string domainKey = forBlock.Code.Domain + ":blockhelp-mulblock-struc-show";
    return
    [
      new WorldInteraction
      {
        ActionLangCode = Lang.HasTranslation(domainKey)
          ? domainKey
          : "exlib:blockhelp-mulblock-struc-show",
        HotKeyCodes = ["ctrl", "shift"],
        MouseButton = EnumMouseButton.Right,
      },
    ];
  }

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref EnumHandling handling
  ) {
    if (TryToggleProjection(world, byPlayer, blockSel.Position)) {
      handling = EnumHandling.PreventSubsequent;
      return true;
    }

    handling = EnumHandling.PassThrough;
    return false;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer,
    ref EnumHandling handling
  ) =>
    ResolveIncompleteAnchor(world, selection.Position) == null
      ? []
      : ProjectionHelp(block);
}
