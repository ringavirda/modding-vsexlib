using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Storage;

/// <summary>
/// Collectible behavior, listed ahead of GroundStorable, that refuses a Shift+right-click placement
/// of a stack whose <see cref="IStoredSize.PosedLength"/> exceeds <see cref="ExStorage.OneBlock"/>.
/// The collectible implements <see cref="IStoredSize"/>; a collectible that does not is never refused.
/// Property <c>langKey</c> names the lang entry shown, default <c>exlib:fitsoneblock-toolong</c>.
/// </summary>
[CollectibleBehaviorRegister]
public class ExFitsOneBlock(CollectibleObject collObj)
  : CollectibleBehavior(collObj) {
  private const string DefaultKey = "exlib:fitsoneblock-toolong";

  private string _langKey = DefaultKey;

  /// <inheritdoc/>
  public override void Initialize(JsonObject properties) {
    base.Initialize(properties);
    _langKey = properties["langKey"].AsString(DefaultKey);
  }

  /// <inheritdoc/>
  public override void OnHeldInteractStart(
    ItemSlot slot,
    EntityAgent byEntity,
    BlockSelection blockSel,
    EntitySelection entitySel,
    bool firstEvent,
    ref EnumHandHandling handHandling,
    ref EnumHandling handling
  ) {
    ItemStack? stack = slot.Itemstack;
    if (
      blockSel == null
      || stack == null
      || !byEntity.Controls.ShiftKey
      || stack.Collectible is not IStoredSize size
      || size.PosedLength(stack) <= ExStorage.OneBlock
    )
      return;

    (byEntity.World.Api as ICoreClientAPI)?.TriggerIngameError(
      this,
      "toolong",
      Lang.Get(_langKey)
    );
    handHandling = EnumHandHandling.PreventDefault;
    handling = EnumHandling.PreventSubsequent;
  }
}
