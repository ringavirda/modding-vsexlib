using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ExpandedLib.Assembling;

/// <summary>Marks a block as placed only by hand assembly; the def's <c>handAssembly</c> attribute carries the tool and fasteners. Declared first among a def's behaviours by <see cref="Definitions.ExBlockDef.HandAssembly"/>. A block class that overrides <c>TryPlaceBlock</c> calls <see cref="TryPlaceBlock"/> first and returns its result when <c>handling</c> is not <see cref="EnumHandling.PassThrough"/>.</summary>
[BlockBehaviorRegister("HandAssembly", PrefixModId = false)]
public class BlockBehaviorHandAssembly : BlockBehavior {
  /// <summary>Creates the behaviour for <paramref name="block"/>.</summary>
  /// <param name="block">The block the behaviour is declared on.</param>
  public BlockBehaviorHandAssembly(Block block)
    : base(block) { }

  /// <summary>The tool and fasteners the block declares, or null when its attribute is missing.</summary>
  public HandAssemblySpec? Spec => HandAssemblySpec.Of(block);

  /// <summary>Refuses a survival placement outside the assembly handler and, in creative, places the aim's first candidate fixed.</summary>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref EnumHandling handling,
    ref string failureCode
  ) {
    if (HandAssemblyGate.Refuses(world, byPlayer, itemstack, ref failureCode)) {
      handling = EnumHandling.PreventSubsequent;
      return false;
    }
    if (
      !HandAssemblyGate.PlacesFirst(
        world,
        byPlayer,
        itemstack,
        blockSel,
        ref failureCode,
        out bool placed
      )
    )
      return true;
    handling = EnumHandling.PreventSubsequent;
    return placed;
  }

  private ICoreAPI? _api;
  private ItemStack[]? _tools;

  /// <inheritdoc />
  public override void OnLoaded(ICoreAPI api) {
    base.OnLoaded(api);
    _api = api;
  }

  /// <summary>Adds the stepping and fixing gestures to the help of the held part; the fixing line shows the tools of <see cref="Spec"/>.</summary>
  public override WorldInteraction[] GetHeldInteractionHelp(
    ItemSlot inSlot,
    ref EnumHandling handling
  ) {
    if (_tools == null && _api != null && Spec is { } spec)
      _tools = spec.ToolStacks(_api.World);
    return
    [
      new WorldInteraction
      {
        ActionLangCode = ExlibLang.BlockhelpHandassemblyStep,
        MouseButton = EnumMouseButton.Right,
      },
      new WorldInteraction
      {
        ActionLangCode = ExlibLang.BlockhelpHandassemblyFix,
        MouseButton = EnumMouseButton.Right,
        HotKeyCode = "shift",
        Itemstacks = _tools,
      },
    ];
  }

  /// <summary>Adds the fit gesture to the help of a placed block that is an <see cref="IHandFitTarget"/> while the main hand holds an item it takes; the line shows the tools of <see cref="Spec"/>.</summary>
  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer,
    ref EnumHandling handled
  ) {
    handled = EnumHandling.PassThrough;
    if (
      Spec is not { } spec
      || forPlayer.InventoryManager.ActiveHotbarSlot.Itemstack is not { } held
      || HandFitAim.Of(world.BlockAccessor, selection.Position, held, out _)
        is null
    )
      return [];
    return
    [
      new WorldInteraction
      {
        ActionLangCode = ExlibLang.BlockhelpHandassemblyFit,
        MouseButton = EnumMouseButton.Right,
        HotKeyCode = "shift",
        Itemstacks = spec.ToolStacks(world),
      },
    ];
  }
}
