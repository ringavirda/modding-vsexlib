using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace WasherPunch.Gui;

/// <summary>The drilling machine's window: the work slot, the bit slot and the chips slot.</summary>
public class GuiDialogWasherDrill : GuiDialogBlockEntity {
  public GuiDialogWasherDrill(
    string title,
    InventoryBase inventory,
    BlockPos pos,
    ICoreClientAPI capi
  )
    : base(title, inventory, pos, capi) {
    if (IsDuplicate)
      return;

    ElementBounds work = ElementStdBounds.SlotGrid(
      EnumDialogArea.None,
      0,
      40,
      1,
      1
    );
    ElementBounds tool = ElementStdBounds.SlotGrid(
      EnumDialogArea.None,
      60,
      40,
      1,
      1
    );
    ElementBounds chips = ElementStdBounds.SlotGrid(
      EnumDialogArea.None,
      180,
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
      .Gui.CreateCompo("washerdrill-" + BlockEntityPosition, dialog)
      .AddShadedDialogBG(bg)
      .AddDialogTitleBar(title, () => TryClose())
      .BeginChildElements(bg)
      .AddItemSlotGrid(Inventory, DoSendPacket, 1, [0], work)
      .AddItemSlotGrid(Inventory, DoSendPacket, 1, [1], tool)
      .AddItemSlotGrid(Inventory, DoSendPacket, 1, [2], chips)
      .EndChildElements()
      .Compose();
  }
}
