using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace WasherPunch.Gui;

/// <summary>The press's window: the plate slot beside the washer slot.</summary>
public class GuiDialogWasherPunch : GuiDialogBlockEntity {
  public GuiDialogWasherPunch(
    string title,
    InventoryBase inventory,
    BlockPos pos,
    ICoreClientAPI capi
  )
    : base(title, inventory, pos, capi) {
    if (IsDuplicate)
      return;

    ElementBounds input = ElementStdBounds.SlotGrid(
      EnumDialogArea.None,
      0,
      40,
      1,
      1
    );
    ElementBounds output = ElementStdBounds.SlotGrid(
      EnumDialogArea.None,
      120,
      40,
      1,
      1
    );
    ElementBounds bg = ElementBounds.Fill.WithFixedPadding(
      GuiStyle.ElementToDialogPadding
    );
    bg.BothSizing = ElementSizing.FitToChildren;
    ElementBounds dialog = ElementStdBounds
      .AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle)
      .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

    SingleComposer = capi
      .Gui.CreateCompo("washerpunch-" + BlockEntityPosition, dialog)
      .AddShadedDialogBG(bg)
      .AddDialogTitleBar(title, () => TryClose())
      .BeginChildElements(bg)
      .AddItemSlotGrid(Inventory, DoSendPacket, 1, [0], input)
      .AddItemSlotGrid(Inventory, DoSendPacket, 1, [1], output)
      .EndChildElements()
      .Compose();
  }
}
