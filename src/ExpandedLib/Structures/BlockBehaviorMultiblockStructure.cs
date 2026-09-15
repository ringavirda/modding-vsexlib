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

  /// <summary>Resolves the incomplete multiblock anchor for the block at <paramref name="pos"/>, or null.</summary>
  public static BlockEntityMultiblockStructure? ResolveIncompleteAnchor(
    IWorldAccessor world,
    BlockPos pos
  ) {
    BlockEntityMultiblockStructure? anchor = world.BlockAccessor.GetBlockEntity(
      pos
    ) switch {
      BlockEntityMultiblockStructure structure => structure,
      IMultiblockComponent component => component.ResolveOwningAnchor(),
      _ => null,
    };
    return anchor is { StructureComplete: false } ? anchor : null;
  }

  /// <summary>
  /// Toggles the build-outline projection when the player gestures over an incomplete anchor at
  /// <paramref name="pos"/>; returns whether the click was consumed.
  /// </summary>
  public static bool TryToggleProjection(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockPos pos
  ) {
    if (
      !IsProjectionGesture(byPlayer)
      || ResolveIncompleteAnchor(world, pos) is not { } anchor
    )
      return false;

    anchor.Interact(byPlayer);
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
